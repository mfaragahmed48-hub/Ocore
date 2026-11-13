
using Ocore.desktop.Models;
using Ocore.desktop.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace Ocore.desktop.Views
{
    public partial class AddCatalogDrugWindow : Window
    {
        private readonly int currentUserId;
        private readonly DrugCatalogItem selectedDrug;
        private readonly ProductService productService;

        public int CreatedProductId { get; private set; }

        public AddCatalogDrugWindow(
            int userId,
            DrugCatalogItem drug)
        {
            InitializeComponent();

            currentUserId = userId;
            selectedDrug = drug;

            productService = new ProductService();

            LoadDrugData();
            LoadCategories();
        }

        private void LoadDrugData()
        {
            if (selectedDrug == null)
                return;

            DrugNameTextBox.Text =
                selectedDrug.DisplayName;

            GenericNameTextBox.Text =
                selectedDrug.GenericName ?? "";

            StrengthTextBox.Text =
                selectedDrug.Strength ?? "";

            DosageFormTextBox.Text =
                selectedDrug.DosageForm ?? "";

            ManufacturerTextBox.Text =
                selectedDrug.Manufacturer ?? "";

            // السعر المرجعي اقتراح أولي وليس سعرًا ملزمًا.
            if (selectedDrug.ReferencePrice > 0)
            {
                SalePriceTextBox.Text =
                    selectedDrug.ReferencePrice.ToString(
                        "0.00",
                        CultureInfo.CurrentCulture);
            }
        }

        private void LoadCategories()
        {
            try
            {
                List<KeyValuePair<int, string>> categories =
                    productService.GetActiveCategories(currentUserId);

                CategoryComboBox.ItemsSource = categories;

                if (categories.Count > 0)
                {
                    CategoryComboBox.SelectedIndex = 0;
                }
                else
                {
                    MessageBox.Show(
                        "لا توجد تصنيفات نشطة.\n"
                        + "أضف تصنيفًا أولاً من إدارة التصنيفات ثم حاول مرة أخرى.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    SaveButtonEnabled(false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل التصنيفات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                SaveButtonEnabled(false);
            }
        }

        private void SaveButtonEnabled(bool enabled)
        {
            SaveProductButton.IsEnabled = enabled;
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (selectedDrug == null)
                {
                    MessageBox.Show("بيانات الدواء غير متاحة.");
                    return;
                }

                if (CategoryComboBox.SelectedValue == null)
                {
                    MessageBox.Show("اختر تصنيف الصنف أولاً.");
                    CategoryComboBox.Focus();
                    return;
                }

                string productCode =
                    ProductCodeTextBox.Text.Trim();

                string unitName =
                    UnitNameTextBox.Text.Trim();

                string smallUnitName =
                    SmallUnitNameTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(productCode))
                {
                    MessageBox.Show("أدخل كود الصنف.");
                    ProductCodeTextBox.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(unitName))
                {
                    MessageBox.Show("أدخل اسم الوحدة الكبيرة.");
                    UnitNameTextBox.Focus();
                    return;
                }

                if (string.IsNullOrWhiteSpace(smallUnitName))
                {
                    MessageBox.Show("أدخل أقل وحدة يمكن بيعها.");
                    SmallUnitNameTextBox.Focus();
                    return;
                }

                if (!TryReadNumber(
                    UnitsPerPackageTextBox.Text,
                    out double unitsPerPackageValue)
                    || unitsPerPackageValue < 1
                    || unitsPerPackageValue != Math.Floor(unitsPerPackageValue)
                    || unitsPerPackageValue > int.MaxValue)
                {
                    MessageBox.Show(
                        "أدخل عدد الوحدات الصغيرة داخل الوحدة الكبيرة بشكل صحيح، ويجب أن يكون 1 على الأقل.");

                    UnitsPerPackageTextBox.Focus();
                    return;
                }

                if (!TryReadNumber(
                    SalePriceTextBox.Text,
                    out double salePrice)
                    || salePrice < 0)
                {
                    MessageBox.Show("أدخل سعر بيع صحيحًا.");
                    SalePriceTextBox.Focus();
                    return;
                }

                if (!TryReadNumber(
                    PurchasePriceTextBox.Text,
                    out double purchasePrice)
                    || purchasePrice < 0)
                {
                    MessageBox.Show("أدخل سعر شراء صحيحًا.");
                    PurchasePriceTextBox.Focus();
                    return;
                }

                int categoryId =
                    Convert.ToInt32(CategoryComboBox.SelectedValue);

                int unitsPerPackage =
                    Convert.ToInt32(unitsPerPackageValue);

                var product = new Product
                {
                    ProductCode = productCode,
                    Name = selectedDrug.DisplayName,

                    DrugCatalogId = selectedDrug.Id,
                    CategoryId = categoryId,

                    UnitName = unitName,
                    SmallUnitName = smallUnitName,

                    UnitsPerPackage = unitsPerPackage,

                    SalePrice = salePrice,
                    PurchasePrice = purchasePrice,

                    Quantity = 0,
                    TotalQuantity = 0,
                    ExpiredQuantity = 0,

                    IsArchived = false,
                    Barcode = null
                };

                var confirmation = MessageBox.Show(
                    "هل تريد تسجيل الدواء التالي في المخزون؟\n\n"
                    + product.Name
                    + "\nكود الصنف: " + product.ProductCode
                    + "\nالتصنيف: " + CategoryComboBox.Text
                    + "\nالوحدة الكبيرة: " + unitName
                    + "\nأقل وحدة للبيع: " + smallUnitName
                    + "\nعدد الوحدات الصغيرة داخل الوحدة الكبيرة: "
                    + unitsPerPackage
                    + "\n\nسيتم إنشاء الصنف برصيد صفر.",
                    "تأكيد تسجيل الدواء",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                SaveProductButton.IsEnabled = false;

                try
                {
                    CreatedProductId = productService.CreateProduct(
                        currentUserId,
                        product,
                        0,
                        null,
                        null);
                }
                finally
                {
                    SaveProductButton.IsEnabled = true;
                }

                MessageBox.Show(
                    "تم تسجيل الدواء في المخزون بنجاح.\n"
                    + "أصبح مرتبطًا بدليل الأدوية.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تسجيل الدواء في المخزون:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
