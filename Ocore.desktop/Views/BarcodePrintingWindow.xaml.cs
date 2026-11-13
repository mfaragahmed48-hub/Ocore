
using Ocore.desktop.Models;
using Ocore.desktop.Models.Printing;
using Ocore.desktop.Services;
using Ocore.desktop.Services.Printing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ocore.desktop.Views
{
    public partial class BarcodePrintingWindow : Window
    {
        private readonly int currentUserId;
        private readonly ProductService productService;
        private readonly BarcodePrintService barcodePrintService;
        private readonly PrinterConfigurationService printerConfigurationService;

        private List<Product> allProducts;

        private PrinterConfiguration printerConfiguration;

        private readonly ObservableCollection<PrintQueueItem> printQueue =
            new ObservableCollection<PrintQueueItem>();

        public BarcodePrintingWindow(int currentUserId)
        {
            InitializeComponent();

            this.currentUserId = currentUserId;

            productService = new ProductService();
            barcodePrintService = new BarcodePrintService();
            printerConfigurationService = new PrinterConfigurationService();

            PrintQueueDataGrid.ItemsSource = printQueue;

            SearchTextBox.TextChanged += SearchTextBox_TextChanged;

            SearchButton.Click += SearchButton_Click;
            AddSelectedButton.Click += AddSelectedButton_Click;
            AddAllButton.Click += AddAllButton_Click;
            RemoveSelectedButton.Click += RemoveSelectedButton_Click;
            PreviewButton.Click += PreviewButton_Click;
            PrintButton.Click += PrintButton_Click;
            PrinterSettingsButton.Click += PrinterSettingsButton_Click;
            CloseButton.Click += CloseButton_Click;

            ProductsDataGrid.PreviewKeyDown +=
                ProductsDataGrid_PreviewKeyDown;

            PrintQueueDataGrid.PreviewKeyDown +=
                PrintQueueDataGrid_PreviewKeyDown;

            LoadPrinterConfiguration();
            LoadProducts();
        }

        #region Load Data

        private void LoadProducts()
        {
            try
            {
                allProducts =
                    productService.GetActiveProducts(currentUserId);

                ApplySearchFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحميل الأصناف.\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadPrinterConfiguration()
        {
            try
            {
                printerConfiguration =
                    printerConfigurationService.GetConfiguration(
                        PrinterType.BarcodeRoll);

                if (printerConfiguration == null)
                {
                    printerConfiguration =
                        PrinterConfigurationDefaults.Create(
                            PrinterType.BarcodeRoll);
                }

                DisplayPrinterConfiguration();
            }
            catch (Exception ex)
            {
                printerConfiguration = null;

                DisplayPrinterConfiguration();

                MessageBox.Show(
                    "تعذر تحميل إعدادات طابعة الباركود من قاعدة البيانات.\n\n" +
                    ex.Message,
                    "إعدادات طابعة الباركود",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void DisplayPrinterConfiguration()
        {
            if (printerConfiguration == null)
            {
                PrinterNameText.Text = "تعذر تحميل الإعدادات";
                LabelWidthText.Text = "--";
                LabelHeightText.Text = "--";
                return;
            }

            PrinterNameText.Text =
                string.IsNullOrWhiteSpace(printerConfiguration.PrinterName)
                ? "لم يتم تحديد طابعة"
                : printerConfiguration.PrinterName;

            LabelWidthText.Text =
                printerConfiguration.BarcodeLabelWidth.ToString(
                    "0.##",
                    CultureInfo.CurrentCulture) + " مم";

            LabelHeightText.Text =
                printerConfiguration.BarcodeLabelHeight.ToString(
                    "0.##",
                    CultureInfo.CurrentCulture) + " مم";

            ShowPriceCheckBoxState =
                printerConfiguration.BarcodeShowPrice;

            ShowProductCodeCheckBoxState =
                printerConfiguration.BarcodeShowProductCode;

            UpdatePreview();
        }

        private bool ShowPriceCheckBoxState
        {
            get;
            set;
        }

        private bool ShowProductCodeCheckBoxState
        {
            get;
            set;
        }

        private void PrinterSettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool saved =
                PrinterSettingsNavigationService.Open(this);

            if (saved)
            {
                LoadPrinterConfiguration();
            }
        }

        #endregion

        #region Search

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            if (allProducts == null)
                return;

            string search = SearchTextBox.Text.Trim();

            IEnumerable<Product> filtered = allProducts;

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = allProducts.Where(p =>
                    ContainsText(p.Name, search) ||
                    ContainsText(p.ProductCode, search) ||
                    ContainsText(p.Barcode, search));
            }

            ProductsDataGrid.ItemsSource = filtered.ToList();
        }

        private bool ContainsText(
            string value,
            string search)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(
                       search,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region Products Grid Keyboard Navigation

        private void ProductsDataGrid_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                MoveProductsGridVertically(
                    Keyboard.Modifiers == ModifierKeys.Shift);

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                AddCurrentProductToQueue();

                e.Handled = true;
            }
        }

        private void MoveProductsGridVertically(bool moveUp)
        {
            if (ProductsDataGrid.Items.Count == 0)
                return;

            int currentRowIndex =
                ProductsDataGrid.Items.IndexOf(
                    ProductsDataGrid.CurrentCell.Item);

            if (currentRowIndex < 0)
            {
                currentRowIndex =
                    ProductsDataGrid.Items.IndexOf(
                        ProductsDataGrid.SelectedItem);
            }

            if (currentRowIndex < 0)
                currentRowIndex = 0;

            int columnIndex = 0;

            if (ProductsDataGrid.CurrentCell.Column != null)
            {
                columnIndex =
                    ProductsDataGrid.CurrentCell.Column.DisplayIndex;
            }

            int targetRowIndex =
                moveUp
                ? currentRowIndex - 1
                : currentRowIndex + 1;

            if (targetRowIndex < 0 ||
                targetRowIndex >= ProductsDataGrid.Items.Count)
            {
                return;
            }

            DataGridColumn targetColumn =
                ProductsDataGrid.Columns.FirstOrDefault(
                    column => column.DisplayIndex == columnIndex);

            if (targetColumn == null)
                return;

            Product targetProduct =
                ProductsDataGrid.Items[targetRowIndex] as Product;

            if (targetProduct == null)
                return;

            ProductsDataGrid.SelectedItem = targetProduct;

            ProductsDataGrid.CurrentCell =
                new DataGridCellInfo(
                    targetProduct,
                    targetColumn);

            ProductsDataGrid.ScrollIntoView(
                targetProduct,
                targetColumn);
        }

        private void AddCurrentProductToQueue()
        {
            Product selectedProduct =
                ProductsDataGrid.CurrentCell.Item as Product;

            if (selectedProduct == null)
            {
                selectedProduct =
                    ProductsDataGrid.SelectedItem as Product;
            }

            if (selectedProduct == null)
            {
                MessageBox.Show(
                    "حدد صنفًا أولًا.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            AddProductToQueue(selectedProduct);

            UpdatePreview();
        }

        #endregion

        #region Print Queue

        private void AddSelectedButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedProducts =
                ProductsDataGrid.SelectedItems
                    .Cast<Product>()
                    .ToList();

            if (selectedProducts.Count == 0)
            {
                MessageBox.Show(
                    "حدد صنفًا واحدًا على الأقل.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            foreach (Product product in selectedProducts)
                AddProductToQueue(product);

            UpdatePreview();
        }

        private void AddAllButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (allProducts == null || allProducts.Count == 0)
            {
                MessageBox.Show(
                    "لا توجد أصناف لإضافتها.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            foreach (Product product in allProducts)
                AddProductToQueue(product);

            UpdatePreview();
        }

        private void AddProductToQueue(Product product)
        {
            if (product == null)
                return;

            PrintQueueItem existing =
                printQueue.FirstOrDefault(
                    p => p.Product.Id == product.Id);

            if (existing != null)
                return;

            printQueue.Add(new PrintQueueItem(product));
        }

        private void RemoveSelectedButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            RemoveSelectedPrintQueueItems();
        }

        private void PrintQueueDataGrid_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
                return;

            RemoveSelectedPrintQueueItems();

            e.Handled = true;
        }

        private void RemoveSelectedPrintQueueItems()
        {
            var selectedItems =
                PrintQueueDataGrid.SelectedItems
                    .Cast<PrintQueueItem>()
                    .Distinct()
                    .ToList();

            if (selectedItems.Count == 0)
            {
                PrintQueueItem currentItem =
                    PrintQueueDataGrid.CurrentCell.Item
                    as PrintQueueItem;

                if (currentItem != null)
                    selectedItems.Add(currentItem);
            }

            if (selectedItems.Count == 0)
                return;

            foreach (PrintQueueItem item in selectedItems)
                printQueue.Remove(item);

            PrintQueueDataGrid.SelectedItem = null;

            UpdatePreview();
        }

        #endregion

        #region Preview

        private void PreviewButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (printerConfiguration == null)
            {
                ShowPreviewMessage(
                    "إعدادات طابعة الباركود غير متاحة.");

                return;
            }

            PrintQueueItem selected =
                PrintQueueDataGrid.SelectedItem as PrintQueueItem;

            if (selected == null && printQueue.Count > 0)
                selected = printQueue[0];

            if (selected == null)
            {
                ShowPreviewMessage(
                    "أضف صنفًا لمعاينة الملصق.");

                return;
            }

            double width =
                printerConfiguration.BarcodeLabelWidth;

            double height =
                printerConfiguration.BarcodeLabelHeight;

            if (!IsValidDimension(width) ||
                !IsValidDimension(height))
            {
                ShowPreviewMessage(
                    "مقاسات الملصق المحفوظة غير صحيحة.");

                return;
            }

            try
            {
                DrawingVisual visual =
                    barcodePrintService.CreateLabelVisual(
                        selected.Product,
                        width,
                        height,
                        ShowPriceCheckBoxState,
                        ShowProductCodeCheckBoxState);

                // أبعاد العرض الفعلية على الشاشة بوحدة DIP
                double pixelWidth = width * 96.0 / 25.4;
                double pixelHeight = height * 96.0 / 25.4;

                // رفع دقة المعاينة إلى 300 DPI
                const double previewDpi = 300.0;

                int bitmapWidth = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        width * previewDpi / 25.4));

                int bitmapHeight = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        height * previewDpi / 25.4));

                RenderTargetBitmap bitmap =
                    new RenderTargetBitmap(
                        bitmapWidth,
                        bitmapHeight,
                        previewDpi,
                        previewDpi,
                        PixelFormats.Pbgra32);

                bitmap.Render(visual);
                bitmap.Freeze();

                PreviewBorder.Width = pixelWidth;
                PreviewBorder.Height = pixelHeight;

                Image previewImage = new Image
                {
                    Source = bitmap,
                    Width = pixelWidth,
                    Height = pixelHeight,
                    Stretch = Stretch.Fill,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                RenderOptions.SetBitmapScalingMode(
                    previewImage,
                    BitmapScalingMode.HighQuality);

                PreviewContainer.Children.Clear();
                PreviewContainer.Children.Add(previewImage);
            }
            catch (Exception ex)
            {
                ShowPreviewMessage(
                    "تعذر إنشاء المعاينة.");

                MessageBox.Show(
                    ex.Message,
                    "خطأ في معاينة الباركود",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private bool IsValidDimension(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   value > 0 &&
                   value <= 200;
        }

        private void ShowPreviewMessage(string message)
        {
            PreviewContainer.Children.Clear();

            PreviewContainer.Children.Add(
                new TextBlock
                {
                    Text = message,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                });
        }

        #endregion

        #region Print

        private void PrintButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (printQueue.Count == 0)
            {
                MessageBox.Show(
                    "قائمة الطباعة فارغة. أضف أصنافًا أولًا.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (printerConfiguration == null)
            {
                MessageBox.Show(
                    "تعذر تحميل إعدادات طابعة الباركود.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            PrintQueueDataGrid.CommitEdit(
                DataGridEditingUnit.Cell,
                true);

            PrintQueueDataGrid.CommitEdit(
                DataGridEditingUnit.Row,
                true);

            foreach (PrintQueueItem item in printQueue)
            {
                if (item.Product == null || item.LabelCount <= 0)
                {
                    MessageBox.Show(
                        "تأكد من صحة الأصناف وعدد الملصقات. يجب أن يكون العدد أكبر من صفر.",
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }
            }

            double width =
                printerConfiguration.BarcodeLabelWidth;

            double height =
                printerConfiguration.BarcodeLabelHeight;

            double gap =
                printerConfiguration.BarcodeGap;

            double offsetX =
                printerConfiguration.BarcodeOffsetX;

            double offsetY =
                printerConfiguration.BarcodeOffsetY;

            if (!IsValidDimension(width) ||
                !IsValidDimension(height) ||
                !IsValidNumber(gap) ||
                !IsValidNumber(offsetX) ||
                !IsValidNumber(offsetY) ||
                gap < 0 || gap > 50 ||
                offsetX < -50 || offsetX > 50 ||
                offsetY < -50 || offsetY > 50)
            {
                MessageBox.Show(
                    "إعدادات مقاس أو مسافة أو إزاحة الملصق غير صحيحة. راجع إعدادات طابعة الباركود.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string printerName =
                printerConfiguration.PrinterName;

            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show(
                    "لم يتم تحديد طابعة باركود.\nافتح إعدادات الطابعات وحدد طابعة الباركود أولًا.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                bool printerInstalled =
                    PrinterSettings.InstalledPrinters
                        .Cast<string>()
                        .Any(name => string.Equals(
                            name,
                            printerName,
                            StringComparison.OrdinalIgnoreCase));

                if (!printerInstalled)
                {
                    MessageBox.Show(
                        "الطابعة المحفوظة غير موجودة ضمن طابعات Windows المثبتة:\n\n" +
                        printerName +
                        "\n\nافتح إعدادات الطابعات واختر طابعة أخرى.",
                        "طابعة الباركود غير متاحة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var items = printQueue
                    .Select(item => Tuple.Create(
                        item.Product,
                        item.LabelCount))
                    .ToList();

                FixedDocument document =
                    barcodePrintService.CreateRollDocument(
                        items,
                        width,
                        height,
                        gap,
                        offsetX,
                        offsetY,
                        printerConfiguration.BarcodeShowPrice,
                        printerConfiguration.BarcodeShowProductCode);

                double pageWidthDip =
                    width * 96.0 / 25.4;

                double pageHeightDip =
                    ((height * 2) + gap) * 96.0 / 25.4;

                LocalPrintServer localPrintServer =
                    new LocalPrintServer();

                System.Printing.PrintQueue printQueueWindows =
                    localPrintServer.GetPrintQueue(printerName);

                printQueueWindows.Refresh();

                System.Windows.Controls.PrintDialog printDialog =
                    new System.Windows.Controls.PrintDialog();

                printDialog.PrintQueue = printQueueWindows;

                PrintTicket ticket = new PrintTicket
                {
                    PageMediaSize =
                        new PageMediaSize(
                            pageWidthDip,
                            pageHeightDip),

                    PageOrientation =
                        PageOrientation.Portrait
                };

                printDialog.PrintTicket = ticket;

                printDialog.PrintDocument(
                    document.DocumentPaginator,
                    "O CORE - Barcode Labels Roll");

                MessageBox.Show(
                    "تم إرسال ملصقات الرول إلى الطابعة:\n\n" +
                    printerName +
                    "\n\nتأكد من أن تعريف الطابعة يدعم مقاس الملصق واتجاه الطباعة المحددين.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر إنشاء مستند الطباعة أو إرساله إلى الطابعة.\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool IsValidNumber(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
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

        #region Helper Classes

        public class PrintQueueItem
        {
            public Product Product { get; private set; }

            public string ProductCode
            {
                get { return Product.ProductCode; }
            }

            public string Name
            {
                get { return Product.Name; }
            }

            public string Barcode
            {
                get { return Product.Barcode; }
            }

            public int LabelCount { get; set; }

            public PrintQueueItem(Product product)
            {
                Product = product;
                LabelCount = 1;
            }
        }

        #endregion
    }
}
