
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Printing;
using Ocore.desktop.Models;
using Ocore.desktop.Models.Printing;
using Ocore.desktop.Services;
using Ocore.desktop.Services.Printing;

namespace Ocore.desktop.Views
{
    public partial class PurchaseInvoiceDetailsWindow : Window
    {
        private readonly int currentUserId;
        private readonly int invoiceId;

        private readonly PurchaseService purchaseService;
        private readonly PharmacySettingsService settingsService;
        private readonly PrinterConfigurationService printerConfigurationService;

        private PurchaseInvoice currentInvoice;
        private PharmacySettings pharmacySettings;
        private PrinterConfiguration printerConfiguration;

        private FlowDocumentScrollViewer previewViewer;
        private Window previewWindow;

        private double currentFontSize = 13;
        private double currentPaperWidth = 793.7;
        private double currentPaperHeight = 1122.5;

        public PurchaseInvoiceDetailsWindow(
            int userId,
            int selectedInvoiceId)
        {
            InitializeComponent();

            currentUserId = userId;
            invoiceId = selectedInvoiceId;

            purchaseService = new PurchaseService();
            settingsService = new PharmacySettingsService();
            printerConfigurationService = new PrinterConfigurationService();

            Loaded += PurchaseInvoiceDetailsWindow_Loaded;
        }

        private void PurchaseInvoiceDetailsWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            LoadInvoiceDetails();
        }

