
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

        private readonly Dictionary<PurchaseInvoiceItem, double>
            packagePrices = new Dictionary<PurchaseInvoiceItem, double>();

        // ==========================================
        // إعدادات تكبير جدول أصناف الفاتورة فقط
        // ==========================================

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

            InvoiceItemsGrid.RowEditEnding +=
                InvoiceItemsGrid_RowEditEnding;

            LoadSuppliers();
            LoadProducts();
            UpdateTotal();
        }

        // ==========================================
        // تكبير وتصغير جدول الأصناف فقط
        // ==========================================

        private void InitializeZoom()
        {
            zoomTransform = new ScaleTransform(1.0, 1.0);

            // تطبيق التكبير على جدول الأصناف فقط.
            // بقية الشاشة لا تتأثر.
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
            {
                return;
            }

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

        // ==========================================
        // تحميل الموردين
        // ==========================================

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

        // ==========================================
        // تحميل الأصناف
        // ==========================================

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

            string searchText = ProductComboBox.Text;

            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            return !string.IsNullOrWhiteSpace(product.Name)
                && product.Name.IndexOf(
                    searchText.Trim(),
                    StringComparison.CurrentCultureIgnoreCase) >= 0;
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

        // ==========================================
        // اختيار الصنف
        // ==========================================

        private void ProductComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            var product =
                ProductComboBox.SelectedItem as Product;

            PurchaseUnitComboBox.ItemsSource = null;
            PurchaseUnitComboBox.IsEnabled = false;
            UnitPriceTextBox.Clear();

            // مسح سعر البيع عند إلغاء اختيار الصنف
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

            // عرض سعر البيع المسجل بالفعل في بيانات الصنف
            SalePriceTextBox.Text =
                product.SalePrice.ToString(
                    "0.00",
                    CultureInfo.CurrentCulture);
        }

        // ==========================================
        // اختيار وحدة الشراء
        // ==========================================

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

        // ==========================================
        // إضافة صنف جديد
        // ==========================================

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

        // ==========================================
        // إضافة بند إلى الفاتورة
        // ==========================================

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

            if (PurchaseUnitComboBox.SelectedItem == null)
            {
                MessageBox.Show(
                    "اختر وحدة الشراء أولاً.");

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
                MessageBox.Show(
                    "أدخل سعر شراء صحيحاً.");

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

        // ==========================================
        // تغيير الوحدة داخل جدول الفاتورة
        // ==========================================

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

        // ==========================================
        // حذف بند من الفاتورة قبل الحفظ
        // ==========================================

        private void RemoveItemButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedItem =
                InvoiceItemsGrid.SelectedItem
                as PurchaseInvoiceItem;

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

        // ==========================================
        // تحديث الإجمالي عند تعديل خلية
        // ==========================================

        private void InvoiceItemsGrid_CellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e)
        {
            var item =
                e.Row.Item as PurchaseInvoiceItem;

            bool unitColumn =
                e.Column.Header != null
                && e.Column.Header.ToString() == "الوحدة";

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (unitColumn && item != null)
                        UpdateItemUnitPrice(item);

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

        // ==========================================
        // حفظ الفاتورة
        // ==========================================

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
                MessageBox.Show(
                    "اختر المورد أولاً.");
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
                MessageBox.Show(
                    "حدد تاريخ فاتورة الشراء.");
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
                    IsInvalidNumber(item.TaxPercent))
                {
                    MessageBox.Show(
                        "نسبة الضريبة لا يمكن أن تكون سالبة.");

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
                Convert.ToInt32(
                    SupplierComboBox.SelectedValue);

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

                InvoiceDatePicker.SelectedDate =
                    DateTime.Today;

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

        // ==========================================
        // التحقق من الحاجة لتاريخ الصلاحية
        // ==========================================

        private bool FindProductRequiresExpiry(int productId)
        {
            Product product = allProducts.Find(
                p => p.Id == productId);

            return product != null &&
                   product.DrugCatalogId.HasValue;
        }

        // ==========================================
        // أدوات قراءة الأرقام
        // ==========================================

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

        // ==========================================
        // تحديث الجدول والإجمالي
        // ==========================================

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

        // ==========================================
        // إغلاق النافذة
        // ==========================================

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
