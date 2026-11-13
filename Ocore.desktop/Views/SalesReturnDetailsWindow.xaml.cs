
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ocore.desktop.Models;
using Ocore.desktop.Models.Printing;
using Ocore.desktop.Services;
using Ocore.desktop.Services.Printing;

namespace Ocore.desktop.Views
{
    public partial class SalesReturnDetailsWindow : Window
    {
        private readonly SalesReturnDetails returnDetails;
        private readonly PharmacySettingsService settingsService;

        private readonly PrinterConfigurationService printerConfigurationService;
        private readonly ThermalReceiptRenderer receiptRenderer;
        private readonly ThermalPrinterService thermalPrinterService;

        private PharmacySettings pharmacySettings;
        private PrinterConfiguration printerConfiguration;

        private FlowDocumentScrollViewer previewViewer;
        private FlowDocument currentPreviewDocument;
        private BitmapSource currentPreviewImage;

        private const int PreviewDpi = 203;

        private bool isPrinting;

        public SalesReturnDetailsWindow(SalesReturnDetails details)
        {
            InitializeComponent();

            settingsService = new PharmacySettingsService();

            printerConfigurationService =
                new PrinterConfigurationService();

            receiptRenderer = new ThermalReceiptRenderer();
            thermalPrinterService = new ThermalPrinterService();

            if (details == null)
            {
                MessageBox.Show(
                    "بيانات المرتجع غير متاحة.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
                return;
            }

            returnDetails = details;

            LoadReturnDetails();
            LoadPharmacySettings();
            LoadCashierConfiguration();
        }

        #region تحميل بيانات المرتجع

        private void LoadReturnDetails()
        {
            ReturnNumberText.Text =
                returnDetails.ReturnNumber ?? "-";

            InvoiceNumberText.Text =
                returnDetails.InvoiceNumber ?? "-";

            EmployeeNameText.Text =
                returnDetails.EmployeeName ?? "-";

            ReturnDateText.Text =
                returnDetails.CreatedAt.ToString(
                    "yyyy-MM-dd HH:mm");

            ReturnStatusText.Text =
                GetStatusDisplayName(returnDetails.Status);

            ReturnReasonText.Text =
                string.IsNullOrWhiteSpace(returnDetails.Reason)
                    ? "غير محدد"
                    : returnDetails.Reason;

            ItemsDataGrid.ItemsSource =
                returnDetails.Items;

            ReturnTotalText.Text =
                returnDetails.Total.ToString("N2") + " ج.م";
        }

        #endregion

        #region إعدادات الصيدلية

        private void LoadPharmacySettings()
        {
            try
            {
                pharmacySettings =
                    settingsService.GetSettingsForPrinting(
                        returnDetails.UserId);
            }
            catch (Exception ex)
            {
                pharmacySettings = null;

                MessageBox.Show(
                    "تعذر تحميل إعدادات الصيدلية:\n" +
                    ex.Message,
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        #endregion

        #region إعدادات طابعة الكاشير الجديدة

        private void LoadCashierConfiguration()
        {
            printerConfiguration =
                printerConfigurationService.GetConfiguration(
                    PrinterType.Cashier);

            if (printerConfiguration == null)
            {
                printerConfiguration =
                    PrinterConfigurationDefaults.Create(
                        PrinterType.Cashier);
            }
        }

        private double GetPaperWidth()
        {
            if (printerConfiguration != null &&
                (printerConfiguration.PaperWidth == 58 ||
                 printerConfiguration.PaperWidth == 80))
            {
                return printerConfiguration.PaperWidth;
            }

            return 58;
        }

        private double GetFontScale()
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

        private int GetPrintWidthDots()
        {
            if (printerConfiguration == null)
                return 0;

            int width = printerConfiguration.PrintWidthDots;

            if (width < 0 || width > 2048)
            {
                throw new InvalidOperationException(
                    "عرض الطباعة بالنقاط في إعدادات الكاشير غير صالح.");
            }

            return width;
        }

        private string GetPrinterName()
        {
            return printerConfiguration == null
                ? string.Empty
                : (printerConfiguration.PrinterName ?? "").Trim();
        }

        private string GetPrintMode()
        {
            return printerConfiguration == null
                ? string.Empty
                : (printerConfiguration.PrintMode ?? "").Trim();
        }

        #endregion

        #region حالة المرتجع

        private string GetStatusDisplayName(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "غير محدد";

            switch (status.Trim().ToLowerInvariant())
            {
                case "completed":
                    return "مكتمل";

                case "pending":
                    return "قيد الانتظار";

                case "cancelled":
                    return "ملغي";

                default:
                    return status;
            }
        }

        #endregion

        #region المعاينة

        private void PrintButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                OpenPrintPreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر فتح معاينة الإيصال:\n" +
                    ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenPrintPreview()
        {
            Window previewWindow = new Window
            {
                Title = "معاينة إيصال مرتجع البيع",
                Width = 520,
                Height = 750,
                MinWidth = 400,
                MinHeight = 550,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Owner = this,
                FlowDirection = FlowDirection.RightToLeft,
                Background = new SolidColorBrush(
                    Color.FromRgb(243, 246, 248))
            };

            DockPanel mainPanel = new DockPanel
            {
                Margin = new Thickness(12)
            };

            previewViewer = new FlowDocumentScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,

                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,

                IsToolBarVisible = false,
                Background = Brushes.White,
                Padding = new Thickness(10)
            };

            mainPanel.Children.Add(previewViewer);

            Button printButton = new Button
            {
                Content = "طباعة إيصال المرتجع",
                Height = 42,
                Margin = new Thickness(0, 10, 0, 0),
                Background = new SolidColorBrush(
                    Color.FromRgb(18, 61, 53)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            printButton.Click += async (s, e) =>
            {
                await PrintCurrentReturnAsync();
            };

            DockPanel.SetDock(printButton, Dock.Bottom);
            mainPanel.Children.Add(printButton);

            previewWindow.Content = mainPanel;

            RefreshPreview();

            previewWindow.ShowDialog();

            previewViewer = null;
            currentPreviewDocument = null;
            currentPreviewImage = null;
        }

        private void RefreshPreview()
        {
            if (previewViewer == null)
                return;

            try
            {
                double paperWidth = GetPaperWidth();
                double fontScale = GetFontScale();
                int printWidthDots = GetPrintWidthDots();

                FlowDocument document =
                    CreateReturnDocument(
                        paperWidth,
                        fontScale,
                        printWidthDots);

                BitmapSource previewImage =
                    receiptRenderer.RenderDocument(
                        document,
                        paperWidth,
                        PreviewDpi);

                // قص المساحة البيضاء الزائدة من أسفل المرتجع فقط.
                previewImage =
                    receiptRenderer.CropBottomWhitespace(
                        previewImage,
                        250,
                        16);

                if (previewImage == null ||
                    !previewImage.IsFrozen)
                {
                    throw new InvalidOperationException(
                        "تعذر تجهيز صورة معاينة المرتجع.");
                }

                previewViewer.Document = document;

                currentPreviewDocument = document;
                currentPreviewImage = previewImage;
            }
            catch (Exception ex)
            {
                currentPreviewDocument = null;
                currentPreviewImage = null;

                previewViewer.Document = null;

                MessageBox.Show(
                    "تعذر تحديث معاينة المرتجع:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region إنشاء مستند المرتجع

        private FlowDocument CreateReturnDocument(
            double paperWidth,
            double fontScale,
            int printWidthDots)
        {
            double width =
                printWidthDots > 0
                    ? printWidthDots * 96.0 / 203.0
                    : paperWidth * 96.0 / 25.4;

            FlowDocument document = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = new FontFamily("Arial"),
                FontSize = 11 * fontScale,
                PageWidth = width,
                PageHeight = 1000,
                PagePadding = new Thickness(12),
                ColumnWidth = width - 24,
                TextAlignment = TextAlignment.Center
            };

            AddPharmacyLogo(document);

            string pharmacyName =
                pharmacySettings == null ||
                string.IsNullOrWhiteSpace(
                    pharmacySettings.PharmacyName)
                    ? "الصيدلية"
                    : pharmacySettings.PharmacyName;

            AddCenteredParagraph(
                document,
                pharmacyName,
                17 * fontScale,
                true);

            if (pharmacySettings != null)
            {
                AddCenteredParagraph(
                    document,
                    pharmacySettings.Address,
                    9 * fontScale,
                    false);

                if (!string.IsNullOrWhiteSpace(
                    pharmacySettings.Phone))
                {
                    AddCenteredParagraph(
                        document,
                        "هاتف: " + pharmacySettings.Phone,
                        9 * fontScale,
                        false);
                }

                if (!string.IsNullOrWhiteSpace(
                    pharmacySettings.TaxNumber))
                {
                    AddCenteredParagraph(
                        document,
                        "الرقم الضريبي: " +
                        pharmacySettings.TaxNumber,
                        9 * fontScale,
                        false);
                }
            }

            AddSeparator(document);

            AddCenteredParagraph(
                document,
                "إيصال مرتجع بيع",
                15 * fontScale,
                true);

            AddSeparator(document);

            AddInfoParagraph(
                document,
                "رقم المرتجع: " + returnDetails.ReturnNumber,
                fontScale);

            AddInfoParagraph(
                document,
                "فاتورة البيع: " + returnDetails.InvoiceNumber,
                fontScale);

            AddInfoParagraph(
                document,
                "الموظف: " + returnDetails.EmployeeName,
                fontScale);

            AddInfoParagraph(
                document,
                "التاريخ: " +
                returnDetails.CreatedAt.ToString(
                    "yyyy-MM-dd HH:mm"),
                fontScale);

            AddInfoParagraph(
                document,
                "الحالة: " +
                GetStatusDisplayName(returnDetails.Status),
                fontScale);

            if (!string.IsNullOrWhiteSpace(returnDetails.Reason))
            {
                AddInfoParagraph(
                    document,
                    "السبب: " + returnDetails.Reason,
                    fontScale);
            }

            AddSeparator(document);

            AddCenteredParagraph(
                document,
                "تفاصيل الأصناف المرتجعة",
                11 * fontScale,
                true);

            if (returnDetails.Items != null)
            {
                foreach (SalesReturnItem item in returnDetails.Items)
                {
                    AddInfoParagraph(
                        document,
                        item.ItemName,
                        fontScale);

                    AddInfoParagraph(
                        document,
                        "الوحدة: " + item.ReturnUnitName,
                        fontScale);

                    AddInfoParagraph(
                        document,
                        "الكمية: " +
                        item.ReturnUnitQuantity.ToString("N2"),
                        fontScale);

                    AddInfoParagraph(
                        document,
                        "سعر الوحدة: " +
                        item.ReturnUnitPrice.ToString("N2") +
                        " ج.م",
                        fontScale);

                    AddInfoParagraph(
                        document,
                        "الإجمالي: " +
                        item.Total.ToString("N2") +
                        " ج.م",
                        fontScale);

                    AddSeparator(document);
                }
            }

            AddCenteredParagraph(
                document,
                "إجمالي المرتجع: " +
                returnDetails.Total.ToString("N2") +
                " ج.م",
                14 * fontScale,
                true);

            AddSeparator(document);

            AddCenteredParagraph(
                document,
                "نشكركم",
                11 * fontScale,
                true);

            return document;
        }

        #endregion

        #region شعار الصيدلية

        private void AddPharmacyLogo(FlowDocument document)
        {
            try
            {
                if (pharmacySettings == null ||
                    string.IsNullOrWhiteSpace(
                        pharmacySettings.LogoPath) ||
                    !File.Exists(pharmacySettings.LogoPath))
                {
                    return;
                }

                BitmapImage bitmap = new BitmapImage();

                bitmap.BeginInit();

                bitmap.UriSource =
                    new Uri(pharmacySettings.LogoPath);

                bitmap.CacheOption =
                    BitmapCacheOption.OnLoad;

                bitmap.EndInit();
                bitmap.Freeze();

                Image logo = new Image
                {
                    Source = bitmap,
                    Width = 75,
                    Height = 75,
                    Stretch = Stretch.Uniform
                };

                Paragraph paragraph = new Paragraph
                {
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                paragraph.Inlines.Add(
                    new InlineUIContainer(logo));

                document.Blocks.Add(paragraph);
            }
            catch
            {
                // تجاهل أخطاء تحميل الشعار.
            }
        }

        #endregion

        #region تنسيق المستند

        private void AddCenteredParagraph(
            FlowDocument document,
            string text,
            double fontSize,
            bool bold)
        {
            Paragraph paragraph = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                FontSize = fontSize,
                FontWeight = bold
                    ? FontWeights.Bold
                    : FontWeights.Normal,
                Margin = new Thickness(0, 3, 0, 3)
            };

            paragraph.Inlines.Add(
                new Run(text ?? ""));

            document.Blocks.Add(paragraph);
        }

        private void AddInfoParagraph(
            FlowDocument document,
            string text,
            double fontScale)
        {
            Paragraph paragraph = new Paragraph
            {
                TextAlignment = TextAlignment.Right,
                FontSize = 10 * fontScale,
                Margin = new Thickness(0, 2, 0, 2)
            };

            paragraph.Inlines.Add(
                new Run(text ?? ""));

            document.Blocks.Add(paragraph);
        }

        private void AddSeparator(FlowDocument document)
        {
            Paragraph paragraph = new Paragraph
            {
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            };

            paragraph.Inlines.Add(
                new Run("--------------------------------"));

            document.Blocks.Add(paragraph);
        }

        #endregion

        #region الطباعة الحرارية

        private async Task PrintCurrentReturnAsync()
        {
            if (isPrinting)
                return;

            if (currentPreviewDocument == null ||
                currentPreviewImage == null)
            {
                MessageBox.Show(
                    "معاينة المرتجع غير جاهزة للطباعة.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string printerName = GetPrinterName();

            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show(
                    "لم يتم اختيار طابعة الكاشير.\n\n" +
                    "افتح إعدادات الطابعات واختر الطابعة ثم احفظ الإعدادات.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string printMode = GetPrintMode();

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
                    "وضع الطباعة المحدد غير مدعوم.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (!thermalPrinterService.IsPrinterInstalled(printerName))
            {
                MessageBox.Show(
                    "الطابعة المحددة غير موجودة ضمن طابعات Windows:\n\n" +
                    printerName,
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                isPrinting = true;

                double paperWidth = GetPaperWidth();

                if (isWindowsDriver)
                {
                    await thermalPrinterService
                        .PrintImageWithWindowsDriverAsync(
                            printerName,
                            currentPreviewImage,
                            paperWidth);
                }
                else
                {
                    byte[] rasterData =
                        await receiptRenderer.RenderToEscPosAsync(
                            currentPreviewImage,
                            paperWidth,
                            PreviewDpi);

                    if (rasterData == null ||
                        rasterData.Length == 0)
                    {
                        throw new InvalidOperationException(
                            "تعذر إنشاء بيانات الطباعة الحرارية.");
                    }

                    byte[] printData =
                        BuildFinalPrintData(rasterData);

                    await thermalPrinterService.SendRawAsync(
                        printerName,
                        printData);
                }

                MessageBox.Show(
                    "تم إرسال إيصال المرتجع إلى الطابعة.",
                    "الطباعة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء طباعة إيصال المرتجع:\n\n" +
                    ex.Message,
                    "خطأ في الطباعة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                isPrinting = false;
            }
        }

        private byte[] BuildFinalPrintData(byte[] rasterData)
        {
            if (rasterData == null ||
                rasterData.Length == 0)
            {
                throw new ArgumentException(
                    "بيانات صورة المرتجع غير صالحة.",
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

        #region إغلاق النافذة

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}
