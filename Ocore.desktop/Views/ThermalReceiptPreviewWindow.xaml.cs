
using Ocore.desktop.Models;
using Ocore.desktop.Models.Printing;
using Ocore.desktop.Services;
using Ocore.desktop.Services.Printing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace Ocore.desktop.Views
{
    public partial class ThermalReceiptPreviewWindow : Window
    {
        private readonly ThermalReceiptDocumentBuilder documentBuilder;
        private readonly ThermalReceiptRenderer receiptRenderer;
        private readonly ThermalPrinterService thermalPrinterService;
        private readonly PrinterConfigurationService printerConfigurationService;

        private SaleInvoiceDetails currentInvoice;
        private PharmacySettings pharmacySettings;
        private PrinterConfiguration printerConfiguration;

        private FlowDocument currentPreviewDocument;
        private BitmapSource currentPreviewImage;

        private const int PreviewDpi = 203;

        private bool isPrinting;
        private bool isPreviewReady;

        public ThermalReceiptPreviewWindow()
        {
            InitializeComponent();

            documentBuilder = new ThermalReceiptDocumentBuilder();
            receiptRenderer = new ThermalReceiptRenderer();
            thermalPrinterService = new ThermalPrinterService();
            printerConfigurationService = new PrinterConfigurationService();

            PrintButton.IsEnabled = false;

            RefreshPreviewButton.Click += RefreshPreviewButton_Click;
            PrintButton.Click += PrintButton_Click;
            CloseButton.Click += CloseButton_Click;
        }

        // التوافق مع الاستدعاءات القديمة.
        public ThermalReceiptPreviewWindow(
            SaleInvoiceDetails invoice,
            PharmacySettings settings)
            : this(
                invoice,
                settings,
                PrintingFunction.CashierSales,
                0)
        {
        }

        // التوافق مع الاستدعاء السابق الذي لا يمرر رقم المستخدم.
        public ThermalReceiptPreviewWindow(
            SaleInvoiceDetails invoice,
            PharmacySettings settings,
            string selectedFunctionKey)
            : this(
                invoice,
                settings,
                selectedFunctionKey,
                0)
        {
        }

        // الاستدعاء مع رقم المستخدم ووظيفة الطباعة.
        // الاحتفاظ بالتوقيع للتوافق مع الشاشات الحالية.
        public ThermalReceiptPreviewWindow(
            SaleInvoiceDetails invoice,
            PharmacySettings settings,
            string selectedFunctionKey,
            int userId)
            : this()
        {
            currentInvoice = invoice;
            pharmacySettings = settings;

            LoadCashierConfiguration();
            RefreshPreview();
        }

        #region Cashier Configuration

        private void LoadCashierConfiguration()
        {
            printerConfiguration =
                printerConfigurationService.GetConfiguration(
                    PrinterType.Cashier);

            // الإعدادات الافتراضية للمعاينة فقط.
            // لا يتم حفظها تلقائيًا في قاعدة البيانات.
            if (printerConfiguration == null)
            {
                printerConfiguration =
                    PrinterConfigurationDefaults.Create(
                        PrinterType.Cashier);
            }
        }

        private double GetProfilePaperWidth()
        {
            if (printerConfiguration != null)
            {
                if (printerConfiguration.PaperWidth == 58 ||
                    printerConfiguration.PaperWidth == 80)
                {
                    return printerConfiguration.PaperWidth;
                }
            }

            // القيمة صفر تعني تلقائي.
            // نستخدم 58 مم للمعاينة دون تغيير القيمة المحفوظة.
            return 58;
        }

        private double GetProfileFontScale()
        {
            if (printerConfiguration == null)
                return 1.0;

            double scale = printerConfiguration.FontScale;

            if (double.IsNaN(scale) ||
                double.IsInfinity(scale) ||
                scale < 0.7 ||
                scale > 1.5)
            {
                return 1.0;
            }

            return scale;
        }

        private int GetProfilePrintWidthDots()
        {
            if (printerConfiguration == null)
                return 0;

            int width = printerConfiguration.PrintWidthDots;

            if (width < 0 || width > 2048)
            {
                throw new InvalidOperationException(
                    "عرض الطباعة بالنقاط في إعدادات طابعة الكاشير غير صالح.");
            }

            return width;
        }

        private string GetProfilePrinterName()
        {
            return printerConfiguration == null
                ? string.Empty
                : (printerConfiguration.PrinterName ?? "").Trim();
        }

        private string GetProfilePrintMode()
        {
            return printerConfiguration == null
                ? string.Empty
                : (printerConfiguration.PrintMode ?? "").Trim();
        }

        #endregion

        #region Preview

        private void RefreshPreviewButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            isPreviewReady = false;
            currentPreviewDocument = null;
            currentPreviewImage = null;

            UpdatePrintButtonState();

            if (currentInvoice == null ||
                pharmacySettings == null ||
                printerConfiguration == null)
            {
                ClearPreview();

                PreviewStatusText.Text =
                    "بيانات الفاتورة أو إعدادات الطباعة غير متاحة.";

                return;
            }

            try
            {
                PreviewStatusText.Text =
                    "جارٍ تجهيز المعاينة...";

                double paperWidth =
                    GetProfilePaperWidth();

                double fontScale =
                    GetProfileFontScale();

                int printWidthDots =
                    GetProfilePrintWidthDots();

                FlowDocument document =
                    documentBuilder.Build(
                        currentInvoice,
                        pharmacySettings,
                        paperWidth,
                        fontScale,
                        printWidthDots);

                BitmapSource previewImage =
                    receiptRenderer.RenderDocument(
                        document,
                        paperWidth,
                        PreviewDpi);

                if (previewImage == null)
                {
                    throw new InvalidOperationException(
                        "تعذر إنشاء صورة معاينة الإيصال.");
                }

                if (!previewImage.IsFrozen)
                {
                    throw new InvalidOperationException(
                        "صورة المعاينة غير جاهزة.");
                }

                ReceiptPreviewImage.Source =
                    previewImage;

                double imageWidth =
                    previewImage.PixelWidth * 96.0 / PreviewDpi;

                double imageHeight =
                    previewImage.PixelHeight * 96.0 / PreviewDpi;

                ReceiptPreviewImage.Width = imageWidth;
                ReceiptPreviewImage.Height = imageHeight;

                ReceiptPaperBorder.Width = imageWidth;
                ReceiptPaperBorder.Height = imageHeight;

                PreviewScrollViewer.ScrollToTop();

                currentPreviewDocument = document;
                currentPreviewImage = previewImage;

                isPreviewReady = true;

                PreviewStatusText.Text =
                    "تم تحديث المعاينة بنجاح - عرض الورق " +
                    paperWidth.ToString("0") +
                    " مم - ارتفاع الإيصال " +
                    (imageHeight * 25.4 / 96.0).ToString("0.0") +
                    " مم";

                UpdatePrintButtonState();
            }
            catch (Exception ex)
            {
                ClearPreview();

                isPreviewReady = false;
                currentPreviewDocument = null;
                currentPreviewImage = null;

                UpdatePrintButtonState();

                PreviewStatusText.Text =
                    "حدث خطأ أثناء تجهيز المعاينة.";

                MessageBox.Show(
                    "تعذر إنشاء معاينة الإيصال الحراري:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Print Button State

        private void UpdatePrintButtonState()
        {
            string printMode =
                GetProfilePrintMode();

            bool supportedMode =
                string.Equals(
                    printMode,
                    "EscPos",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    printMode,
                    "WindowsDriver",
                    StringComparison.OrdinalIgnoreCase);

            bool hasPrinter =
                !string.IsNullOrWhiteSpace(
                    GetProfilePrinterName());

            bool canPrint =
                isPreviewReady &&
                currentPreviewDocument != null &&
                currentPreviewImage != null &&
                !isPrinting &&
                currentInvoice != null &&
                pharmacySettings != null &&
                printerConfiguration != null &&
                hasPrinter &&
                supportedMode;

            if (PrintButton == null)
                return;

            PrintButton.IsEnabled = canPrint;

            if (!hasPrinter)
            {
                PrintButton.ToolTip =
                    "اختر طابعة الكاشير من إعدادات الطابعات أولاً.";
            }
            else if (string.Equals(
                printMode,
                "WindowsDriver",
                StringComparison.OrdinalIgnoreCase))
            {
                PrintButton.ToolTip =
                    "طباعة الإيصال باستخدام تعريف Windows.";
            }
            else if (string.Equals(
                printMode,
                "EscPos",
                StringComparison.OrdinalIgnoreCase))
            {
                PrintButton.ToolTip =
                    "طباعة الإيصال باستخدام ESC/POS RAW. " +
                    "القص وفتح درج النقود يعتمدان على دعم الطابعة.";
            }
            else
            {
                PrintButton.ToolTip =
                    "وضع الطباعة المحدد غير مدعوم.";
            }
        }

        #endregion

        #region Printing

        private async void PrintButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isPrinting)
                return;

            if (!isPreviewReady ||
                currentPreviewDocument == null ||
                currentPreviewImage == null ||
                currentInvoice == null ||
                pharmacySettings == null ||
                printerConfiguration == null)
            {
                MessageBox.Show(
                    "يجب تجهيز معاينة الإيصال أولاً.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string printerName =
                GetProfilePrinterName();

            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show(
                    "لم يتم اختيار طابعة الكاشير.\n\n" +
                    "افتح إعدادات الطابعات واختر الطابعة المطلوبة ثم احفظ الإعدادات.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string printMode =
                GetProfilePrintMode();

            bool isWindowsDriver =
                string.Equals(
                    printMode,
                    "WindowsDriver",
                    StringComparison.OrdinalIgnoreCase);

            bool isEscPos =
                string.Equals(
                    printMode,
                    "EscPos",
                    StringComparison.OrdinalIgnoreCase);

            if (!isWindowsDriver && !isEscPos)
            {
                MessageBox.Show(
                    "وضع الطباعة المحدد غير معروف.\n\n" +
                    "راجع إعدادات طابعة الكاشير.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (!thermalPrinterService.IsPrinterInstalled(printerName))
            {
                MessageBox.Show(
                    "الطابعة المحددة غير موجودة ضمن الطابعات المثبتة في Windows:\n\n" +
                    printerName +
                    "\n\nراجع اسم الطابعة من إعدادات الطابعات.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                isPrinting = true;
                UpdatePrintButtonState();

                double paperWidth =
                    GetProfilePaperWidth();

                BitmapSource receiptImage =
                    currentPreviewImage;

                PreviewStatusText.Text =
                    "جارٍ تجهيز الإيصال للطباعة...";

                if (isWindowsDriver)
                {
                    await PrintUsingWindowsDriverAsync(
                        printerName,
                        receiptImage,
                        paperWidth);
                }
                else
                {
                    await PrintUsingEscPosAsync(
                        printerName,
                        receiptImage,
                        paperWidth);
                }

                PreviewStatusText.Text =
                    "تم إرسال الإيصال إلى الطابعة.";

                MessageBox.Show(
                    "تم إرسال الإيصال إلى الطابعة بنجاح.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                PreviewStatusText.Text =
                    "تعذر إرسال الإيصال إلى الطابعة.";

                MessageBox.Show(
                    "حدث خطأ أثناء طباعة الإيصال الحراري:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                isPrinting = false;
                UpdatePrintButtonState();
            }
        }

        #endregion

        #region Windows Driver Printing

        private async Task PrintUsingWindowsDriverAsync(
            string printerName,
            BitmapSource receiptImage,
            double paperWidth)
        {
            if (receiptImage == null ||
                !receiptImage.IsFrozen)
            {
                throw new InvalidOperationException(
                    "صورة الإيصال غير جاهزة للطباعة.");
            }

            PreviewStatusText.Text =
                "جارٍ إرسال الإيصال إلى تعريف Windows...";

            await thermalPrinterService.PrintImageWithWindowsDriverAsync(
                printerName,
                receiptImage,
                paperWidth);
        }

        #endregion

        #region ESC/POS RAW Printing

        private async Task PrintUsingEscPosAsync(
            string printerName,
            BitmapSource receiptImage,
            double paperWidth)
        {
            if (receiptImage == null ||
                !receiptImage.IsFrozen)
            {
                throw new InvalidOperationException(
                    "صورة الإيصال غير جاهزة للطباعة.");
            }

            PreviewStatusText.Text =
                "جارٍ تحويل صورة الإيصال إلى بيانات ESC/POS...";

            byte[] rasterData =
                await receiptRenderer.RenderToEscPosAsync(
                    receiptImage,
                    paperWidth,
                    PreviewDpi);

            if (rasterData == null ||
                rasterData.Length == 0)
            {
                throw new InvalidOperationException(
                    "لم يتم إنشاء بيانات الطباعة الحرارية.");
            }

            byte[] printData =
                BuildFinalPrintData(rasterData);

            PreviewStatusText.Text =
                "جارٍ إرسال بيانات ESC/POS إلى الطابعة...";

            await thermalPrinterService.SendRawAsync(
                printerName,
                printData);
        }

        private byte[] BuildFinalPrintData(byte[] rasterData)
        {
            if (rasterData == null ||
                rasterData.Length == 0)
            {
                throw new ArgumentException(
                    "بيانات صورة الإيصال غير صالحة.",
                    nameof(rasterData));
            }

            List<byte> result =
                new List<byte>(rasterData.Length + 32);

            result.AddRange(rasterData);

            if (printerConfiguration.AutoCut)
            {
                result.AddRange(
                    thermalPrinterService.CreateFeedCommand(3));
            }

            if (printerConfiguration.OpenCashDrawer)
            {
                result.AddRange(
                    thermalPrinterService.CreateOpenCashDrawerCommand());
            }

            if (printerConfiguration.AutoCut)
            {
                result.AddRange(
                    thermalPrinterService.CreateCutCommand());
            }

            return result.ToArray();
        }

        #endregion

        #region Preview Helpers

        private void ClearPreview()
        {
            currentPreviewDocument = null;
            currentPreviewImage = null;

            ReceiptPreviewImage.Source = null;

            ReceiptPreviewImage.Width = 0;
            ReceiptPreviewImage.Height = 0;

            ReceiptPaperBorder.Width = 0;
            ReceiptPaperBorder.Height = 0;

            PreviewScrollViewer.ScrollToTop();
        }

        #endregion

        #region Window

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}
