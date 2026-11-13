
using Ocore.desktop.Models;
using Ocore.desktop.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Ocore.desktop.Views
{
    public partial class AddProductWindow : Window
    {
        private readonly int currentUserId;
        private readonly ProductService productService;
        private readonly string connectionString;

        private List<CatalogDrugItem> allCatalogDrugs;
        private ICollectionView catalogDrugsView;
        private CatalogDrugItem selectedCatalogDrug;

        private bool isEditMode;
        private Product productBeingEdited;

        private class CatalogDrugItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string GenericName { get; set; }
            public string Strength { get; set; }
            public string DosageForm { get; set; }
            public string RouteOfAdministration { get; set; }
            public string Manufacturer { get; set; }
            public string DrugClass { get; set; }
            public string ProductCode { get; set; }

            public string SearchText
            {
                get
                {
                    return string.Join(" ",
                        Name,
                        GenericName,
                        Strength,
                        DosageForm,
                        RouteOfAdministration,
                        Manufacturer,
                        DrugClass);
                }
            }
        }

        public AddProductWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            productService = new ProductService();

            var database = new Ocore.desktop.Data.DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";

            allCatalogDrugs = new List<CatalogDrugItem>();

            SaveButton.Click += SaveButton_Click;
            CancelButton.Click += CancelButton_Click;

            CatalogSearchTextBox.TextChanged +=
                CatalogSearchTextBox_TextChanged;

            CatalogDrugsDataGrid.SelectionChanged +=
                CatalogDrugsDataGrid_SelectionChanged;

            UseCatalogDrugButton.Click +=
                UseCatalogDrugButton_Click;

            ManageCategoriesButton.Click +=
                ManageCategoriesButton_Click;

            AddCategoryButton.Click +=
                AddCategoryButton_Click;

            UpdateCategoryButton.Click +=
                UpdateCategoryButton_Click;

            DeactivateCategoryButton.Click +=
                DeactivateCategoryButton_Click;

            CancelCategoryEditButton.Click +=
                CancelCategoryEditButton_Click;

            CategoriesDataGrid.SelectionChanged +=
                CategoriesDataGrid_SelectionChanged;

            UseCatalogDrugButton.IsEnabled = false;

            Loaded += AddProductWindow_Loaded;
        }

        // Constructor خاص بتعديل صنف موجود.
        public AddProductWindow(int userId, Product product)
            : this(userId)
        {
            if (product == null)
                throw new ArgumentNullException("product");

            isEditMode = true;
            productBeingEdited = product;
        }

        private void AddProductWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            LoadCategories();

            if (isEditMode)
            {
                ConfigureEditMode();
                return;
            }

            LoadDrugCatalog();
        }

        private void ConfigureEditMode()
        {
            Title = "O CORE - تعديل بيانات الصنف";
            WindowHeaderTextBlock.Text = "✏️ تعديل بيانات الصنف";
            SaveButton.Content = "💾 حفظ التعديلات";

            CatalogPanel.Visibility = Visibility.Collapsed;
            InitialBatchPanel.Visibility = Visibility.Collapsed;

            ProductCodeTextBox.Text =
                productBeingEdited.ProductCode ?? string.Empty;

            ProductNameTextBox.Text =
                productBeingEdited.Name ?? string.Empty;

            BarcodeTextBox.Text =
                productBeingEdited.Barcode ?? string.Empty;

            string unitName =
                productBeingEdited.UnitName ?? string.Empty;

            ComboBoxItem unitItem = UnitComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item =>
                    string.Equals(
                        Convert.ToString(item.Content),
                        unitName,
                        StringComparison.OrdinalIgnoreCase));

            if (unitItem == null &&
                !string.IsNullOrWhiteSpace(unitName))
            {
                unitItem = new ComboBoxItem
                {
                    Content = unitName
                };

                UnitComboBox.Items.Add(unitItem);
            }

            if (unitItem != null)
                UnitComboBox.SelectedItem = unitItem;

            SmallUnitComboBox.Text =
                productBeingEdited.SmallUnitName ?? string.Empty;

            UnitsPerPackageTextBox.Text =
                productBeingEdited.UnitsPerPackage.ToString(
                    CultureInfo.CurrentCulture);

            SalePriceTextBox.Text =
                productBeingEdited.SalePrice.ToString(
                    CultureInfo.CurrentCulture);

            PurchasePriceTextBox.Text =
                productBeingEdited.PurchasePrice.ToString(
                    CultureInfo.CurrentCulture);

            if (productBeingEdited.CategoryId.HasValue)
            {
                var categoryItems =
                    CategoryComboBox.ItemsSource
                    as List<KeyValuePair<int, string>>;

                if (categoryItems != null &&
                    !categoryItems.Any(x =>
                        x.Key == productBeingEdited.CategoryId.Value))
                {
                    categoryItems.Add(
                        new KeyValuePair<int, string>(
                            productBeingEdited.CategoryId.Value,
                            "تصنيف حالي (موقوف)"));

                    CategoryComboBox.Items.Refresh();
                }
            }

            CategoryComboBox.SelectedValue =
                productBeingEdited.CategoryId ?? 0;

            ProductNameTextBox.Focus();
        }

        #region Category Management

        private void LoadCategories()
        {
            try
            {
                var categories =
                    productService.GetActiveCategories(currentUserId);

                categories.Insert(
                    0,
                    new KeyValuePair<int, string>(
                        0, "بدون تصنيف"));

                CategoryComboBox.ItemsSource = categories;
                CategoryComboBox.DisplayMemberPath = "Value";
                CategoryComboBox.SelectedValuePath = "Key";

                if (CategoryComboBox.SelectedValue == null)
                    CategoryComboBox.SelectedIndex = 0;
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل التصنيفات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadCategoriesForManagement()
        {
            try
            {
                var categories =
                    productService.GetCategoriesForManagement(currentUserId);

                CategoriesDataGrid.ItemsSource = categories;
                CategoriesDataGrid.SelectedItem = null;

                UpdateCategoryButton.IsEnabled = false;
                DeactivateCategoryButton.IsEnabled = false;
                CategoryNameTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحميل التصنيفات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ManageCategoriesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool isVisible =
                CategoryManagementPanel.Visibility == Visibility.Visible;

            CategoryManagementPanel.Visibility =
                isVisible ? Visibility.Collapsed : Visibility.Visible;

            if (!isVisible)
                LoadCategoriesForManagement();
            else
                ClearCategorySelection();
        }

        private void AddCategoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                string name = CategoryNameTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show(
                        "اكتب اسم التصنيف أولاً.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    CategoryNameTextBox.Focus();
                    return;
                }

                productService.AddCategory(currentUserId, name);

                LoadCategories();
                LoadCategoriesForManagement();

                CategoryNameTextBox.Clear();

                MessageBox.Show(
                    "تمت إضافة التصنيف بنجاح.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "تعذر إضافة التصنيف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void UpdateCategoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selected =
                CategoriesDataGrid.SelectedItem
                as KeyValuePair<int, string>?;

            if (!selected.HasValue || selected.Value.Key <= 0)
            {
                MessageBox.Show("اختر تصنيفًا من القائمة أولاً.");
                return;
            }

            string newName = CategoryNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("اكتب الاسم الجديد للتصنيف.");
                CategoryNameTextBox.Focus();
                return;
            }

            try
            {
                productService.UpdateCategory(
                    currentUserId,
                    selected.Value.Key,
                    newName);

                LoadCategories();
                LoadCategoriesForManagement();

                MessageBox.Show(
                    "تم تعديل التصنيف بنجاح.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "تعذر تعديل التصنيف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DeactivateCategoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selected =
                CategoriesDataGrid.SelectedItem
                as KeyValuePair<int, string>?;

            if (!selected.HasValue || selected.Value.Key <= 0)
            {
                MessageBox.Show("اختر تصنيفًا من القائمة أولاً.");
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                "هل تريد إيقاف التصنيف التالي؟\n\n" +
                selected.Value.Value +
                "\n\nلن يتم حذف الأصناف المرتبطة به.",
                "تأكيد إيقاف التصنيف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                productService.DeactivateCategory(
                    currentUserId,
                    selected.Value.Key);

                LoadCategories();
                LoadCategoriesForManagement();

                MessageBox.Show(
                    "تم إيقاف التصنيف بنجاح.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "تعذر إيقاف التصنيف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void CategoriesDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            var selected =
                CategoriesDataGrid.SelectedItem
                as KeyValuePair<int, string>?;

            if (!selected.HasValue || selected.Value.Key <= 0)
            {
                UpdateCategoryButton.IsEnabled = false;
                DeactivateCategoryButton.IsEnabled = false;
                return;
            }

            CategoryNameTextBox.Text = selected.Value.Value;

            UpdateCategoryButton.IsEnabled = true;
            DeactivateCategoryButton.IsEnabled = true;
        }

        private void CancelCategoryEditButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ClearCategorySelection();
        }

        private void ClearCategorySelection()
        {
            CategoriesDataGrid.SelectedItem = null;
            CategoryNameTextBox.Clear();

            UpdateCategoryButton.IsEnabled = false;
            DeactivateCategoryButton.IsEnabled = false;
        }

        #endregion

        private void LoadDrugCatalog()
        {
            try
            {
                allCatalogDrugs.Clear();

                using (var connection =
                    new System.Data.SQLite.SQLiteConnection(connectionString))
                {
                    connection.Open();

                    string sql = @"
SELECT
    Id,
    TradeName,
    GenericName,
    Strength,
    DosageForm,
    RouteOfAdministration,
    Manufacturer,
    DrugClass
FROM DrugCatalog
WHERE IsActive = 1
  AND UPPER(COALESCE(SourceStatus, '')) NOT LIKE '%CANCELLED%'
  AND UPPER(COALESCE(SourceStatus, '')) NOT LIKE '%ILLEGAL IMPORT%'
  AND UPPER(COALESCE(SourceStatus, '')) NOT LIKE '%N/A YET%'
  AND UPPER(COALESCE(SourceStatus, '')) NOT LIKE '%N/A%'
ORDER BY TradeName;";

                    using (var command =
                        new System.Data.SQLite.SQLiteCommand(sql, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            allCatalogDrugs.Add(
                                new CatalogDrugItem
                                {
                                    Id = Convert.ToInt32(reader["Id"]),
                                    Name = Convert.ToString(reader["TradeName"]),
                                    GenericName = Convert.ToString(reader["GenericName"]),
                                    Strength = Convert.ToString(reader["Strength"]),
                                    DosageForm = Convert.ToString(reader["DosageForm"]),
                                    RouteOfAdministration = Convert.ToString(reader["RouteOfAdministration"]),
                                    Manufacturer = Convert.ToString(reader["Manufacturer"]),
                                    DrugClass = Convert.ToString(reader["DrugClass"])
                                });
                        }
                    }
                }

                catalogDrugsView =
                    CollectionViewSource.GetDefaultView(allCatalogDrugs);

                catalogDrugsView.Filter = FilterCatalogDrug;

                CatalogDrugsDataGrid.ItemsSource = catalogDrugsView;

                CatalogSelectionStatusText.Text =
                    "تم تحميل " + allCatalogDrugs.Count.ToString("N0") +
                    " دواء من الدليل. اكتب اسم الدواء للبحث.";
            }
            catch (Exception ex)
            {
                CatalogSelectionStatusText.Text =
                    "تعذر تحميل دليل الأدوية.";

                MessageBox.Show(
                    "حدث خطأ أثناء تحميل دليل الأدوية:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool FilterCatalogDrug(object item)
        {
            var drug = item as CatalogDrugItem;

            if (drug == null)
                return false;

            string search = Normalize(CatalogSearchTextBox.Text);

            if (search.Length == 0)
                return true;

            string[] words = search.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);

            string searchableText = Normalize(drug.SearchText);

            return words.All(word =>
                searchableText.Contains(word));
        }

        private void CatalogSearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            selectedCatalogDrug = null;

            if (catalogDrugsView != null)
                catalogDrugsView.Refresh();

            CatalogDrugsDataGrid.SelectedItem = null;
            UseCatalogDrugButton.IsEnabled = false;

            CatalogSelectionStatusText.Text =
                "اختر دواءً من نتائج البحث أو اترك الدليل دون اختيار لإضافة صنف يدوي.";
        }

        private void CatalogDrugsDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UseCatalogDrugButton.IsEnabled =
                CatalogDrugsDataGrid.SelectedItem is CatalogDrugItem;
        }

        private void UseCatalogDrugButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var drug =
                CatalogDrugsDataGrid.SelectedItem as CatalogDrugItem;

            if (drug == null)
            {
                MessageBox.Show("اختر دواءً من القائمة أولاً.");
                return;
            }

            var existing = FindExistingProduct(drug.Name);

            if (existing != null)
            {
                MessageBox.Show(
                    "هذا الدواء موجود بالفعل في المخزون.\n\n" +
                    "كود الصنف: " + existing.ProductCode +
                    "\nاسم الصنف: " + existing.Name +
                    "\n\nلا يمكن إنشاء صنف مكرر.",
                    "الصنف موجود",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            selectedCatalogDrug = drug;

            ProductNameTextBox.Text = drug.Name;

            CatalogSelectionStatusText.Text =
                "تم اختيار: " + drug.Name +
                "\nالمادة الفعالة: " + SafeText(drug.GenericName) +
                "\nالتركيز: " + SafeText(drug.Strength) +
                "\nالشركة: " + SafeText(drug.Manufacturer) +
                "\n\nسيتم ربط الصنف بهذا السجل من دليل الأدوية عند الحفظ.";

            ProductNameTextBox.Focus();
        }

        private Product FindExistingProduct(string drugName)
        {
            if (string.IsNullOrWhiteSpace(drugName))
                return null;

            var products =
                productService.GetActiveProducts(currentUserId);

            string normalizedName = Normalize(drugName);

            return products.FirstOrDefault(p =>
                Normalize(p.Name) == normalizedName);
        }

        private string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return string.Join(
                " ",
                value.Trim()
                    .ToUpperInvariant()
                    .Split(
                        new[] { ' ', '\t', '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries));
        }

        private string SafeText(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "غير متوفر"
                : value.Trim();
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                string name = ProductNameTextBox.Text.Trim();

                // الكود والباركود يتم توليدهما داخل ProductService.
                // لا نقرأهما من حقول الشاشة ولا نشترط إدخالهما.

                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("أدخل اسم الصنف.");
                    ProductNameTextBox.Focus();
                    return;
                }

                if (!isEditMode &&
                    selectedCatalogDrug != null &&
                    Normalize(name) != Normalize(selectedCatalogDrug.Name))
                {
                    MessageBox.Show(
                        "تم تغيير اسم الصنف بعد اختياره من دليل الأدوية.\n\n" +
                        "اختر الدواء من الدليل مرة أخرى، أو أضف الصنف يدويًا دون ربط.",
                        "تأكيد ارتباط الدليل",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ProductNameTextBox.Focus();
                    return;
                }

                if (!TryReadNumber(
                    SalePriceTextBox.Text, out double salePrice))
                {
                    MessageBox.Show("أدخل سعر بيع صحيحًا.");
                    SalePriceTextBox.Focus();
                    return;
                }

                if (!TryReadNumber(
                    PurchasePriceTextBox.Text, out double purchasePrice))
                {
                    MessageBox.Show("أدخل سعر شراء صحيحًا.");
                    PurchasePriceTextBox.Focus();
                    return;
                }

                if (salePrice < 0 || purchasePrice < 0)
                {
                    MessageBox.Show("لا يمكن إدخال أسعار سالبة.");
                    return;
                }

                double quantity = 0;

                if (!isEditMode)
                {
                    if (!TryReadNumber(
                        InitialQuantityTextBox.Text, out quantity))
                    {
                        MessageBox.Show("أدخل كمية افتتاحية صحيحة.");
                        InitialQuantityTextBox.Focus();
                        return;
                    }

                    if (quantity < 0)
                    {
                        MessageBox.Show("لا يمكن إدخال كمية سالبة.");
                        return;
                    }
                }

                string unitName = "علبة";

                if (UnitComboBox.SelectedItem is ComboBoxItem selectedUnit)
                {
                    unitName = Convert.ToString(selectedUnit.Content);
                }

                if (string.IsNullOrWhiteSpace(unitName))
                {
                    MessageBox.Show("اختر العبوة الأساسية.");
                    UnitComboBox.Focus();
                    return;
                }

                string smallUnitName = SmallUnitComboBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(smallUnitName))
                {
                    MessageBox.Show("أدخل اسم الوحدة الصغيرة.");
                    SmallUnitComboBox.Focus();
                    return;
                }

                int unitsPerPackage;

                if (!int.TryParse(
                    UnitsPerPackageTextBox.Text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.CurrentCulture,
                    out unitsPerPackage))
                {
                    if (!int.TryParse(
                        UnitsPerPackageTextBox.Text.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out unitsPerPackage))
                    {
                        MessageBox.Show(
                            "أدخل عددًا صحيحًا للوحدات داخل العبوة.");

                        UnitsPerPackageTextBox.Focus();
                        return;
                    }
                }

                if (unitsPerPackage < 1)
                {
                    MessageBox.Show(
                        "عدد الوحدات داخل العبوة يجب أن يكون واحدًا على الأقل.");

                    UnitsPerPackageTextBox.Focus();
                    return;
                }

                int? categoryId = null;

                if (CategoryComboBox.SelectedValue != null)
                {
                    int selectedCategory =
                        Convert.ToInt32(CategoryComboBox.SelectedValue);

                    if (selectedCategory > 0)
                        categoryId = selectedCategory;
                }

                DateTime? expiryDate = null;

                if (!isEditMode)
                {
                    expiryDate = ExpiryDatePicker.SelectedDate;

                    if (quantity > 0 && !expiryDate.HasValue)
                    {
                        MessageBox.Show(
                            "يجب تحديد تاريخ الصلاحية عند إدخال كمية افتتاحية.");

                        ExpiryDatePicker.Focus();
                        return;
                    }

                    if (quantity > 0 &&
                        expiryDate.Value.Date < DateTime.Today)
                    {
                        MessageBox.Show(
                            "تاريخ الصلاحية لا يمكن أن يكون في الماضي.");

                        ExpiryDatePicker.Focus();
                        return;
                    }
                }

                var existing = FindExistingProduct(name);

                if (existing != null &&
                    (!isEditMode ||
                     existing.Id != productBeingEdited.Id))
                {
                    MessageBox.Show(
                        "لا يمكن حفظ الصنف؛ يوجد صنف بنفس الاسم بالفعل.\n" +
                        "كود الصنف الموجود: " + existing.ProductCode,
                        "صنف مكرر",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var product = new Product
                {
                    Id = isEditMode ? productBeingEdited.Id : 0,

                    // في الإضافة، الخدمة هي المسؤولة عن توليد الكود والباركود.
                    // في التعديل نحتفظ بالقيم القديمة للعرض فقط.
                    ProductCode = isEditMode
                        ? productBeingEdited.ProductCode
                        : null,

                    Barcode = isEditMode
                        ? productBeingEdited.Barcode
                        : null,

                    Name = name,
                    UnitName = unitName,
                    SmallUnitName = smallUnitName,
                    UnitsPerPackage = unitsPerPackage,
                    CategoryId = categoryId,
                    SalePrice = salePrice,
                    PurchasePrice = purchasePrice,

                    DrugCatalogId = isEditMode
                        ? productBeingEdited.DrugCatalogId
                        : selectedCatalogDrug?.Id
                };

                SaveButton.IsEnabled = false;

                if (isEditMode)
                {
                    productService.UpdateProduct(currentUserId, product);

                    MessageBox.Show(
                        "تم تعديل بيانات الصنف بنجاح.\n" +
                        "كود الصنف: " + product.ProductCode +
                        "\nالباركود: " +
                        (string.IsNullOrWhiteSpace(product.Barcode)
                            ? "لا يوجد باركود مسجل"
                            : product.Barcode) +
                        "\n\nلم يتم تغيير الرصيد أو التشغيلات أو الفواتير السابقة.",
                        "نجاح",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    int productId = productService.CreateProduct(
                        currentUserId,
                        product,
                        quantity,
                        BatchNumberTextBox.Text.Trim(),
                        expiryDate);

                    MessageBox.Show(
                        "تم حفظ الصنف بنجاح.\n\n" +
                        "رقم الصنف في قاعدة البيانات: " + productId +
                        "\nكود الصنف: " + product.ProductCode +
                        "\nالباركود: " + product.Barcode +
                        "\nالعبوة: " + unitName +
                        "\nالوحدة الصغيرة: " + smallUnitName +
                        "\nعدد الوحدات داخل العبوة: " + unitsPerPackage +
                        (selectedCatalogDrug != null
                            ? "\nتم ربط الصنف بدليل الأدوية."
                            : "\nتمت إضافة الصنف دون ربطه بدليل الأدوية."),
                        "نجاح",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

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
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء حفظ الصنف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        private bool TryReadNumber(string text, out double value)
        {
            bool success = double.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out value);

            if (!success)
            {
                success = double.TryParse(
                    text,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out value);
            }

            return success &&
                   !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
