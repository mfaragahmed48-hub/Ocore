
using Ocore.desktop.Models;
using Ocore.desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ocore.desktop.Views
{
    public partial class PurchaseWindow : Window
    {
        private readonly int currentUserId;

        private readonly SupplierService supplierService;
        private readonly ProductService productService;
        private readonly PurchaseService purchaseService;

        private readonly ObservableCollection<PurchaseInvoiceItem> invoiceItems;

        private List<Product> allProducts;
        private ICollectionView productsView;
        private TextBox productEditableTextBox;

        private string pendingInternationalBarcode;
        private int? scannedProductId;

        private readonly Dictionary<PurchaseInvoiceItem, double>
            packagePrices = new Dictionary<PurchaseInvoiceItem, double>();

        private ScaleTransform zoomTransform;

        private const double MinimumZoom = 0.75;
        private const double MaximumZoom = 1.50;
        private const double ZoomStep = 0.10;

        public PurchaseWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;

            InitializeZoom();

            supplierService = new SupplierService();
            productService = new ProductService();
            purchaseService = new PurchaseService();

            invoiceItems = new ObservableCollection<PurchaseInvoiceItem>();
            allProducts = new List<Product>();

            InvoiceDatePicker.SelectedDate = DateTime.Today;

            InvoiceItemsGrid.ItemsSource = invoiceItems;

            ProductComboBox.SelectionChanged +=
                ProductComboBox_SelectionChanged;

            ProductComboBox.Loaded +=
                ProductComboBox_Loaded;

            PurchaseUnitComboBox.SelectionChanged +=
                PurchaseUnitComboBox_SelectionChanged;

            InvoiceItemsGrid.CellEditEnding +=
                InvoiceItemsGrid_CellEditEnding;

            LoadSuppliers();
            LoadProducts();
            UpdateTotal();
        }

        private void InitializeZoom()
        {
            zoomTransform = new ScaleTransform(1.0, 1.0);
            InvoiceItemsGrid.LayoutTransform = zoomTransform;
            PreviewMouseWheel += PurchaseWindow_PreviewMouseWheel;
        }

        private void PurchaseWindow_PreviewMouseWheel(
            object sender,
            System.Windows.Input.MouseWheelEventArgs e)
        {
            if ((System.Windows.Input.Keyboard.Modifiers &
                 System.Windows.Input.ModifierKeys.Control) !=
                System.Windows.Input.ModifierKeys.Control)
                return;

            if (zoomTransform == null)
                return;

            double newZoom = zoomTransform.ScaleX;

            if (e.Delta > 0)
                newZoom += ZoomStep;
            else if (e.Delta < 0)
                newZoom -= ZoomStep;

            newZoom = Math.Max(
                MinimumZoom,
                Math.Min(MaximumZoom, newZoom));

            zoomTransform.ScaleX = newZoom;
            zoomTransform.ScaleY = newZoom;

            e.Handled = true;
        }

        private void LoadSuppliers()
        {
            try
            {
                SupplierComboBox.ItemsSource =
                    supplierService.GetSuppliers(currentUserId);

                if (SupplierComboBox.Items.Count > 0)
                    SupplierComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل الموردين:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadProducts()
        {
            try
            {
                allProducts =
                    productService.GetActiveProducts(currentUserId);

                productsView =
                    CollectionViewSource.GetDefaultView(allProducts);

                productsView.Filter = FilterProducts;

                ProductComboBox.ItemsSource = productsView;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل الأصناف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool FilterProducts(object item)
        {
            var product = item as Product;

            if (product == null)
                return false;

            string searchText =
                (ProductComboBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            bool nameMatches =
                !string.IsNullOrWhiteSpace(product.Name) &&
                product.Name.IndexOf(
                    searchText,
                    StringComparison.CurrentCultureIgnoreCase) >= 0;

            bool codeMatches =
                !string.IsNullOrWhiteSpace(product.ProductCode) &&
                product.ProductCode.IndexOf(
                    searchText,
                    StringComparison.CurrentCultureIgnoreCase) >= 0;

            return nameMatches || codeMatches;
        }

        private void ProductComboBox_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            ProductComboBox.ApplyTemplate();

            productEditableTextBox =
                ProductComboBox.Template.FindName(
                    "PART_EditableTextBox",
                    ProductComboBox) as TextBox;

            if (productEditableTextBox != null)
            {
                productEditableTextBox.TextChanged -=
                    ProductSearchTextBox_TextChanged;

                productEditableTextBox.TextChanged +=
                    ProductSearchTextBox_TextChanged;
            }
        }

        private void ProductSearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (productsView == null)
                return;

            productsView.Refresh();

            if (ProductComboBox.Items.Count > 0)
                ProductComboBox.IsDropDownOpen = true;
        }

        private void BarcodeScanTextBox_KeyDown(
            object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                ProcessScannedBarcode();
            }
        }

        private void ReadBarcodeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ProcessScannedBarcode();
        }

        private void ProcessScannedBarcode()
        {
            string scannedValue =
                (BarcodeScanTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(scannedValue))
            {
                BarcodeStatusTextBlock.Text =
                    "امسح الباركود أولاً.";

                BarcodeScanTextBox.Focus();
                return;
            }

            pendingInternationalBarcode = null;
            scannedProductId = null;
            LinkPendingBarcodeButton.IsEnabled = false;

            try
            {
                GS1BarcodeData barcodeData =
                    GS1BarcodeService.Parse(scannedValue);

                int? productId =
                    productService.FindProductIdByBarcode(
                        barcodeData.GTIN);

                if (productId.HasValue)
                {
                    Product product = allProducts.Find(
                        p => p.Id == productId.Value);

                    if (product == null)
                    {
                        LoadProducts();

                        product = allProducts.Find(
                            p => p.Id == productId.Value);
                    }

                    if (product == null)
                    {
                        throw new InvalidOperationException(
                            "تم العثور على الباركود، لكن تعذر تحميل بيانات الصنف.");
                    }

                    scannedProductId = product.Id;

                    ProductComboBox.SelectedItem = product;
                    ProductComboBox.Text = product.Name;

                    if (!string.IsNullOrWhiteSpace(
                        barcodeData.BatchNumber))
                    {
                        BatchNumberTextBox.Text =
                            barcodeData.BatchNumber;
                    }

                    if (barcodeData.ExpiryDate.HasValue)
                    {
                        ExpiryDatePicker.SelectedDate =
                            barcodeData.ExpiryDate.Value.Date;
                    }

                    BarcodeStatusTextBlock.Text =
                        "تم التعرف على الصنف: " +
                        product.Name +
                        " - GTIN: " +
                        barcodeData.GTIN +
                        (barcodeData.BatchNumber != null
                            ? " - التشغيلة: " + barcodeData.BatchNumber
                            : "") +
                        (barcodeData.ExpiryDate.HasValue
                            ? " - الصلاحية: " +
                              barcodeData.ExpiryDate.Value.ToString("yyyy-MM-dd")
                            : "");

                    BarcodeScanTextBox.Clear();
                    ProductComboBox.Focus();
                }
                else
                {
                    pendingInternationalBarcode =
                        barcodeData.GTIN;

                    BarcodeStatusTextBlock.Text =
                        "هذا الباركود غير مرتبط بأي صنف. اختر الصنف يدويًا، ثم اضغط (ربط بالصنف المحدد)."
                        + " GTIN: " + barcodeData.GTIN;

                    if (!string.IsNullOrWhiteSpace(
                        barcodeData.BatchNumber))
                    {
                        BatchNumberTextBox.Text =
                            barcodeData.BatchNumber;
                    }

                    if (barcodeData.ExpiryDate.HasValue)
                    {
                        ExpiryDatePicker.SelectedDate =
                            barcodeData.ExpiryDate.Value.Date;
                    }

                    LinkPendingBarcodeButton.IsEnabled = true;

                    BarcodeScanTextBox.Clear();
                    ProductComboBox.Focus();
                }
            }
            catch (Exception ex)
            {
                scannedProductId = null;
                pendingInternationalBarcode = null;
                LinkPendingBarcodeButton.IsEnabled = false;

                BarcodeStatusTextBlock.Text =
                    "تعذر قراءة الباركود: " + ex.Message;

                MessageBox.Show(
                    "تعذر قراءة الباركود:\n" + ex.Message,
                    "باركود غير صالح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                BarcodeScanTextBox.Focus();
                BarcodeScanTextBox.SelectAll();
            }
        }

        private void LinkPendingBarcodeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(
                pendingInternationalBarcode))
            {
                MessageBox.Show(
                    "لا يوجد باركود جديد ينتظر الربط.");
                return;
            }

            Product selectedProduct =
                ProductComboBox.SelectedItem as Product;

            if (selectedProduct == null ||
                !string.Equals(
                    (ProductComboBox.Text ?? "").Trim(),
                    (selectedProduct.Name ?? "").Trim(),
                    StringComparison.CurrentCultureIgnoreCase))
            {
                MessageBox.Show(
                    "اختر الصنف الصحيح من القائمة أولاً.",
                    "اختيار الصنف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                ProductComboBox.Focus();
                return;
            }

            string barcodeToLink =
                pendingInternationalBarcode;

            var confirmation = MessageBox.Show(
                "هل تريد ربط الباركود التالي بالصنف المحدد؟\n\n"
                + "الصنف: " + selectedProduct.Name + "\n"
                + "الباركود: " + barcodeToLink + "\n\n"
                + "لن يمكن ربط هذا الباركود بصنف آخر بعد تأكيد العملية.",
                "تأكيد ربط الباركود",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                productService.LinkInternationalBarcode(
                    currentUserId,
                    selectedProduct.Id,
                    barcodeToLink);

                scannedProductId = selectedProduct.Id;
                pendingInternationalBarcode = null;
                LinkPendingBarcodeButton.IsEnabled = false;

                BarcodeStatusTextBlock.Text =
                    "تم ربط الباركود بنجاح بالصنف: " +
                    selectedProduct.Name;

                MessageBox.Show(
                    "تم ربط الباركود بالصنف بنجاح.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                BarcodeScanTextBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر ربط الباركود:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ProductComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            var product =
                ProductComboBox.SelectedItem as Product;

            PurchaseUnitComboBox.ItemsSource = null;
            PurchaseUnitComboBox.IsEnabled = false;
            UnitPriceTextBox.Clear();
            SalePriceTextBox.Clear();

            if (product == null)
                return;

            string packageUnit =
                string.IsNullOrWhiteSpace(product.UnitName)
                    ? "وحدة"
                    : product.UnitName.Trim();

            string smallUnit =
                product.SmallUnitName == null
                    ? ""
                    : product.SmallUnitName.Trim();

            var units = new List<string>
            {
                packageUnit
            };

            if (!string.IsNullOrWhiteSpace(smallUnit)
                && !string.Equals(
                    packageUnit,
                    smallUnit,
                    StringComparison.OrdinalIgnoreCase))
            {
                units.Add(smallUnit);
            }

            PurchaseUnitComboBox.ItemsSource = units;
            PurchaseUnitComboBox.SelectedIndex = 0;
            PurchaseUnitComboBox.IsEnabled = true;

            UnitPriceTextBox.Text =
                product.PurchasePrice.ToString(
                    "0.00",
                    CultureInfo.CurrentCulture);

            SalePriceTextBox.Text =
                product.SalePrice.ToString(
                    "0.00",
                    CultureInfo.CurrentCulture);
        }

        private void PurchaseUnitComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            var product =
                ProductComboBox.SelectedItem as Product;

            if (product == null ||
                PurchaseUnitComboBox.SelectedItem == null)
                return;

            string selectedUnit =
                PurchaseUnitComboBox.SelectedItem.ToString();

            bool buyingSmallUnit =
                IsSmallUnit(product, selectedUnit);

            double price = product.PurchasePrice;

            if (buyingSmallUnit)
            {
                int unitsPerPackage =
                    product.UnitsPerPackage;

                if (unitsPerPackage <= 0)
                    unitsPerPackage = 1;

                price =
                    product.PurchasePrice / unitsPerPackage;
            }

            UnitPriceTextBox.Text =
                price.ToString(
                    "0.00",
                    CultureInfo.CurrentCulture);
        }

        private static bool IsSmallUnit(
            Product product,
            string selectedUnit)
        {
            return product != null
                && !string.IsNullOrWhiteSpace(product.SmallUnitName)
                && !string.IsNullOrWhiteSpace(selectedUnit)
                && string.Equals(
                    selectedUnit.Trim(),
                    product.SmallUnitName.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }

        private void AddNewProductButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var choice = MessageBox.Show(
                "اختر طريقة إضافة الصنف:\n\n"
                + "نعم: إضافة دواء من دليل الأدوية.\n"
                + "لا: إضافة صنف يدوي.\n"
                + "إلغاء: الرجوع دون إجراء أي تغيير.",
                "إضافة صنف جديد",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Cancel)
                return;

            if (choice == MessageBoxResult.Yes)
                AddDrugFromCatalog();
            else if (choice == MessageBoxResult.No)
                AddManualProduct();
        }

        private void AddDrugFromCatalog()
        {
            try
            {
                var searchWindow =
                    new DrugCatalogSearchWindow();

                searchWindow.Owner = this;

                bool? searchResult =
                    searchWindow.ShowDialog();

                if (searchResult != true ||
                    searchWindow.SelectedDrug == null)
                    return;

                var addWindow = new AddCatalogDrugWindow(
                    currentUserId,
                    searchWindow.SelectedDrug);

                addWindow.Owner = this;

                bool? addResult =
                    addWindow.ShowDialog();

                if (addResult != true)
                    return;

                LoadAndSelectProduct(
                    addWindow.CreatedProductId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء إضافة الدواء من الدليل:\n"
                    + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void AddManualProduct()
        {
            try
            {
                var window =
                    new AddManualProductWindow(currentUserId);

                window.Owner = this;

                bool? result = window.ShowDialog();

                if (result != true)
                    return;

                LoadAndSelectProduct(
                    window.CreatedProductId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء إضافة الصنف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadAndSelectProduct(int productId)
        {
            LoadProducts();

            if (productsView != null)
                productsView.Refresh();

            Product newProduct = allProducts.Find(
                p => p.Id == productId);

            if (newProduct == null)
            {
                MessageBox.Show(
                    "تم حفظ الصنف، لكن تعذر العثور عليه في قائمة الأصناف.\n"
                    + "حاول تحديث الشاشة.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            ProductComboBox.SelectedItem = newProduct;
            ProductComboBox.Text = newProduct.Name;

            QuantityTextBox.Text = "1";
            BatchNumberTextBox.Clear();
            ExpiryDatePicker.SelectedDate = null;
            ProductComboBox.Focus();

            MessageBox.Show(
                "تمت إضافة الصنف وتحديده في فاتورة الشراء.\n"
                + "اختر وحدة الشراء وأدخل الكمية والتشغيلة والصلاحية ثم اضغط إضافة.",
                "تمت إضافة الصنف",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void AddItemButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var product =
                ProductComboBox.SelectedItem as Product;

            if (product == null ||
                !string.Equals(
                    (ProductComboBox.Text ?? "").Trim(),
                    (product.Name ?? "").Trim(),
                    StringComparison.CurrentCultureIgnoreCase))
            {
                MessageBox.Show(
                    "اختر صنفاً من القائمة أولاً، ولا تكتفِ بكتابة اسمه.");

                ProductComboBox.Focus();
                return;
            }

            if (scannedProductId.HasValue &&
                product.Id != scannedProductId.Value)
            {
                MessageBox.Show(
                    "تنبيه: الصنف المحدد لا يطابق الصنف الذي تم التعرف عليه من الباركود.\n\n"
                    + "الصنف الصحيح هو: "
                    + (allProducts.Find(
                        p => p.Id == scannedProductId.Value)?.Name
                        ?? "غير معروف")
                    + "\n\n"
                    + "أعد قراءة باركود الصنف المطلوب قبل إضافته إلى الفاتورة.",
                    "عدم تطابق الباركود والصنف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                ProductComboBox.Focus();
                return;
            }

            if (PurchaseUnitComboBox.SelectedItem == null)
            {
                MessageBox.Show("اختر وحدة الشراء أولاً.");
                PurchaseUnitComboBox.Focus();
                return;
            }

            if (!TryReadNumber(
                QuantityTextBox.Text,
                out double quantity)
                || quantity <= 0
                || !IsWholeNumber(quantity))
            {
                MessageBox.Show(
                    "أدخل كمية صحيحة أكبر من صفر، بدون كسور.");

                QuantityTextBox.Focus();
                return;
            }

            if (!TryReadNumber(
                UnitPriceTextBox.Text,
                out double unitPrice)
                || unitPrice < 0
                || IsInvalidNumber(unitPrice))
            {
                MessageBox.Show("أدخل سعر شراء صحيحاً.");
                UnitPriceTextBox.Focus();
                return;
            }

            bool requiresExpiryDate =
                product.DrugCatalogId.HasValue;

            DateTime? expiryDate = null;

            if (ExpiryDatePicker.SelectedDate.HasValue)
            {
                expiryDate =
                    ExpiryDatePicker.SelectedDate.Value.Date;

                if (expiryDate.Value < DateTime.Today)
                {
                    MessageBox.Show(
                        "لا يمكن إضافة صنف منتهي الصلاحية.");
                    return;
                }
            }
            else if (requiresExpiryDate)
            {
                MessageBox.Show(
                    "هذا الصنف مرتبط بدليل الأدوية، لذلك يجب تحديد تاريخ الصلاحية.");

                ExpiryDatePicker.Focus();
                return;
            }

            string packageUnit =
                string.IsNullOrWhiteSpace(product.UnitName)
                    ? "وحدة"
                    : product.UnitName.Trim();

            string smallUnit =
                product.SmallUnitName == null
                    ? ""
                    : product.SmallUnitName.Trim();

            string selectedUnit =
                PurchaseUnitComboBox.SelectedItem.ToString();

            int unitsPerPackage =
                product.UnitsPerPackage;

            if (unitsPerPackage <= 0)
                unitsPerPackage = 1;

            string batchNumber =
                (BatchNumberTextBox.Text ?? "").Trim();

            var item = new PurchaseInvoiceItem
            {
                ProductId = product.Id,
                DrugCatalogId = product.DrugCatalogId,
                ItemName = product.Name,
                ProductCode = product.ProductCode ?? "",
                Manufacturer = "",
                UnitName = selectedUnit,
                PackageUnitName = packageUnit,
                SmallUnitName = smallUnit,
                UnitsPerPackage = unitsPerPackage,
                BatchNumber = batchNumber,
                ExpiryDate = expiryDate,
                ExpiryDateText = expiryDate.HasValue
                    ? expiryDate.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture)
                    : "",
                Quantity = quantity,
                UnitPrice = unitPrice,
                FreeQuantity = 0,
                DiscountPercent = 0,
                TaxPercent = 0,
                Notes = ""
            };

            invoiceItems.Add(item);
            packagePrices[item] = product.PurchasePrice;

            scannedProductId = null;
            RefreshInvoiceGrid();
            UpdateTotal();

            BatchNumberTextBox.Clear();
            QuantityTextBox.Text = "1";
            ExpiryDatePicker.SelectedDate = null;
            ProductComboBox.SelectedIndex = -1;
            ProductComboBox.Text = "";
            PurchaseUnitComboBox.ItemsSource = null;
            PurchaseUnitComboBox.IsEnabled = false;
            UnitPriceTextBox.Clear();
            SalePriceTextBox.Clear();
            ProductComboBox.Focus();
        }
        private void InvoiceProductComboBox_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            var comboBox = sender as ComboBox;

            if (comboBox == null || allProducts == null)
                return;

            comboBox.ApplyTemplate();

            var textBox = comboBox.Template.FindName(
                "PART_EditableTextBox",
                comboBox) as TextBox;

            if (textBox == null)
                return;

            // لكل صف View مستقل حتى لا يؤثر البحث في صف على بقية الصفوف.
            var view = new ListCollectionView(allProducts);

            view.Filter = product =>
            {
                var p = product as Product;

                if (p == null)
                    return false;

                string search = (textBox.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(search))
                    return true;

                return
                    (!string.IsNullOrWhiteSpace(p.Name) &&
                     p.Name.IndexOf(
                         search,
                         StringComparison.CurrentCultureIgnoreCase) >= 0)
                    ||
                    (!string.IsNullOrWhiteSpace(p.ProductCode) &&
                     p.ProductCode.IndexOf(
                         search,
                         StringComparison.CurrentCultureIgnoreCase) >= 0);
            };

            comboBox.Tag = view;
            comboBox.ItemsSource = view;

            textBox.Tag = comboBox;

            textBox.TextChanged -=
                InvoiceProductSearchTextBox_TextChanged;

            textBox.TextChanged +=
                InvoiceProductSearchTextBox_TextChanged;
        }

        private void InvoiceProductSearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            var comboBox = textBox?.Tag as ComboBox;
            var view = comboBox?.Tag as ListCollectionView;

            if (textBox == null || comboBox == null || view == null)
                return;

            // الفلتر يقرأ من TextBox نفسه لضمان استخدام أحدث حرف مكتوب.
            view.Refresh();

            if (!string.IsNullOrWhiteSpace(textBox.Text) &&
                comboBox.Items.Count > 0)
            {
                comboBox.IsDropDownOpen = true;
            }
        }

        private void InvoiceProductComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;

            if (comboBox == null || e.AddedItems == null ||
                e.AddedItems.Count == 0)
                return;

            var product = e.AddedItems[0] as Product;
            var item = comboBox.DataContext as PurchaseInvoiceItem;

            if (product == null || item == null)
                return;

            ApplyProductToInvoiceItem(item, product);

            // تحديث النص الظاهر بعد اختيار نتيجة فعلية.
            comboBox.Text = product.Name ?? "";

            UpdateTotal();
        }

        private void AddInvoiceRowButton_Click(object sender, RoutedEventArgs e)
        {
            var item = new PurchaseInvoiceItem
            {
                Quantity = 1,
                FreeQuantity = 0,
                DiscountPercent = 0,
                TaxPercent = 0,
                IsTaxDeductible = false,
                BarcodeInput = "",
                BatchNumber = "",
                ExpiryDate = null,
                Notes = ""
            };

            invoiceItems.Add(item);
            RefreshInvoiceGrid();
            UpdateTotal();

            InvoiceItemsGrid.SelectedItem = item;
            InvoiceItemsGrid.ScrollIntoView(item);
            InvoiceItemsGrid.Focus();
        }

        private void RemoveInvoiceRowButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedItem =
                InvoiceItemsGrid.SelectedItem as PurchaseInvoiceItem;

            if (selectedItem == null)
            {
                MessageBox.Show(
                    "حدد الصف الذي تريد حذفه أولاً.",
                    "حذف صف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                "هل تريد حذف الصف المحدد من الفاتورة؟",
                "تأكيد حذف الصف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            invoiceItems.Remove(selectedItem);
            packagePrices.Remove(selectedItem);

            RefreshInvoiceGrid();
            UpdateTotal();
        }

        private Product FindProductForInvoiceEntry(
            string value,
            bool searchByCode)
        {
            string search = (value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(search))
                return null;

            if (searchByCode)
            {
                return allProducts.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(p.ProductCode) &&
                    string.Equals(
                        p.ProductCode.Trim(),
                        search,
                        StringComparison.CurrentCultureIgnoreCase));
            }

            Product exactName = allProducts.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.Name) &&
                string.Equals(
                    p.Name.Trim(),
                    search,
                    StringComparison.CurrentCultureIgnoreCase));

            if (exactName != null)
                return exactName;

            var matches = allProducts.Where(p =>
                !string.IsNullOrWhiteSpace(p.Name) &&
                p.Name.IndexOf(
                    search,
                    StringComparison.CurrentCultureIgnoreCase) >= 0)
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        private void ResolveInvoiceItemProduct(
            PurchaseInvoiceItem item,
            string enteredValue,
            bool searchByCode)
        {
            if (item == null)
                return;

            string search = (enteredValue ?? "").Trim();

            if (string.IsNullOrWhiteSpace(search))
            {
                item.ProductId = 0;
                item.DrugCatalogId = null;
                item.UnitName = "";
                item.PackageUnitName = "";
                item.SmallUnitName = "";
                item.UnitsPerPackage = 1;
                item.UnitPrice = 0;

                packagePrices.Remove(item);
                UpdateTotal();
                return;
            }

            Product product =
                FindProductForInvoiceEntry(search, searchByCode);

            if (product == null)
            {
                item.ProductId = 0;
                item.DrugCatalogId = null;
                item.UnitName = "";
                item.PackageUnitName = "";
                item.SmallUnitName = "";
                item.UnitsPerPackage = 1;
                item.UnitPrice = 0;

                packagePrices.Remove(item);

                bool ambiguousName = !searchByCode &&
                    allProducts.Count(p =>
                        !string.IsNullOrWhiteSpace(p.Name) &&
                        p.Name.IndexOf(
                            search,
                            StringComparison.CurrentCultureIgnoreCase) >= 0) > 1;

                if (ambiguousName)
                {
                    MessageBox.Show(
                        "يوجد أكثر من صنف يحتوي اسمه على النص المدخل. اكتب اسمًا أدق أو استخدم كود الصنف.",
                        "تحديد الصنف",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                UpdateTotal();
                return;
            }

            ApplyProductToInvoiceItem(item, product);
        }

        private void ApplyProductToInvoiceItem(
            PurchaseInvoiceItem item,
            Product product)
        {
            if (item == null || product == null)
                return;

            item.ProductId = product.Id;
            item.DrugCatalogId = product.DrugCatalogId;
            item.ProductCode = product.ProductCode ?? "";
            item.ItemName = product.Name ?? "";

            item.PackageUnitName =
                string.IsNullOrWhiteSpace(product.UnitName)
                    ? "وحدة"
                    : product.UnitName.Trim();

            item.SmallUnitName =
                string.IsNullOrWhiteSpace(product.SmallUnitName)
                    ? ""
                    : product.SmallUnitName.Trim();

            item.UnitsPerPackage =
                product.UnitsPerPackage > 0
                    ? product.UnitsPerPackage
                    : 1;

            item.UnitName = item.PackageUnitName;
            item.UnitPrice = product.PurchasePrice;

            packagePrices[item] = product.PurchasePrice;

            UpdateTotal();
        }
        private void InvoiceRowBarcodeTextBox_KeyDown(
            object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter)
                return;

            e.Handled = true;

            var textBox = sender as TextBox;
            var item = textBox?.DataContext as PurchaseInvoiceItem;

            if (textBox == null || item == null)
                return;

            string scannedValue =
                (textBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(scannedValue))
                return;

            item.BarcodeInput = scannedValue;

            ProcessInvoiceRowBarcode(item, scannedValue);
        }

        private void ProcessInvoiceRowBarcode(
            PurchaseInvoiceItem item,
            string scannedValue)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(scannedValue))
                return;

            GS1BarcodeData barcodeData = null;
            Product matchedProduct = null;
            string parseError = null;

            try
            {
                barcodeData =
                    GS1BarcodeService.Parse(scannedValue);

                if (!string.IsNullOrWhiteSpace(barcodeData.GTIN))
                {
                    int? productId =
                        productService.FindProductIdByBarcode(
                            barcodeData.GTIN);

                    if (productId.HasValue)
                    {
                        matchedProduct = allProducts.FirstOrDefault(
                            p => p.Id == productId.Value);
                    }

                    if (matchedProduct == null)
                    {
                        matchedProduct = allProducts.FirstOrDefault(p =>
                            !string.IsNullOrWhiteSpace(p.Barcode) &&
                            (string.Equals(
                                p.Barcode.Trim(),
                                barcodeData.GTIN,
                                StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(
                                p.Barcode.Trim(),
                                scannedValue,
                                StringComparison.OrdinalIgnoreCase)));
                    }
                }
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }

            // دعم الباركود العادي المخزن مباشرة في بيانات الصنف.
            if (matchedProduct == null)
            {
                matchedProduct = allProducts.FirstOrDefault(p =>
                    (!string.IsNullOrWhiteSpace(p.Barcode) &&
                     string.Equals(
                        p.Barcode.Trim(),
                        scannedValue,
                        StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(p.ProductCode) &&
                     string.Equals(
                        p.ProductCode.Trim(),
                        scannedValue,
                        StringComparison.OrdinalIgnoreCase)));
            }

            if (matchedProduct != null)
                ApplyProductToInvoiceItem(item, matchedProduct);

            // لا نستبدل البيانات اليدوية إذا لم يرسلها الباركود.
            if (barcodeData != null &&
                !string.IsNullOrWhiteSpace(barcodeData.BatchNumber))
            {
                item.BatchNumber = barcodeData.BatchNumber;
            }

            if (barcodeData != null &&
                barcodeData.ExpiryDate.HasValue)
            {
                item.ExpiryDate = barcodeData.ExpiryDate.Value.Date;
            }

            item.BarcodeInput = "";

            RefreshInvoiceGrid();
            UpdateTotal();

            if (matchedProduct != null)
            {
                MessageBox.Show(
                    "تم التعرف على الصنف داخل الصف: " +
                    matchedProduct.Name +
                    (barcodeData != null &&
                     !string.IsNullOrWhiteSpace(barcodeData.BatchNumber)
                        ? "\nالتشغيلة: " + barcodeData.BatchNumber
                        : "") +
                    (barcodeData != null &&
                     barcodeData.ExpiryDate.HasValue
                        ? "\nالصلاحية: " +
                          barcodeData.ExpiryDate.Value.ToString("yyyy-MM-dd")
                        : ""),
                    "تمت قراءة الباركود",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (barcodeData != null)
            {
                MessageBox.Show(
                    "تمت قراءة بيانات GS1، لكن رقم GTIN غير مرتبط بصنف في النظام. اختر الصنف يدويًا في نفس الصف. تم الاحتفاظ بالتشغيلة والصلاحية إن وُجدتا.",
                    "الصنف غير مربوط",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    "تعذر التعرف على الباركود كصنف معروف." +
                    (string.IsNullOrWhiteSpace(parseError)
                        ? ""
                        : "\nتفاصيل القراءة: " + parseError) +
                    "\nيمكنك اختيار الصنف يدويًا بالكود أو الاسم.",
                    "باركود غير معروف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            InvoiceItemsGrid.Focus();
        }

        private void UpdateItemUnitPrice(
            PurchaseInvoiceItem item)
        {
            if (item == null)
                return;

            if (!packagePrices.TryGetValue(
                item,
                out double packagePrice))
            {
                return;
            }

            bool buyingSmallUnit =
                !string.IsNullOrWhiteSpace(item.SmallUnitName)
                && string.Equals(
                    (item.UnitName ?? "").Trim(),
                    item.SmallUnitName.Trim(),
                    StringComparison.OrdinalIgnoreCase);

            if (buyingSmallUnit)
            {
                if (item.UnitsPerPackage <= 0)
                {
                    MessageBox.Show(
                        "عدد الوحدات داخل العبوة غير صحيح.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                item.UnitPrice =
                    packagePrice / item.UnitsPerPackage;
            }
            else
            {
                item.UnitPrice = packagePrice;
            }
        }

        private void RemoveItemButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedItem =
                InvoiceItemsGrid.SelectedItem as PurchaseInvoiceItem;

            if (selectedItem == null)
            {
                MessageBox.Show(
                    "اختر الصنف الذي تريد حذفه من الفاتورة.");
                return;
            }

            var result = MessageBox.Show(
                "هل تريد حذف الصنف المحدد بالكامل من الفاتورة؟",
                "تأكيد الحذف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            invoiceItems.Remove(selectedItem);
            packagePrices.Remove(selectedItem);

            RefreshInvoiceGrid();
            UpdateTotal();
        }

        private void InvoiceItemsGrid_CellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e)
        {
            var item =
                e.Row.Item as PurchaseInvoiceItem;

            string columnHeader =
                e.Column.Header == null
                    ? ""
                    : e.Column.Header.ToString();

            bool unitColumn = columnHeader == "الوحدة";
            bool codeColumn = columnHeader == "كود الصنف";
            bool nameColumn = columnHeader == "اسم الصنف";

            string editedText = null;

            if (codeColumn)
            {
                var textBox = e.EditingElement as TextBox;

                if (textBox != null)
                    editedText = textBox.Text;
            }
            else if (nameColumn)
            {
                // عمود اسم الصنف أصبح ComboBox قابلًا للبحث.
                var comboBox = e.EditingElement as ComboBox;

                if (comboBox != null)
                    editedText = comboBox.Text;
            }

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (item != null && unitColumn)
                        UpdateItemUnitPrice(item);

                    if (item != null && codeColumn)
                        ResolveInvoiceItemProduct(
                            item,
                            editedText,
                            true);

                    if (item != null && nameColumn)
                        ResolveInvoiceItemProduct(
                            item,
                            editedText,
                            false);

                    UpdateTotal();
                }),
                DispatcherPriority.Background);
        }

        private void InvoiceItemsGrid_RowEditEnding(
            object sender,
            DataGridRowEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    UpdateTotal();
                }),
                DispatcherPriority.Background);
        }

        private void SaveInvoiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            InvoiceItemsGrid.CommitEdit(
                DataGridEditingUnit.Cell,
                true);

            InvoiceItemsGrid.CommitEdit(
                DataGridEditingUnit.Row,
                true);

            if (SupplierComboBox.SelectedValue == null)
            {
                MessageBox.Show("اختر المورد أولاً.");
                return;
            }

            if (invoiceItems.Count == 0)
            {
                MessageBox.Show(
                    "أضف صنفاً واحداً على الأقل للفاتورة.");
                return;
            }

            if (InvoiceDatePicker.SelectedDate == null)
            {
                MessageBox.Show("حدد تاريخ فاتورة الشراء.");
                return;
            }

            foreach (var item in invoiceItems)
            {
                if (item == null)
                {
                    MessageBox.Show(
                        "يوجد بند غير صالح في الفاتورة.");
                    return;
                }

                if (item.ProductId <= 0)
                {
                    MessageBox.Show(
                        "يوجد صف لم يتم ربطه بصنف صحيح. أدخل كود الصنف أو اختر اسمه من الجدول قبل حفظ الفاتورة.",
                        "صنف غير محدد",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    InvoiceItemsGrid.SelectedItem = item;
                    InvoiceItemsGrid.ScrollIntoView(item);
                    return;
                }

                if (item.Quantity <= 0 ||
                    IsInvalidNumber(item.Quantity) ||
                    !IsWholeNumber(item.Quantity))
                {
                    MessageBox.Show(
                        "توجد كمية مشتراة غير صحيحة في الفاتورة.\n"
                        + "يجب أن تكون عددًا صحيحًا أكبر من صفر.");
                    return;
                }

                if (item.FreeQuantity < 0 ||
                    IsInvalidNumber(item.FreeQuantity) ||
                    !IsWholeNumber(item.FreeQuantity))
                {
                    MessageBox.Show(
                        "توجد كمية مجانية غير صحيحة.\n"
                        + "يجب أن تكون عددًا صحيحًا غير سالب.");
                    return;
                }

                if (item.UnitPrice < 0 ||
                    IsInvalidNumber(item.UnitPrice))
                {
                    MessageBox.Show(
                        "يوجد سعر شراء غير صحيح في الفاتورة.");
                    return;
                }

                if (item.DiscountPercent < 0 ||
                    item.DiscountPercent > 100 ||
                    IsInvalidNumber(item.DiscountPercent))
                {
                    MessageBox.Show(
                        "نسبة الخصم يجب أن تكون بين صفر و100.");
                    return;
                }

                if (item.TaxPercent < 0 ||
                    item.TaxPercent > 100 ||
                    IsInvalidNumber(item.TaxPercent))
                {
                    MessageBox.Show(
                        "نسبة الضريبة يجب أن تكون بين صفر و100.");
                    return;
                }

                if (IsInvalidNumber(item.Total) ||
                    item.Total < 0)
                {
                    MessageBox.Show(
                        "إجمالي أحد الأصناف غير صحيح.");
                    return;
                }

                if (item.ExpiryDate.HasValue &&
                    item.ExpiryDate.Value.Date < DateTime.Today)
                {
                    MessageBox.Show(
                        "يوجد صنف بتاريخ صلاحية منتهٍ.");
                    return;
                }

                if (item.ExpiryDate == null &&
                    FindProductRequiresExpiry(item.ProductId))
                {
                    MessageBox.Show(
                        "يوجد دواء مرتبط بدليل الأدوية بدون تاريخ صلاحية.");
                    return;
                }
            }

            int supplierId =
                Convert.ToInt32(SupplierComboBox.SelectedValue);

            var result = MessageBox.Show(
                "إجمالي الفاتورة: "
                + TotalTextBlock.Text
                + "\n\nهل تريد حفظ فاتورة الشراء وإضافة الكميات إلى المخزون؟",
                "تأكيد الحفظ",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                int invoiceId = purchaseService.SaveInvoice(
                    currentUserId,
                    supplierId,
                    InvoiceDatePicker.SelectedDate.Value,
                    invoiceItems.ToList());

                MessageBox.Show(
                    "تم حفظ فاتورة الشراء بنجاح.\nرقم الفاتورة الداخلي: "
                    + invoiceId,
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                invoiceItems.Clear();
                packagePrices.Clear();
                scannedProductId = null;
                pendingInternationalBarcode = null;
                LinkPendingBarcodeButton.IsEnabled = false;

                InvoiceDatePicker.SelectedDate = DateTime.Today;

                RefreshInvoiceGrid();
                UpdateTotal();

                BatchNumberTextBox.Clear();
                QuantityTextBox.Text = "1";
                UnitPriceTextBox.Clear();
                SalePriceTextBox.Clear();
                ExpiryDatePicker.SelectedDate = null;

                ProductComboBox.SelectedIndex = -1;
                ProductComboBox.Text = "";

                PurchaseUnitComboBox.ItemsSource = null;
                PurchaseUnitComboBox.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر حفظ فاتورة الشراء:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool FindProductRequiresExpiry(int productId)
        {
            Product product = allProducts.Find(
                p => p.Id == productId);

            return product != null &&
                   product.DrugCatalogId.HasValue;
        }

        private bool TryReadNumber(
            string text,
            out double value)
        {
            if (double.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out value))
            {
                return true;
            }

            return double.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static bool IsInvalidNumber(double value)
        {
            return double.IsNaN(value) ||
                   double.IsInfinity(value);
        }

        private static bool IsWholeNumber(double value)
        {
            return Math.Abs(
                value - Math.Round(
                    value,
                    0,
                    MidpointRounding.AwayFromZero)) < 0.0000001;
        }

        private void RefreshInvoiceGrid()
        {
            InvoiceItemsGrid.Items.Refresh();
            InvoiceItemsGrid.UpdateLayout();

            if (invoiceItems.Count > 0)
            {
                PurchaseInvoiceItem lastItem =
                    invoiceItems[invoiceItems.Count - 1];

                InvoiceItemsGrid.ScrollIntoView(lastItem);
            }
        }

        private void UpdateTotal()
        {
            double total = 0;

            foreach (var item in invoiceItems)
            {
                if (item != null)
                    total += item.Total;
            }

            TotalTextBlock.Text =
                total.ToString(
                    "N2",
                    CultureInfo.CurrentCulture)
                + " جنيه";
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}