        private void LoadInvoiceDetails()
        {
            try
            {
                var invoice = purchaseService.GetInvoiceDetails(
                    currentUserId,
                    invoiceId);

                if (invoice == null)
                {
                    MessageBox.Show(
                        "لم يتم العثور على الفاتورة.",
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    Close();
                    return;
                }

                currentInvoice = invoice;

                InvoiceNumberText.Text = invoice.InvoiceNumber ?? "-";
                SupplierNameText.Text = invoice.SupplierName ?? "-";

                InvoiceDateText.Text =
                    invoice.InvoiceDate.ToString("dd/MM/yyyy");

                InvoiceItemsDataGrid.ItemsSource = invoice.Items;

                InvoiceTotalText.Text =
                    invoice.Total.ToString("N2");

                pharmacySettings =
                    settingsService.GetSettingsForPrinting(currentUserId);
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("تحميل تفاصيل فاتورة الشراء", ex);
            }
        }

        #region Normal Printer Configuration

        private void LoadNormalPrinterConfiguration()
        {
            printerConfiguration =
                printerConfigurationService.GetConfiguration(
                    PrinterType.Normal);

            // إعدادات للمعاينة فقط عند عدم وجود إعداد محفوظ.
            // لا يتم إنشاء سجل أو حفظ أي إعداد تلقائيًا.
            if (printerConfiguration == null)
            {
                printerConfiguration =
                    PrinterConfigurationDefaults.Create(
                        PrinterType.Normal);
            }
        }

        private void ApplyNormalPrinterSettings()
        {
            if (printerConfiguration == null)
                throw new InvalidOperationException(
                    "إعدادات الطابعة العادية غير متاحة.");

            double width;
            double height;

            switch ((printerConfiguration.PaperSize ?? "A4").Trim())
            {
                case "A5":
                    width = 559.37;
                    height = 793.70;
                    break;

                case "Letter":
                    width = 816.00;
                    height = 1056.00;
                    break;

                case "A4":
                default:
                    width = 793.70;
                    height = 1122.50;
                    break;
            }

            if (string.Equals(
                printerConfiguration.Orientation,
                "Landscape",
                StringComparison.OrdinalIgnoreCase))
            {
                currentPaperWidth = height;
                currentPaperHeight = width;
            }
            else
            {
                currentPaperWidth = width;
                currentPaperHeight = height;
            }

            double fontScale = printerConfiguration.FontScale;

            if (double.IsNaN(fontScale) ||
                double.IsInfinity(fontScale) ||
                fontScale < 0.7 ||
                fontScale > 1.5)
            {
                fontScale = 1.0;
            }

            currentFontSize = 13.0 * fontScale;
        }

        private Thickness GetConfiguredPageMargins()
        {
            if (printerConfiguration == null)
                return new Thickness(8);

            // التحويل من ملليمتر إلى وحدات WPF (DIP).
            const double millimeterToDip = 96.0 / 25.4;

            return new Thickness(
                printerConfiguration.MarginLeft * millimeterToDip,
                printerConfiguration.MarginTop * millimeterToDip,
                printerConfiguration.MarginRight * millimeterToDip,
                printerConfiguration.MarginBottom * millimeterToDip);
        }

        private string GetConfiguredPrinterName()
        {
            return printerConfiguration == null
                ? string.Empty
                : (printerConfiguration.PrinterName ?? "").Trim();
        }

        #endregion

        private void PrintInvoiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (currentInvoice == null)
            {
                MessageBox.Show(
                    "لا توجد فاتورة جاهزة للطباعة.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                LoadNormalPrinterConfiguration();

                previewViewer = new FlowDocumentScrollViewer
                {
                    IsToolBarVisible = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                Button settingsButton = new Button
                {
                    Content = "⚙ إعدادات الطابعات",
                    Width = 190,
                    Height = 42,
                    Margin = new Thickness(10),
                    FontWeight = FontWeights.Bold
                };

                Button printButton = new Button
                {
                    Content = "🖨 طباعة الفاتورة",
                    Width = 220,
                    Height = 42,
                    Margin = new Thickness(10),
                    Background = new SolidColorBrush(
                        Color.FromRgb(22, 134, 93)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    BorderThickness = new Thickness(0)
                };

                StackPanel buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                buttonsPanel.Children.Add(settingsButton);
                buttonsPanel.Children.Add(printButton);

                DockPanel panel = new DockPanel();

                DockPanel.SetDock(buttonsPanel, Dock.Bottom);

                panel.Children.Add(buttonsPanel);
                panel.Children.Add(previewViewer);

                previewWindow = new Window
                {
                    Title = "O CORE - معاينة فاتورة الشراء",
                    Width = 1000,
                    Height = 800,
                    MinWidth = 700,
                    MinHeight = 600,
                    WindowStartupLocation =
                        WindowStartupLocation.CenterOwner,
                    Owner = this,
                    FlowDirection = FlowDirection.RightToLeft,
                    Background = new SolidColorBrush(
                        Color.FromRgb(243, 246, 248)),
                    Content = panel
                };

                settingsButton.Click += (s, args) =>
                {
                    bool saved =
                        PrinterSettingsNavigationService.Open(previewWindow);

                    if (saved)
                    {
                        try
                        {
                            LoadNormalPrinterConfiguration();
                            RefreshPurchasePreview();
                        }
                        catch (Exception ex)
                        {
                            Ocore.desktop.ErrorDialog.Show("تحديث معاينة فاتورة الشراء", ex);
                        }
                    }
                };

                printButton.Click += (s, args) =>
                {
                    PrintCurrentInvoice();
                };

                RefreshPurchasePreview();

                previewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("تجهيز معاينة طباعة فاتورة الشراء", ex);
            }
        }

        private void RefreshPurchasePreview()
        {
            if (previewViewer == null)
                return;

            previewViewer.Document = CreateInvoiceDocument();
        }

        private FlowDocument CreateInvoiceDocument()
        {
            ApplyNormalPrinterSettings();

            FlowDocument document = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = new FontFamily("Arial"),
                FontSize = currentFontSize,
                PageWidth = currentPaperWidth,
                PageHeight = currentPaperHeight,
                PagePadding = GetConfiguredPageMargins(),
                ColumnWidth = double.PositiveInfinity
            };

            // إضافة شعار الصيدلية
            AddPharmacyLogo(document);

            string pharmacyName =
                pharmacySettings == null ||
                string.IsNullOrWhiteSpace(pharmacySettings.PharmacyName)
                    ? "O CORE - نظام إدارة الصيدلية"
                    : pharmacySettings.PharmacyName;

            Paragraph title = new Paragraph(
                new Run(pharmacyName))
            {
                FontSize = currentFontSize * 1.5,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };

            document.Blocks.Add(title);

            if (pharmacySettings != null &&
                !string.IsNullOrWhiteSpace(pharmacySettings.Address))
            {
                document.Blocks.Add(
                    CreateCenteredParagraph(
                        "العنوان: " + pharmacySettings.Address));
            }

            if (pharmacySettings != null &&
                !string.IsNullOrWhiteSpace(pharmacySettings.Phone))
            {
                document.Blocks.Add(
                    CreateCenteredParagraph(
                        "هاتف: " + pharmacySettings.Phone));
            }


            Paragraph subtitle = new Paragraph(
                new Run("فاتورة شراء"))
            {
                FontSize = currentFontSize * 1.35,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 15, 0, 20)
            };

            document.Blocks.Add(subtitle);

            document.Blocks.Add(
                CreateInfoParagraph(
                    "رقم الفاتورة: ",
                    InvoiceNumberText.Text));

            document.Blocks.Add(
                CreateInfoParagraph(
                    "اسم المورد: ",
                    SupplierNameText.Text));

            document.Blocks.Add(
                CreateInfoParagraph(
                    "تاريخ الفاتورة: ",
                    InvoiceDateText.Text));

            Paragraph itemsTitle = new Paragraph(
                new Run("تفاصيل الأصناف"))
            {
                FontSize = currentFontSize * 1.15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 18, 0, 8)
            };

            document.Blocks.Add(itemsTitle);

            Table table = new Table
            {
                CellSpacing = 0,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5)
            };

