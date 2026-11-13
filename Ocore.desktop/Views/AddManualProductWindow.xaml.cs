
using System;
using System.Globalization;
using System.Windows;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class AddManualProductWindow : Window
    {
        private readonly int currentUserId;
        private readonly ProductService productService;

        public int CreatedProductId { get; private set; }

        public AddManualProductWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            productService = new ProductService();

            LoadCategories();
        }

        private void LoadCategories()
        {
            try
            {
                var categories =
                    productService.GetActiveCategories(currentUserId);

                CategoryComboBox.ItemsSource = categories;
                CategoryComboBox.DisplayMemberPath = "Value";
                CategoryComboBox.SelectedValuePath = "Key";

                if (categories.Count > 0)
                    CategoryComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحميل التصنيفات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Close();
            }
        }

        private bool TryReadPrice(
            string value,
            out double price)
        {
            price = 0;

            value = (value ?? "").Trim();

            if (string.IsNullOrWhiteSpace(value))
                return true;

            value = value.Replace(',', '.');

            return double.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out price)
                && !double.IsNaN(price)
                && !double.IsInfinity(price)
                && price >= 0;
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (CategoryComboBox.SelectedValue == null)
                {
                    MessageBox.Show(
                        "اختر تصنيف الصنف.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                string productCode =
                    ProductCodeTextBox.Text.Trim();

                string productName =
                    ProductNameTextBox.Text.Trim();

                string unitName =
                    UnitNameTextBox.Text.Trim();

                string barcode =
                    BarcodeTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(productCode))
                {
                    MessageBox.Show(
                        "اكتب كود الصنف.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ProductCodeTextBox.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(productName))
                {
                    MessageBox.Show(
                        "اكتب اسم الصنف.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ProductNameTextBox.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(unitName))
                {
                    MessageBox.Show(
                        "اكتب وحدة الصنف.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    UnitNameTextBox.Focus();
                    return;
                }

                double salePrice;
                double purchasePrice;

                if (!TryReadPrice(
                    SalePriceTextBox.Text,
                    out salePrice))
                {
                    MessageBox.Show(
                        "سعر البيع غير صحيح.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    SalePriceTextBox.Focus();
                    return;
                }

                if (!TryReadPrice(
                    PurchasePriceTextBox.Text,
                    out purchasePrice))
                {
                    MessageBox.Show(
                        "سعر الشراء غير صحيح.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    PurchasePriceTextBox.Focus();
                    return;
                }

                var product = new Product
                {
                    ProductCode = productCode,
                    Name = productName,

                    CategoryId = Convert.ToInt32(
                        CategoryComboBox.SelectedValue),

                    DrugCatalogId = null,

                    UnitName = unitName,
                    SmallUnitName = unitName,
                    UnitsPerPackage = 1,

                    Barcode = barcode,

                    SalePrice = salePrice,
                    PurchasePrice = purchasePrice,

                    Quantity = 0,
                    TotalQuantity = 0,
                    ExpiredQuantity = 0,

                    IsArchived = false
                };

                CreatedProductId =
                    productService.CreateProduct(
                        currentUserId,
                        product,
                        0,
                        null,
                        null);

                MessageBox.Show(
                    "تمت إضافة الصنف بنجاح.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر حفظ الصنف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
