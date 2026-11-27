
using Ocore.desktop.Models;
using Ocore.desktop.Services;
using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
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

            // متابعة إضافة وحذف الصفوف وتعديل بياناتها.
            rows.CollectionChanged += Rows_CollectionChanged;

            AddRowButton.Click += AddRowButton_Click;
            RemoveRowButton.Click += RemoveRowButton_Click;
            PasteButton.Click += PasteButton_Click;
            ImportExcelButton.Click += ImportExcelButton_Click;
            ExportExcelButton.Click += ExportExcelButton_Click;
            SaveButton.Click += SaveButton_Click;
            CloseButton.Click += CloseButton_Click;

            ProductsDataGrid.PreviewKeyDown +=
                ProductsDataGrid_PreviewKeyDown;

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
                Ocore.desktop.ErrorDialog.Show("تحميل تصنيفات الأصناف", ex);

                Close();
            }
        }

        // متابعة تغييرات مجموعة الصفوف.
        private void Rows_CollectionChanged(
            object sender,
            NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (BulkProductRow row in e.OldItems)
                {
                    row.PropertyChanged -=
                        BulkProductRow_PropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (BulkProductRow row in e.NewItems)
                {
                    row.PropertyChanged +=
                        BulkProductRow_PropertyChanged;
                }
            }

            UpdateSummary();
        }

        // تحديث الملخص فور تعديل أي خلية.
        private void BulkProductRow_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            UpdateSummary();
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
            RemoveSelectedRow();
        }

        private void RemoveSelectedRow()
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
                return;
            }

            if (e.Key == Key.Delete)
            {
                // لا نحذف الصف إذا كان المستخدم يحرر نصًا داخل خلية.
                if (ProductsDataGrid.CurrentColumn != null &&
                    Keyboard.FocusedElement is System.Windows.Controls.TextBox)
                {
                    return;
                }

                e.Handled = true;
                RemoveSelectedRow();
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

                clipboardText = clipboardText.TrimEnd('\r', '\n');

                string[] lines = clipboardText.Split(
                    new[] { "\r\n", "\n", "\r" },
                    StringSplitOptions.None);

                if (lines.Length == 0)
                    return;

                // إذا لم توجد علامات Tab فهذا عمود واحد من Excel.
                bool isSingleColumn = !lines.Any(
                    line => line.Contains("\t"));

                if (isSingleColumn)
                {
                    PasteSingleColumn(lines);
                    return;
                }

                // لصق جدول كامل بالطريقة القديمة.
                int addedCount = 0;

                foreach (string line in lines)
                {
                    string[] cells = line.Split('\t');

                    if (cells.Length == 1 &&
                        string.IsNullOrWhiteSpace(cells[0]))
                        continue;

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
                    row.RequiresExpiryDate =
                        ParseRequiresExpiryDate(GetCell(cells, 10));

                    rows.Add(row);
                    addedCount++;
                }

                UpdateSummary();

                MessageBox.Show(
                    "تمت إضافة " + addedCount +
                    " صف من الجدول.\nراجع البيانات قبل الحفظ.",
                    "تم اللصق",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("لصق بيانات الأصناف", ex);
            }
        }

        // لصق عمود واحد في العمود الذي اختاره المستخدم.
        private void PasteSingleColumn(string[] values)
        {
            if (ProductsDataGrid.CurrentCell.Column == null)
            {
                MessageBox.Show(
                    "اضغط أولاً على الخلية التي تريد بدء اللصق منها.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            int columnIndex =
                ProductsDataGrid.CurrentCell.Column.DisplayIndex;

            // الأعمدة المسموح باللصق فيها.
            int[] editableColumns =
            {
        0, 1, 4, 5, 6, 7, 8, 9, 10
    };

            if (!editableColumns.Contains(columnIndex))
            {
                MessageBox.Show(
                    "هذا العمود غير قابل للتعديل أو اللصق.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            BulkProductRow currentRow =
                ProductsDataGrid.CurrentCell.Item as BulkProductRow;

            int startIndex = currentRow != null
                ? rows.IndexOf(currentRow)
                : ProductsDataGrid.SelectedIndex;

            if (startIndex < 0)
            {
                MessageBox.Show(
                    "حدد أول خلية في العمود المطلوب ثم حاول مرة أخرى.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            int pastedCount = 0;
            int skippedCount = 0;

            for (int i = 0; i < values.Length; i++)
            {
                int rowIndex = startIndex + i;

                // إضافة صفوف تلقائياً عند الحاجة.
                while (rows.Count <= rowIndex)
                    AddNewRow();

                BulkProductRow row = rows[rowIndex];

                // حماية الأصناف التي تم حفظها بالفعل.
                if (row.IsSaved)
                {
                    skippedCount++;
                    continue;
                }

                SetColumnValue(
                    row,
                    columnIndex,
                    values[i]);

                pastedCount++;
            }

            ProductsDataGrid.SelectedIndex =
                Math.Min(startIndex, rows.Count - 1);

            ProductsDataGrid.ScrollIntoView(
                ProductsDataGrid.SelectedItem);

            UpdateSummary();

            MessageBox.Show(
                "تم لصق " + pastedCount +
                " قيمة في عمود " + GetColumnHeader(columnIndex) +
                (skippedCount > 0
                    ? "\nتم تخطي " + skippedCount +
                      " صف محفوظ مسبقاً."
                    : ""),
                "تم اللصق",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // وضع القيمة في الخلية المناسبة.
        private void SetColumnValue(
            BulkProductRow row,
            int columnIndex,
            string value)
        {
            value = (value ?? string.Empty).Trim();

            switch (columnIndex)
            {
                case 0:
                    row.Name = value;
                    break;

                case 1:
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        row.CategoryId = 0;
                    }
                    else
                    {
                        var category = categories.FirstOrDefault(
                            x => x.Key > 0 &&
                            string.Equals(
                                x.Value,
                                value,
                                StringComparison.OrdinalIgnoreCase));

                        if (category.Key > 0)
                        {
                            row.CategoryId = category.Key;
                        }
                        else
                        {
                            row.CategoryId = 0;
                            row.Status =
                                "تنبيه: التصنيف غير موجود";
                        }
                    }
                    break;

                case 4:
                    row.UnitsPerPackage = value;
                    break;

                case 5:
                    row.SalePrice = value;
                    break;

                case 6:
                    row.PurchasePrice = value;
                    break;

                case 7:
                    row.InitialQuantity = value;
                    break;

                case 8:
                    row.BatchNumber = value;
                    break;

                case 9:
                    row.ExpiryDate = value;
                    break;

                case 10:
                    row.RequiresExpiryDate = ParseRequiresExpiryDate(value);
                    break;
            }
        }

        private string GetColumnHeader(int columnIndex)
        {
            switch (columnIndex)
            {
                case 0: return "اسم الصنف";
                case 1: return "التصنيف";
                case 4: return "عدد الوحدات";
                case 5: return "سعر البيع";
                case 6: return "سعر الشراء";
                case 7: return "الكمية الافتتاحية";
                case 8: return "رقم التشغيلة";
                case 9: return "تاريخ الصلاحية";
                case 10: return "يتطلب تاريخ الصلاحية";
                default: return "غير معروف";
            }
        }

        private string GetCell(string[] cells, int index)
        {
            if (cells == null || index >= cells.Length)
                return string.Empty;

            return (cells[index] ?? string.Empty).Trim();
        }


        // استيراد ملف Excel وإضافة صفوفه إلى الجدول الحالي دون حذف البيانات الموجودة.
        private void ImportExcelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "اختيار ملف أصناف Excel",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                ProductsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                ProductsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

                int importedCount = 0;
                int warningCount = 0;

                using (var workbook = new XLWorkbook(dialog.FileName))
                {
                    var sheet = workbook.Worksheets.FirstOrDefault();

                    if (sheet == null)
                        throw new InvalidOperationException(
                            "ملف Excel لا يحتوي على ورقة عمل.");

                    var headerMap = new Dictionary<string, int>(
                        StringComparer.OrdinalIgnoreCase);

                    var lastHeaderCell = sheet.Row(1).LastCellUsed();

                    if (lastHeaderCell == null)
                        throw new InvalidOperationException(
                            "الصف الأول في ملف Excel يجب أن يحتوي على عناوين الأعمدة.");

                    for (int column = 1;
                         column <= lastHeaderCell.Address.ColumnNumber;
                         column++)
                    {
                        string header = NormalizeExcelHeader(
                            sheet.Cell(1, column).GetString());

                        if (string.IsNullOrWhiteSpace(header))
                            continue;

                        if (headerMap.ContainsKey(header))
                            throw new InvalidOperationException(
                                "يوجد عنوان عمود مكرر في ملف Excel: " + header);

                        headerMap.Add(header, column);
                    }

                    int nameColumn = FindExcelColumn(
                        headerMap, "اسم الصنف", "اسم الصنف*");
                    int unitsColumn = FindExcelColumn(
                        headerMap, "عدد الوحدات", "عدد الوحدات*", "عدد الوحدات داخل العبوة");
                    int saleColumn = FindExcelColumn(
                        headerMap, "سعر البيع", "سعر البيع*");
                    int purchaseColumn = FindExcelColumn(
                        headerMap, "سعر الشراء", "سعر الشراء*");

                    if (nameColumn == 0 ||
                        unitsColumn == 0 ||
                        saleColumn == 0 ||
                        purchaseColumn == 0)
                    {
                        throw new InvalidOperationException(
                            "ملف Excel لا يحتوي على الأعمدة الأساسية المطلوبة:" +
                            Environment.NewLine +
                            "اسم الصنف، عدد الوحدات، سعر البيع، سعر الشراء.");
                    }

                    int categoryColumn = FindExcelColumn(headerMap, "التصنيف");
                    int batchColumn = FindExcelColumn(headerMap, "رقم التشغيلة");
                    int expiryColumn = FindExcelColumn(headerMap, "تاريخ الصلاحية");
                    int requiresExpiryColumn = FindExcelColumn(
                        headerMap, "يتطلب تاريخ الصلاحية", "إلزام تاريخ الصلاحية");

                    int lastRow = sheet.LastRowUsed() == null
                        ? 1
                        : sheet.LastRowUsed().RowNumber();

                    for (int rowNumber = 2;
                         rowNumber <= lastRow;
                         rowNumber++)
                    {
                        bool isEmpty = true;

                        for (int column = 1;
                             column <= lastHeaderCell.Address.ColumnNumber;
                             column++)
                        {
                            if (!string.IsNullOrWhiteSpace(
                                sheet.Cell(rowNumber, column).GetFormattedString()))
                            {
                                isEmpty = false;
                                break;
                            }
                        }

                        if (isEmpty)
                            continue;

                        var row = new BulkProductRow(categories)
                        {
                            Name = ReadExcelText(sheet.Cell(rowNumber, nameColumn)),
                            UnitsPerPackage = ReadExcelText(sheet.Cell(rowNumber, unitsColumn)),
                            SalePrice = ReadExcelText(sheet.Cell(rowNumber, saleColumn)),
                            PurchasePrice = ReadExcelText(sheet.Cell(rowNumber, purchaseColumn))
                        };

                        var rowWarnings = new List<string>();

                        if (categoryColumn > 0)
                        {
                            string categoryName = ReadExcelText(
                                sheet.Cell(rowNumber, categoryColumn));

                            if (!string.IsNullOrWhiteSpace(categoryName))
                            {
                                var category = categories.FirstOrDefault(
                                    item => item.Key > 0 &&
                                    string.Equals(
                                        item.Value,
                                        categoryName,
                                        StringComparison.OrdinalIgnoreCase));

                                if (category.Key > 0)
                                    row.CategoryId = category.Key;
                                else
                                    rowWarnings.Add(
                                        "التصنيف غير موجود وسيتم تركه بدون تصنيف");
                            }
                        }

                        if (batchColumn > 0)
                            row.BatchNumber = ReadExcelText(
                                sheet.Cell(rowNumber, batchColumn));

                        if (expiryColumn > 0)
                            row.ExpiryDate = ReadExcelDateOrText(
                                sheet.Cell(rowNumber, expiryColumn));

                        if (requiresExpiryColumn > 0)
                        {
                            string requiresExpiry = ReadExcelText(
                                sheet.Cell(rowNumber, requiresExpiryColumn));

                            try
                            {
                                row.RequiresExpiryDate =
                                    ParseRequiresExpiryDate(requiresExpiry);
                            }
                            catch (Exception ex)
                            {
                                row.RequiresExpiryDate = false;
                                rowWarnings.Add(ex.Message);
                            }
                        }

                        if (string.IsNullOrWhiteSpace(row.Name))
                            rowWarnings.Add("اسم الصنف مطلوب");

                        if (rowWarnings.Count > 0)
                        {
                            row.Status = "مراجعة: " +
                                string.Join("؛ ", rowWarnings);
                            warningCount++;
                        }

                        rows.Add(row);
                        importedCount++;
                    }
                }

                UpdateSummary();

                MessageBox.Show(
                    "تم استيراد " + importedCount +
                    " صف وإضافتها إلى الجدول الحالي." +
                    (warningCount > 0
                        ? Environment.NewLine + "توجد " + warningCount +
                          " صفوف تحتاج إلى مراجعة قبل الحفظ."
                        : Environment.NewLine + "راجع البيانات ثم اضغط حفظ الأصناف."),
                    "نتيجة استيراد Excel",
                    MessageBoxButton.OK,
                    warningCount > 0
                        ? MessageBoxImage.Warning
                        : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show(
                    "استيراد أصناف من Excel",
                    ex);
            }
        }

        // تصدير الصفوف التي لم يتم حفظها بعد حتى لا يعاد استيراد أصناف محفوظة بالخطأ.
        private void ExportExcelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ProductsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ProductsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var exportRows = rows
                .Where(row => !row.IsSaved && !IsRowEmpty(row))
                .ToList();

            var dialog = new SaveFileDialog
            {
                Title = "تصدير مجموعة الأصناف إلى Excel",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                FileName = "مجموعة_أصناف_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmm")
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                string[] headers =
                {
                    "اسم الصنف",
                    "التصنيف",
                    "العبوة",
                    "الوحدة الصغيرة",
                    "عدد الوحدات",
                    "سعر البيع",
                    "سعر الشراء",
                    "الكمية الافتتاحية",
                    "رقم التشغيلة",
                    "تاريخ الصلاحية",
                    "يتطلب تاريخ الصلاحية"
                };

                using (var workbook = new XLWorkbook())
                {
                    var sheet = workbook.Worksheets.Add("مجموعة الأصناف");

                    for (int column = 0; column < headers.Length; column++)
                        sheet.Cell(1, column + 1).Value = headers[column];

                    var header = sheet.Range(1, 1, 1, headers.Length);
                    header.Style.Font.Bold = true;
                    header.Style.Font.FontColor = XLColor.White;
                    header.Style.Fill.BackgroundColor =
                        XLColor.FromHtml("#217346");
                    header.Style.Alignment.Horizontal =
                        XLAlignmentHorizontalValues.Center;
                    header.Style.Alignment.Vertical =
                        XLAlignmentVerticalValues.Center;
                    header.Style.Alignment.WrapText = true;

                    int excelRow = 2;

                    foreach (var row in exportRows)
                    {
                        sheet.Cell(excelRow, 1).Value = row.Name ?? "";
                        sheet.Cell(excelRow, 2).Value = row.CategoryName ?? "";
                        sheet.Cell(excelRow, 3).Value = row.UnitName ?? "p";
                        sheet.Cell(excelRow, 4).Value = row.SmallUnitName ?? "u";
                        sheet.Cell(excelRow, 5).Value = row.UnitsPerPackage ?? "1";
                        sheet.Cell(excelRow, 6).Value = row.SalePrice ?? "0";
                        sheet.Cell(excelRow, 7).Value = row.PurchasePrice ?? "0";
                        sheet.Cell(excelRow, 8).Value = row.InitialQuantity ?? "0";
                        sheet.Cell(excelRow, 9).Value = row.BatchNumber ?? "";
                        sheet.Cell(excelRow, 10).Value = row.ExpiryDate ?? "";
                        sheet.Cell(excelRow, 11).Value =
                            row.RequiresExpiryDate ? "نعم" : "لا";

                        excelRow++;
                    }

                    sheet.RightToLeft = true;
                    sheet.SheetView.FreezeRows(1);
                    sheet.Range(1, 1, Math.Max(1, excelRow - 1), headers.Length)
                        .SetAutoFilter();

                    sheet.Column(1).Width = 32;
                    sheet.Column(2).Width = 22;
                    sheet.Columns(3, 4).Width = 16;
                    sheet.Columns(5, 8).Width = 18;
                    sheet.Columns(9, 10).Width = 20;
                    sheet.Column(11).Width = 22;
                    sheet.Row(1).Height = 32;

                    workbook.SaveAs(dialog.FileName);
                }

                MessageBox.Show(
                    "تم تصدير " + exportRows.Count +
                    " صف إلى ملف Excel بنجاح." +
                    (exportRows.Count == 0
                        ? Environment.NewLine +
                          "تم إنشاء قالب فارغ بعناوين الأعمدة."
                        : ""),
                    "تم التصدير",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show(
                    "تصدير مجموعة الأصناف إلى Excel",
                    ex);
            }
        }

        private int FindExcelColumn(
            Dictionary<string, int> headers,
            params string[] possibleNames)
        {
            foreach (string name in possibleNames)
            {
                string normalized = NormalizeExcelHeader(name);
                int column;

                if (headers.TryGetValue(normalized, out column))
                    return column;
            }

            return 0;
        }

        private string NormalizeExcelHeader(string value)
        {
            return new string(
                (value ?? "")
                    .Trim()
                    .Replace("*", "")
                    .Where(character => !char.IsWhiteSpace(character))
                    .ToArray());
        }

        private string ReadExcelText(IXLCell cell)
        {
            return (cell.GetFormattedString() ?? "").Trim();
        }

        private string ReadExcelDateOrText(IXLCell cell)
        {
            DateTime date;

            if (cell.TryGetValue<DateTime>(out date))
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return ReadExcelText(cell);
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
            List<string> saveErrorDetails = new List<string>();

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
                    saveErrorDetails.Add(
                        "الصنف: " + (row.Name ?? "غير محدد") +
                        Environment.NewLine + ex.ToString());
                    errorCount++;
                }
            }

            UpdateSummary();

            if (saveErrorDetails.Count > 0)
            {
                Ocore.desktop.ErrorDialog.Show(
                    "أخطاء حفظ الأصناف",
                    new Exception(string.Join(
                        Environment.NewLine + Environment.NewLine +
                        "------------------------------" +
                        Environment.NewLine + Environment.NewLine,
                        saveErrorDetails)));
            }

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

        // اعتبار الصف فارغًا إذا لم يدخل المستخدم أي بيانات فعلية.
        private bool IsRowEmpty(BulkProductRow row)
        {
            return string.IsNullOrWhiteSpace(row.Name) &&
                   row.CategoryId == 0 &&
                   IsDefaultValue(row.UnitsPerPackage, "1") &&
                   IsDefaultValue(row.SalePrice, "0") &&
                   IsDefaultValue(row.PurchasePrice, "0") &&
                   IsDefaultValue(row.InitialQuantity, "0") &&
                   string.IsNullOrWhiteSpace(row.BatchNumber) &&
                   string.IsNullOrWhiteSpace(row.ExpiryDate) &&
                   !row.RequiresExpiryDate;
        }

        private bool IsDefaultValue(string value, string defaultValue)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   string.Equals(
                       value.Trim(),
                       defaultValue,
                       StringComparison.Ordinal);
        }

        private Product BuildProduct(BulkProductRow row)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
                throw new ArgumentException("اسم الصنف مطلوب.");

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
                RequiresExpiryDate = row.RequiresExpiryDate,
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

        private bool ParseRequiresExpiryDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "y":
                case "نعم":
                case "صح":
                case "✓":
                    return true;

                case "0":
                case "false":
                case "no":
                case "n":
                case "لا":
                case "خطأ":
                case "✗":
                    return false;

                default:
                    throw new ArgumentException(
                        "قيمة إلزام تاريخ الصلاحية غير صحيحة. استخدم نعم أو لا.");
            }
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
            if (SummaryTextBlock == null)
                return;

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
        private bool requiresExpiryDate;
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
            set
            {
                name = value;
                OnPropertyChanged("Name");
            }
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
            set
            {
                unitName = value;
                OnPropertyChanged("UnitName");
            }
        }

        public string SmallUnitName
        {
            get { return smallUnitName; }
            set
            {
                smallUnitName = value;
                OnPropertyChanged("SmallUnitName");
            }
        }

        public string UnitsPerPackage
        {
            get { return unitsPerPackage; }
            set
            {
                unitsPerPackage = value;
                OnPropertyChanged("UnitsPerPackage");
            }
        }

        public string SalePrice
        {
            get { return salePrice; }
            set
            {
                salePrice = value;
                OnPropertyChanged("SalePrice");
            }
        }

        public string PurchasePrice
        {
            get { return purchasePrice; }
            set
            {
                purchasePrice = value;
                OnPropertyChanged("PurchasePrice");
            }
        }

        public string InitialQuantity
        {
            get { return initialQuantity; }
            set
            {
                initialQuantity = value;
                OnPropertyChanged("InitialQuantity");
            }
        }

        public string BatchNumber
        {
            get { return batchNumber; }
            set
            {
                batchNumber = value;
                OnPropertyChanged("BatchNumber");
            }
        }

        public string ExpiryDate
        {
            get { return expiryDate; }
            set
            {
                expiryDate = value;
                OnPropertyChanged("ExpiryDate");
            }
        }

        public string Status
        {
            get { return status; }
            set
            {
                status = value;
                OnPropertyChanged("Status");
            }
        }

        public bool RequiresExpiryDate
        {
            get { return requiresExpiryDate; }
            set
            {
                requiresExpiryDate = value;
                OnPropertyChanged("RequiresExpiryDate");
            }
        }

        public bool IsSaved
        {
            get { return isSaved; }
            set
            {
                isSaved = value;
                OnPropertyChanged("IsSaved");
            }
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
            {
                PropertyChanged(
                    this,
                    new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