            for (int i = 0; i < 7; i++)
            {
                table.Columns.Add(
                    new TableColumn
                    {
                        Width = new GridLength(
                            1,
                            GridUnitType.Star)
                    });
            }

            TableRowGroup headerGroup = new TableRowGroup();
            table.RowGroups.Add(headerGroup);

            TableRow headerRow = new TableRow
            {
                Background = new SolidColorBrush(
                    Color.FromRgb(36, 107, 88))
            };

            headerGroup.Rows.Add(headerRow);

            AddCell(headerRow, "الصنف", true);
            AddCell(headerRow, "الوحدة", true);
            AddCell(headerRow, "التشغيلة", true);
            AddCell(headerRow, "الصلاحية", true);
            AddCell(headerRow, "الكمية", true);
            AddCell(headerRow, "سعر الوحدة", true);
            AddCell(headerRow, "الإجمالي", true);

            TableRowGroup bodyGroup = new TableRowGroup();
            table.RowGroups.Add(bodyGroup);

            if (currentInvoice.Items != null)
            {
                foreach (var item in currentInvoice.Items)
                {
                    TableRow row = new TableRow();
                    bodyGroup.Rows.Add(row);

                    AddCell(row, item.ItemName);
                    AddCell(row, item.UnitName);
                    AddCell(row, item.BatchNumber);
                    AddCell(row, item.ExpiryDateText);
                    AddCell(row, FormatNumber(item.Quantity));
                    AddCell(row, FormatNumber(item.UnitPrice));
                    AddCell(row, FormatNumber(item.Total));
                }
            }

            document.Blocks.Add(table);

            Paragraph totalParagraph = new Paragraph(
                new Run(
                    "إجمالي الفاتورة: " +
                    InvoiceTotalText.Text))
            {
                FontSize = currentFontSize * 1.35,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(0, 25, 0, 10)
            };

            document.Blocks.Add(totalParagraph);

