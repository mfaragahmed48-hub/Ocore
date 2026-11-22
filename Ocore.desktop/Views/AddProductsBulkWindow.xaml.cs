
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
using System.Windows.Input;

namespace Ocore.desktop.Views
{
    public partial class AddProductsBulkWindow : Window
    {
        private readonly int currentUserId;
        private readonly ProductService productService;

        private readonly ObservableCollection<BulkProductRow> rows =
            new ObservableCollection<BulkProductRow>();

        private List<KeyValuePair<int, string>> categories =
            new List<KeyValuePair<int, string>>();

        public bool HasSavedProducts { get; private set; }

        public AddProductsBulkWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            productService = new ProductService();

            LoadCategories();

            ProductsDataGrid.ItemsSource = rows;

            AddRowButton.Click += AddRowButton_Click;
            RemoveRowButton.Click += RemoveRowButton_Click;
            PasteButton.Click += PasteButton_Click;
            SaveButton.Click += SaveButton_Click;
            CloseButton.Click += CloseButton_Click;

            ProductsDataGrid.PreviewKeyDown +=
                ProductsDataGrid_PreviewKeyDown;

            for (int i = 0; i < 3; i++)
                AddNewRow();

            UpdateSummary();
        }

        private void LoadCategories()
        {
            try
            {
                categories =
                    productService.GetActiveCategories(currentUserId);

                categories.Insert(
                    0,
                    new KeyValuePair<int, string>(
                        0,
                        "بدون تصنيف"));
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

        private void AddNewRow()
        {
            rows.Add(new BulkProductRow(categories));
        }

        private void AddRowButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            AddNewRow();

            ProductsDataGrid.SelectedIndex = rows.Count - 1;
            ProductsDataGrid.ScrollIntoView(
                ProductsDataGrid.SelectedItem);

            ProductsDataGrid.BeginEdit();
        }

        private void RemoveRowButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            BulkProductRow selected =
                ProductsDataGrid.SelectedItem as BulkProductRow;

            if (selected == null)
            {
                MessageBox.Show(
                    "اختر الصف الذي تريد حذفه أولاً.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (selected.IsSaved)
            {
                MessageBox.Show(
                    "لا يمكن حذف صف تم حفظه بالفعل.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            rows.Remove(selected);
            UpdateSummary();
        }

        private void PasteButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            PasteFromClipboard();
        }

        private void ProductsDataGrid_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.V &&
                (Keyboard.Modifiers & ModifierKeys.Control) ==
                ModifierKeys.Control)
            {
                e.Handled = true;
                PasteFromClipboard();
            }
        }

        private void PasteFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    MessageBox.Show(
                        "لا توجد بيانات نصية في الحافظة.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                string clipboardText = Clipboard.GetText();

                if (string.IsNullOrWhiteSpace(clipboardText))
                    return;

                ProductsDataGrid.CommitEdit(
                    DataGridEditingUnit.Cell, true);

                ProductsDataGrid.CommitEdit(
                    DataGridEditingUnit.Row, true);

                string[] lines = clipboardText
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split(new[] { '\n' },
                        StringSplitOptions.RemoveEmptyEntries);

                int addedCount = 0;

                foreach (string line in lines)
                {
                    string[] cells = line.Split('\t');

                    if (cells.Length == 1 &&
                        string.IsNullOrWhiteSpace(cells[0]))
                    {
                        continue;
                    }

                    BulkProductRow row =
                        new BulkProductRow(categories);

                    row.Name = GetCell(cells, 0);
                    row.SelectCategoryByName(GetCell(cells, 1));
                    row.UnitName = "p";
                    row.SmallUnitName = "u";
                    row.UnitsPerPackage = GetCell(cells, 4);
                    row.SalePrice = GetCell(cells, 5);
                    row.PurchasePrice = GetCell(cells, 6);
                    row.InitialQuantity = GetCell(cells, 7);
                    row.BatchNumber = GetCell(cells, 8);
                    row.ExpiryDate = GetCell(cells, 9);

                    rows.Add(row);
                    addedCount++;
                }

                UpdateSummary();

                MessageBox.Show(
                    "تمت إضافة " + addedCount +
                    " صف من الحافظة.\nراجع البيانات قبل الحفظ.",
                    "تم اللصق",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر لصق البيانات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string GetCell(string[] cells, int index)
        {
            if (cells == null || index >= cells.Length)
                return string.Empty;

            return (cells[index] ?? string.Empty).Trim();
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ProductsDataGrid.CommitEdit(
                DataGridEditingUnit.Cell, true);

            ProductsDataGrid.CommitEdit(
                DataGridEditingUnit.Row, true);

            int savedCount = 0;
            int errorCount = 0;

            foreach (BulkProductRow row in rows.ToList())
            {
                if (row.IsSaved)
                    continue;

                if (IsRowEmpty(row))
                    continue;

                try
                {
                    Product product = BuildProduct(row);

                    double initialQuantity =
                        ParseNumber(
                            row.InitialQuantity,
                            "الكمية الافتتاحية");

                    DateTime? expiryDate =
                        ParseExpiryDate(row.ExpiryDate);

                    int productId =
                        productService.CreateProduct(
                            currentUserId,
                            product,
                            initialQuantity,
                            row.BatchNumber,
                            expiryDate);

                    row.IsSaved = true;
                    row.Status = "تم الحفظ بنجاح - رقم الصنف: " +
                                 productId;

                    savedCount++;
                    HasSavedProducts = true;
                }
                catch (Exception ex)
                {
                    row.Status = "خطأ: " + ex.Message;
                    errorCount++;
                }
            }

            UpdateSummary();

            if (savedCount > 0)
            {
                MessageBox.Show(
                    "تم حفظ " + savedCount + " صنف بنجاح." +
                    (errorCount > 0
                        ? "\nتوجد " + errorCount +
                          " صفوف بها أخطاء، ويمكنك تصحيحها وإعادة الحفظ."
                        : "\nتم حفظ جميع الصفوف المدخلة بنجاح."),
                    "نتيجة الحفظ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (errorCount > 0)
            {
                MessageBox.Show(
                    "لم يتم حفظ أي صنف.\nراجع رسائل الأخطاء داخل الجدول.",
                    "تعذر الحفظ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    "أدخل بيانات صنف واحد على الأقل قبل الحفظ.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private bool IsRowEmpty(BulkProductRow row)
        {
            return string.IsNullOrWhiteSpace(row.Name) &&
                   string.IsNullOrWhiteSpace(row.SmallUnitName) &&
                   string.IsNullOrWhiteSpace(row.SalePrice) &&
                   string.IsNullOrWhiteSpace(row.PurchasePrice);
        }

        private Product BuildProduct(BulkProductRow row)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
                throw new ArgumentException("اسم الصنف مطلوب.");

            if (string.IsNullOrWhiteSpace(row.SmallUnitName))
                throw new ArgumentException("الوحدة الصغيرة مطلوبة.");

            int unitsPerPackage;

            if (!int.TryParse(
                row.UnitsPerPackage,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out unitsPerPackage) &&
                !int.TryParse(
                    row.UnitsPerPackage,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out unitsPerPackage))
            {
                throw new ArgumentException(
                    "عدد الوحدات داخل العبوة غير صحيح.");
            }

            if (unitsPerPackage < 1)
                throw new ArgumentException(
                    "عدد الوحدات داخل العبوة يجب أن يكون واحدًا على الأقل.");

            double salePrice =
                ParseNumber(row.SalePrice, "سعر البيع");

            double purchasePrice =
                ParseNumber(row.PurchasePrice, "سعر الشراء");

            if (salePrice < 0 || purchasePrice < 0)
                throw new ArgumentException(
                    "الأسعار لا يمكن أن تكون سالبة.");

            int? categoryId =
                row.CategoryId > 0
                    ? (int?)row.CategoryId
                    : null;

            return new Product
            {
                Name = row.Name.Trim(),
                CategoryId = categoryId,
                UnitName = "p",
                SmallUnitName = "u",
                UnitsPerPackage = unitsPerPackage,
                SalePrice = salePrice,
                PurchasePrice = purchasePrice,
                DrugCatalogId = null
            };
        }

        private double ParseNumber(
            string value,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            double result;

            bool valid =
                double.TryParse(
                    value.Trim(),
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out result) ||
                double.TryParse(
                    value.Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out result);

            if (!valid ||
                double.IsNaN(result) ||
                double.IsInfinity(result))
            {
                throw new ArgumentException(
                    fieldName + " غير صحيح.");
            }

            return result;
        }

        private DateTime? ParseExpiryDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string[] formats =
            {
                "yyyy-MM-dd",
                "dd/MM/yyyy",
                "d/M/yyyy",
                "dd-MM-yyyy",
                "d-M-yyyy"
            };

            DateTime result;

            bool valid = DateTime.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out result);

            if (!valid)
            {
                throw new ArgumentException(
                    "تاريخ الصلاحية غير صحيح. استخدم yyyy-MM-dd أو dd/MM/yyyy.");
            }

            return result.Date;
        }

        private void UpdateSummary()
        {
            int saved = rows.Count(x => x.IsSaved);
            int pending = rows.Count(x =>
                !x.IsSaved && !IsRowEmpty(x));

            SummaryTextBlock.Text =
                "إجمالي الصفوف: " + rows.Count +
                " | تم الحفظ: " + saved +
                " | بانتظار الحفظ: " + pending;
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }

    public class BulkProductRow : INotifyPropertyChanged
    {
        private string name;
        private int categoryId;
        private string unitName = "p";
        private string smallUnitName = "u";
        private string unitsPerPackage = "1";
        private string salePrice = "0";
        private string purchasePrice = "0";
        private string initialQuantity = "0";
        private string batchNumber;
        private string expiryDate;
        private string status;
        private bool isSaved;

        public event PropertyChangedEventHandler PropertyChanged;

        public List<KeyValuePair<int, string>> CategoryOptions
        {
            get;
            private set;
        }

        public BulkProductRow(
            List<KeyValuePair<int, string>> categoryOptions)
        {
            CategoryOptions = categoryOptions ??
                new List<KeyValuePair<int, string>>();
        }

        public string Name
        {
            get { return name; }
            set { name = value; OnPropertyChanged("Name"); }
        }

        public int CategoryId
        {
            get { return categoryId; }
            set
            {
                categoryId = value;
                OnPropertyChanged("CategoryId");
                OnPropertyChanged("CategoryName");
            }
        }

        public string CategoryName
        {
            get
            {
                var item = CategoryOptions.FirstOrDefault(
                    x => x.Key == CategoryId);

                return item.Value ?? "بدون تصنيف";
            }
        }

        public string UnitName
        {
            get { return unitName; }
            set { unitName = value; OnPropertyChanged("UnitName"); }
        }

        public string SmallUnitName
        {
            get { return smallUnitName; }
            set { smallUnitName = value; OnPropertyChanged("SmallUnitName"); }
        }

        public string UnitsPerPackage
        {
            get { return unitsPerPackage; }
            set { unitsPerPackage = value; OnPropertyChanged("UnitsPerPackage"); }
        }

        public string SalePrice
        {
            get { return salePrice; }
            set { salePrice = value; OnPropertyChanged("SalePrice"); }
        }

        public string PurchasePrice
        {
            get { return purchasePrice; }
            set { purchasePrice = value; OnPropertyChanged("PurchasePrice"); }
        }

        public string InitialQuantity
        {
            get { return initialQuantity; }
            set { initialQuantity = value; OnPropertyChanged("InitialQuantity"); }
        }

        public string BatchNumber
        {
            get { return batchNumber; }
            set { batchNumber = value; OnPropertyChanged("BatchNumber"); }
        }

        public string ExpiryDate
        {
            get { return expiryDate; }
            set { expiryDate = value; OnPropertyChanged("ExpiryDate"); }
        }

        public string Status
        {
            get { return status; }
            set { status = value; OnPropertyChanged("Status"); }
        }

        public bool IsSaved
        {
            get { return isSaved; }
            set { isSaved = value; OnPropertyChanged("IsSaved"); }
        }

        public void SelectCategoryByName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var match = CategoryOptions.FirstOrDefault(
                x => string.Equals(
                    x.Value,
                    value.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (match.Key > 0)
                CategoryId = match.Key;
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(
                    this,
                    new PropertyChangedEventArgs(propertyName));
        }
    }
}