            Paragraph footer = new Paragraph(
                new Run("شكراً لتعاملكم معنا"))
            {
                FontSize = currentFontSize * 0.85,
                Foreground = Brushes.Gray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0)
            };

            document.Blocks.Add(footer);

            return document;
        }

        private void AddPharmacyLogo(FlowDocument document)
        {
            try
            {
                if (pharmacySettings == null ||
                    string.IsNullOrWhiteSpace(pharmacySettings.LogoPath))
                {
                    return;
                }

                string logoPath = pharmacySettings.LogoPath;

                if (!File.Exists(logoPath))
                    return;

                BitmapImage bitmap = new BitmapImage();

                bitmap.BeginInit();
                bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 300;
                bitmap.EndInit();
                bitmap.Freeze();

                Image logoImage = new Image
                {
                    Source = bitmap,
                    Width = 85,
                    Height = 85,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                Paragraph logoParagraph = new Paragraph
                {
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                logoParagraph.Inlines.Add(
                    new InlineUIContainer(logoImage));

                document.Blocks.Add(logoParagraph);
            }
            catch
            {
                // عدم وجود الشعار أو تعذر قراءته لا يمنع تجهيز الفاتورة.
            }
        }

        private void PrintCurrentInvoice()
        {
            string printerName = GetConfiguredPrinterName();

            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show(
                    "لم يتم اختيار الطابعة العادية.\n\n" +
                    "افتح إعدادات الطابعات واختر الطابعة المطلوبة ثم احفظ الإعدادات.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                using (LocalPrintServer printServer =
                    new LocalPrintServer())
                {
                    PrintQueue printQueue =
                        printServer.GetPrintQueue(printerName);

                    if (printQueue == null)
                    {
                        MessageBox.Show(
                            "الطابعة المحددة غير متاحة على الجهاز:\n\n" +
                            printerName,
                            "O CORE",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        return;
                    }

                    PrintTicket printTicket =
                        printQueue.DefaultPrintTicket;

                    if (printTicket == null)
                    {
                        printTicket = new PrintTicket();
                    }

                    ApplyNormalPrinterSettings();

                    printTicket.PageOrientation =
                        string.Equals(
                            printerConfiguration.Orientation,
                            "Landscape",
                            StringComparison.OrdinalIgnoreCase)
                            ? PageOrientation.Landscape
                            : PageOrientation.Portrait;

                    try
                    {
                        printTicket.PageMediaSize =
                            new PageMediaSize(
                                currentPaperWidth,
                                currentPaperHeight);
                    }
                    catch
                    {
                        // بعض تعريفات الطابعات لا تدعم تحديد المقاس برمجيًا.
                    }

                    PrintDialog printDialog = new PrintDialog
                    {
                        PrintQueue = printQueue,
                        PrintTicket = printTicket
                    };

                    IDocumentPaginatorSource paginatorSource =
                        previewViewer.Document;

                    printDialog.PrintDocument(
                        paginatorSource.DocumentPaginator,
                        "O CORE - فاتورة شراء");

                    MessageBox.Show(
                        "تم إرسال الفاتورة إلى الطابعة:\n" +
                        printerName,
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("طباعة فاتورة الشراء", ex);
            }
        }

        private Paragraph CreateCenteredParagraph(string text)
        {
            return new Paragraph(new Run(text))
            {
                FontSize = currentFontSize * 0.85,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };
        }

        private Paragraph CreateInfoParagraph(
            string label,
            string value)
        {
            Paragraph paragraph = new Paragraph
            {
                FontSize = currentFontSize,
                Margin = new Thickness(0, 4, 0, 4)
            };

            paragraph.Inlines.Add(
                new Run(label)
                {
                    FontWeight = FontWeights.Bold
                });

            paragraph.Inlines.Add(
                new Run(value ?? "-"));

            return paragraph;
        }

        private void AddCell(
            TableRow row,
            string value,
            bool isHeader = false)
        {
            Paragraph paragraph = new Paragraph(
                new Run(value ?? "-"))
            {
                TextAlignment = TextAlignment.Center,
                FontSize = currentFontSize * 0.75,
                Margin = new Thickness(2)
            };

            if (isHeader)
            {
                paragraph.FontWeight = FontWeights.Bold;
                paragraph.Foreground = Brushes.White;
            }

            TableCell cell = new TableCell(paragraph)
            {
                Padding = new Thickness(3),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5)
            };

            row.Cells.Add(cell);
        }

        private string FormatNumber(object value)
        {
            if (value == null)
                return "0.00";

            try
            {
                return Convert.ToDecimal(
                    value,
                    CultureInfo.CurrentCulture).ToString("N2");
            }
            catch
            {
                return Convert.ToString(value) ?? "";
            }
        }

        #region Invoice Barcode Printing


        private void PrintInvoiceBarcodesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (currentInvoice == null ||
                currentInvoice.Items == null ||
                currentInvoice.Items.Count == 0)
            {
                MessageBox.Show(
                    "لا توجد أصناف في الفاتورة لطباعة باركودها.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            try
            {
                Dictionary<int, double> packageQuantities =
                    new Dictionary<int, double>();

                // حساب إجمالي كمية كل كارت، شامل المجاني،
                // وتحويل الأشرطة إلى علب قبل التجميع.
                foreach (PurchaseInvoiceItem item in currentInvoice.Items)
                {
                    if (item == null || item.ProductId <= 0)
                    {
                        throw new InvalidOperationException(
                            "يوجد بند في الفاتورة لا يحتوي على كود صنف صحيح.");
                    }

                    if (double.IsNaN(item.Quantity) ||
                        double.IsInfinity(item.Quantity) ||
                        double.IsNaN(item.FreeQuantity) ||
                        double.IsInfinity(item.FreeQuantity) ||
                        item.Quantity < 0 ||
                        item.FreeQuantity < 0)
                    {
                        throw new InvalidOperationException(
                            "توجد كمية غير صحيحة في الصنف: " +
                            (item.ItemName ?? ""));
                    }

                    double totalQuantity =
                        item.Quantity + item.FreeQuantity;

                    if (double.IsNaN(totalQuantity) ||
                        double.IsInfinity(totalQuantity))
                    {
                        throw new InvalidOperationException(
                            "إجمالي الكمية غير صالح للصنف: " +
                            (item.ItemName ?? ""));
                    }

                    bool isSmallUnit =
                        !string.IsNullOrWhiteSpace(item.UnitName) &&
                        !string.IsNullOrWhiteSpace(item.SmallUnitName) &&
                        string.Equals(
                            item.UnitName.Trim(),
                            item.SmallUnitName.Trim(),
                            StringComparison.OrdinalIgnoreCase);

                    double packageQuantity;

                    if (isSmallUnit)
                    {
                        if (item.UnitsPerPackage <= 0)
                        {
                            throw new InvalidOperationException(
                                "عدد الوحدات داخل العلبة غير صحيح للصنف: " +
                                (item.ItemName ?? ""));
                        }

                        packageQuantity =
                            totalQuantity / item.UnitsPerPackage;
                    }
                    else
                    {
                        packageQuantity = totalQuantity;
                    }

                    if (double.IsNaN(packageQuantity) ||
                        double.IsInfinity(packageQuantity) ||
                        packageQuantity < 0)
                    {
                        throw new InvalidOperationException(
                            "تعذر تحويل كمية الصنف إلى علب: " +
                            (item.ItemName ?? ""));
                    }

                    if (packageQuantities.ContainsKey(item.ProductId))
                    {
                        packageQuantities[item.ProductId] +=
                            packageQuantity;
                    }
                    else
                    {
                        packageQuantities.Add(
                            item.ProductId,
                            packageQuantity);
                    }

                    if (double.IsNaN(packageQuantities[item.ProductId]) ||
                        double.IsInfinity(packageQuantities[item.ProductId]))
                    {
                        throw new InvalidOperationException(
                            "إجمالي كمية الصنف أكبر من النطاق المسموح: " +
                            (item.ItemName ?? ""));
                    }
                }

                // تحميل بيانات كروت الأصناف حسب ProductId.
                ProductService productService = new ProductService();

                List<Product> products =
                    productService.GetProductsForManagement(currentUserId);

                Dictionary<int, Product> productsById =
                    products
                        .Where(p => p != null &&
                                    packageQuantities.ContainsKey(p.Id))
                        .GroupBy(p => p.Id)
                        .ToDictionary(g => g.Key, g => g.First());

                List<Tuple<Product, int>> printItems =
                    new List<Tuple<Product, int>>();

                foreach (KeyValuePair<int, double> entry in packageQuantities)
                {
                    double normalizedQuantity =
                        Math.Round(
                            entry.Value,
                            8,
                            MidpointRounding.AwayFromZero);

                    double roundedCount =
                        Math.Ceiling(normalizedQuantity);

                    if (roundedCount <= 0)
                        continue;

                    if (roundedCount > int.MaxValue)
                    {
                        throw new InvalidOperationException(
                            "عدد الملصقات كبير جدًا لكارت الصنف رقم: " +
                            entry.Key);
                    }

                    Product product;

                    if (!productsById.TryGetValue(
                        entry.Key,
                        out product))
                    {
                        throw new InvalidOperationException(
                            "تعذر تحميل بيانات كارت الصنف رقم: " +
                            entry.Key);
                    }

                    printItems.Add(
                        Tuple.Create(
                            product,
                            checked((int)roundedCount)));
                }

                if (printItems.Count == 0)
                {
                    MessageBox.Show(
                        "لا توجد كميات تنتج عنها ملصقات للطباعة.",
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                // عرض نافذة اختيار الأصناف.
                // كل الأصناف محددة تلقائيًا عند فتح النافذة.
                List<Tuple<Product, int>> selectedItems =
                    ShowBarcodeSelectionWindow(printItems);

                if (selectedItems == null || selectedItems.Count == 0)
                    return;

                BarcodePrintService barcodePrintService =
                    new BarcodePrintService();

                // التحقق من الباركود للأصناف المختارة فقط.
                foreach (Tuple<Product, int> item in selectedItems)
                {
                    if (!barcodePrintService.IsValidEan13(item.Item1.Barcode))
                    {
                        throw new InvalidOperationException(
                            "باركود الصنف المختار غير صحيح أو ليس EAN-13:\n\n" +
                            (item.Item1.Name ?? "") +
                            " - كود الصنف: " +
                            (item.Item1.ProductCode ?? ""));
                    }
                }

                // استخدام إعدادات طابعة الباركود الحالية دون تعديلها.
                PrinterConfiguration barcodeConfiguration =
                    printerConfigurationService.GetConfiguration(
                        PrinterType.BarcodeRoll);

                if (barcodeConfiguration == null)
                {
                    barcodeConfiguration =
                        PrinterConfigurationDefaults.Create(
                            PrinterType.BarcodeRoll);
                }

                double labelWidth =
                    barcodeConfiguration.BarcodeLabelWidth;

                double labelHeight =
                    barcodeConfiguration.BarcodeLabelHeight;

                double gap =
                    barcodeConfiguration.BarcodeGap;

                double offsetX =
                    barcodeConfiguration.BarcodeOffsetX;

                double offsetY =
                    barcodeConfiguration.BarcodeOffsetY;

                if (!IsValidBarcodeDimension(labelWidth) ||
                    !IsValidBarcodeDimension(labelHeight) ||
                    !IsValidBarcodeNumber(gap) ||
                    !IsValidBarcodeNumber(offsetX) ||
                    !IsValidBarcodeNumber(offsetY) ||
                    gap < 0 || gap > 50 ||
                    offsetX < -50 || offsetX > 50 ||
                    offsetY < -50 || offsetY > 50)
                {
                    throw new InvalidOperationException(
                        "إعدادات مقاس أو مسافة أو إزاحة ملصق الباركود غير صحيحة. راجع إعدادات طابعة الباركود.");
                }

                string printerName =
                    (barcodeConfiguration.PrinterName ?? "").Trim();

                if (string.IsNullOrWhiteSpace(printerName))
                {
                    MessageBox.Show(
                        "لم يتم تحديد طابعة باركود. افتح إعدادات الطابعات وحدد طابعة الباركود أولًا.",
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                bool printerInstalled =
                    System.Drawing.Printing.PrinterSettings.InstalledPrinters
                        .Cast<string>()
                        .Any(name => string.Equals(
                            name,
                            printerName,
                            StringComparison.OrdinalIgnoreCase));

                if (!printerInstalled)
                {
                    MessageBox.Show(
                        "الطابعة المحفوظة غير موجودة ضمن طابعات Windows المثبتة:\n\n" +
                        printerName,
                        "طابعة الباركود غير متاحة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                long totalLabels =
                    selectedItems.Aggregate(
                        0L,
                        (total, item) => checked(total + item.Item2));

                bool confirmed =
                    MessageBox.Show(
                        "سيتم طباعة " + totalLabels +
                        " ملصق باركود لعدد " + selectedItems.Count +
                        " كارت صنف.\n\nهل تريد المتابعة؟",
                        "تأكيد طباعة باركود الفاتورة",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes;

                if (!confirmed)
                    return;

                FixedDocument document =
                    barcodePrintService.CreateRollDocument(
                        selectedItems,
                        labelWidth,
                        labelHeight,
                        gap,
                        offsetX,
                        offsetY,
                        barcodeConfiguration.BarcodeShowPrice,
                        barcodeConfiguration.BarcodeShowProductCode);

                double pageHeightDip =
                    ((labelHeight * 2) + gap) * 96.0 / 25.4;

                using (LocalPrintServer localPrintServer =
                    new LocalPrintServer())
                {
                    PrintQueue windowsPrintQueue =
                        localPrintServer.GetPrintQueue(printerName);

                    windowsPrintQueue.Refresh();

                    System.Windows.Controls.PrintDialog printDialog =
                        new System.Windows.Controls.PrintDialog();

                    printDialog.PrintQueue = windowsPrintQueue;

                    printDialog.PrintTicket = new PrintTicket
                    {
                        PageMediaSize =
                            new PageMediaSize(
                                labelWidth * 96.0 / 25.4,
                                pageHeightDip),

                        PageOrientation =
                            PageOrientation.Portrait
                    };

                    printDialog.PrintDocument(
                        document.DocumentPaginator,
                        "O CORE - Purchase Invoice Barcodes");
                }

                MessageBox.Show(
                    "تم إرسال " + totalLabels +
                    " ملصق باركود إلى الطابعة:\n\n" +
                    printerName,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("طباعة باركود أصناف فاتورة الشراء", ex);
            }
        }

        private List<Tuple<Product, int>> ShowBarcodeSelectionWindow(
            List<Tuple<Product, int>> items)
        {
            Window selectionWindow = new Window
            {
                Title = "اختيار أصناف طباعة الباركود",
                Width = 520,
                Height = 600,
                MinWidth = 420,
                MinHeight = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                FlowDirection = FlowDirection.RightToLeft,
                ResizeMode = ResizeMode.CanResize,
                Background = Brushes.White
            };

            StackPanel mainPanel = new StackPanel
            {
                Margin = new Thickness(15)
            };

            TextBlock titleText = new TextBlock
            {
                Text = "حدد الأصناف التي تريد طباعة باركودها",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(24, 63, 54)),
                Margin = new Thickness(0, 0, 0, 10)
            };

            TextBlock instructionText = new TextBlock
            {
                Text = "كل الأصناف محددة تلقائيًا. أزل علامة الاختيار من أي صنف لا تريد طباعته.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 12)
            };

            StackPanel itemsPanel = new StackPanel
            {
                Margin = new Thickness(5)
            };

            List<CheckBox> checkBoxes = new List<CheckBox>();

            TextBlock totalText = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(22, 134, 93)),
                Margin = new Thickness(0, 12, 0, 12)
            };

            Button printButton = new Button
            {
                Content = "متابعة الطباعة",
                Width = 140,
                Height = 38,
                Margin = new Thickness(5),
                Background = new SolidColorBrush(
                    Color.FromRgb(22, 134, 93)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            Button cancelButton = new Button
            {
                Content = "إلغاء",
                Width = 100,
                Height = 38,
                Margin = new Thickness(5)
            };

            Action updateTotal = () =>
            {
                long count = 0;
                int selectedCount = 0;

                foreach (CheckBox checkBox in checkBoxes)
                {
                    if (checkBox.IsChecked == true)
                    {
                        Tuple<Product, int> item =
                            checkBox.Tag as Tuple<Product, int>;

                        if (item != null)
                        {
                            count += item.Item2;
                            selectedCount++;
                        }
                    }
                }

                totalText.Text =
                    "عدد الأصناف المختارة: " + selectedCount +
                    "    |    إجمالي الملصقات: " + count;

                printButton.IsEnabled = selectedCount > 0;
            };

            foreach (Tuple<Product, int> item in items)
            {
                CheckBox checkBox = new CheckBox
                {
                    IsChecked = true,
                    Tag = item,
                    Margin = new Thickness(5, 8, 5, 8),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    Content =
                        (item.Item1.Name ?? "صنف بدون اسم") +
                        "   |   كود: " +
                        (item.Item1.ProductCode ?? "-") +
                        "   |   عدد الملصقات: " +
                        item.Item2
                };

                checkBoxes.Add(checkBox);

                itemsPanel.Children.Add(checkBox);
            }

            foreach (CheckBox checkBox in checkBoxes)
            {
                checkBox.Checked += (s, e) => updateTotal();
                checkBox.Unchecked += (s, e) => updateTotal();
            }

            ScrollViewer scrollViewer = new ScrollViewer
            {
                Content = itemsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 390
            };

            StackPanel buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            buttonsPanel.Children.Add(printButton);
            buttonsPanel.Children.Add(cancelButton);

            mainPanel.Children.Add(titleText);
            mainPanel.Children.Add(instructionText);
            mainPanel.Children.Add(scrollViewer);
            mainPanel.Children.Add(totalText);
            mainPanel.Children.Add(buttonsPanel);

            selectionWindow.Content = mainPanel;

            bool confirmed = false;

            printButton.Click += (s, e) =>
            {
                confirmed = true;
                selectionWindow.DialogResult = true;
            };

            cancelButton.Click += (s, e) =>
            {
                selectionWindow.DialogResult = false;
            };

            updateTotal();

            selectionWindow.ShowDialog();

            if (!confirmed)
                return null;

            return checkBoxes
                .Where(c => c.IsChecked == true)
                .Select(c => c.Tag as Tuple<Product, int>)
                .Where(item => item != null)
                .ToList();
        }


        private static bool IsValidBarcodeDimension(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   value > 0 &&
                   value <= 200;
        }

        private static bool IsValidBarcodeNumber(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        #endregion

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
