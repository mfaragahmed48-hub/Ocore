
using ClosedXML.Excel;
using Ocore.desktop.Data;
using Ocore.desktop.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Ocore.desktop.Services
{
    public class ProductExcelRow
    {
        public int RowNumber { get; set; }
        public string ProductCode { get; set; }
        public string Name { get; set; }
        public string Barcode { get; set; }
        public string CategoryName { get; set; }
        public string UnitName { get; set; }
        public string SmallUnitName { get; set; }
        public int UnitsPerPackage { get; set; }
        public double PurchasePrice { get; set; }
        public double SalePrice { get; set; }
        public double MaxDiscountPercentage { get; set; }
        public double MinimumStockLevel { get; set; }
        public string Error { get; set; }
    }

    public class ProductExcelService
    {
        private readonly string connectionString;

        private static readonly string[] Headers =
        {
            "كود الصنف",
            "اسم الصنف",
            "الباركود",
            "التصنيف",
            "الوحدة (P)",
            "الوحدة الصغيرة (U)",
            "عدد U داخل P",
            "سعر الشراء",
            "سعر البيع",
            "حد الخصم",
            "الحد الأدنى",
            "الرصيد الحالي",
            "الرصيد المنتهي"
        };

        public ProductExcelService()
        {
            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        public void ExportProducts(
            string filePath,
            IEnumerable<Product> products)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("مسار ملف التصدير غير صحيح.");

            if (products == null)
                throw new ArgumentNullException(nameof(products));

            var productList = products.ToList();
            var categories = GetCategoryNames();

            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("الأصناف");

                for (int i = 0; i < Headers.Length; i++)
                {
                    sheet.Cell(1, i + 1).Value = Headers[i];
                }

                var header = sheet.Range(1, 1, 1, Headers.Length);

                header.Style.Font.Bold = true;
                header.Style.Font.FontColor = XLColor.White;
                header.Style.Fill.BackgroundColor =
                    XLColor.FromHtml("#217346");
                header.Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Center;
                header.Style.Alignment.Vertical =
                    XLAlignmentVerticalValues.Center;

                int row = 2;

                foreach (var product in productList)
                {
                    sheet.Cell(row, 1).Value = product.ProductCode;
                    sheet.Cell(row, 2).Value = product.Name;
                    sheet.Cell(row, 3).Value = product.Barcode ?? "";

                    string categoryName = "";

                    if (product.CategoryId.HasValue &&
                        categories.ContainsKey(product.CategoryId.Value))
                    {
                        categoryName =
                            categories[product.CategoryId.Value];
                    }

                    sheet.Cell(row, 4).Value = categoryName;
                    sheet.Cell(row, 5).Value = product.UnitName;
                    sheet.Cell(row, 6).Value = product.SmallUnitName;
                    sheet.Cell(row, 7).Value = product.UnitsPerPackage;
                    sheet.Cell(row, 8).Value = product.PurchasePrice;
                    sheet.Cell(row, 9).Value = product.SalePrice;
                    sheet.Cell(row, 10).Value =
                        product.MaxDiscountPercentage;
                    sheet.Cell(row, 11).Value =
                        product.MinimumStockLevel;

                    // بيانات الرصيد للتصدير فقط.
                    sheet.Cell(row, 12).Value = product.Quantity;
                    sheet.Cell(row, 13).Value =
                        product.ExpiredQuantity;

                    row++;
                }

                sheet.SheetView.RightToLeft = true;
                sheet.SheetView.FreezeRows(1);
                sheet.RangeUsed().SetAutoFilter();

                sheet.Column(1).Width = 16;
                sheet.Column(2).Width = 32;
                sheet.Column(3).Width = 20;
                sheet.Column(4).Width = 22;
                sheet.Column(5).Width = 16;
                sheet.Column(6).Width = 18;
                sheet.Column(7).Width = 17;
                sheet.Columns(8, 13).Width = 17;

                if (row > 2)
                {
                    sheet.Range(2, 8, row - 1, 13)
                        .Style.NumberFormat.Format = "0.###";
                }

                sheet.Cell(row + 1, 1).Value =
                    "تنبيه: الرصيد الحالي والرصيد المنتهي للعرض فقط، ولا يتم استيرادهما.";

                sheet.Range(row + 1, 1, row + 1, Headers.Length)
                    .Merge();

                sheet.Cell(row + 1, 1).Style.Font.Italic = true;
                sheet.Cell(row + 1, 1).Style.Font.FontColor =
                    XLColor.DarkRed;

                workbook.SaveAs(filePath);
            }
        }

        public List<ProductExcelRow> ReadImportFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) ||
                !File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "ملف Excel غير موجود.");
            }

            var result = new List<ProductExcelRow>();

            using (var workbook = new XLWorkbook(filePath))
            {
                var sheet = workbook.Worksheets.FirstOrDefault();

                if (sheet == null)
                    throw new InvalidOperationException(
                        "ملف Excel لا يحتوي على ورقة عمل.");

                var headerMap = ReadHeaders(sheet);

                ValidateRequiredHeaders(headerMap);

                int lastRow = sheet.LastRowUsed() == null
                    ? 1
                    : sheet.LastRowUsed().RowNumber();

                for (int row = 2; row <= lastRow; row++)
                {
                    if (IsEmptyRow(sheet, row, headerMap))
                        continue;

                    var item = new ProductExcelRow
                    {
                        RowNumber = row,
                        ProductCode = ReadText(sheet, row, headerMap, "كود الصنف"),
                        Name = ReadText(sheet, row, headerMap, "اسم الصنف"),
                        Barcode = ReadText(sheet, row, headerMap, "الباركود"),
                        CategoryName = ReadText(sheet, row, headerMap, "التصنيف"),
                        UnitName = ReadText(sheet, row, headerMap, "الوحدة (P)"),
                        SmallUnitName = ReadText(sheet, row, headerMap, "الوحدة الصغيرة (U)")
                    };

                    item.Error = ValidateRow(
                        sheet,
                        row,
                        headerMap,
                        item);

                    result.Add(item);
                }
            }

            ValidateFileDuplicates(result);

            return result;
        }

        private Dictionary<string, int> ReadHeaders(
            IXLWorksheet sheet)
        {
            var result = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

            var lastCell = sheet.Row(1).LastCellUsed();

            if (lastCell == null)
                return result;

            for (int column = 1;
                 column <= lastCell.Address.ColumnNumber;
                 column++)
            {
                string name = Normalize(
                    sheet.Cell(1, column).GetString());

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (result.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        "يوجد عمود مكرر في ملف Excel: " + name);
                }

                result.Add(name, column);
            }

            return result;
        }

        private void ValidateRequiredHeaders(
            Dictionary<string, int> headers)
        {
            string[] required =
            {
                "كود الصنف",
                "اسم الصنف",
                "الباركود",
                "التصنيف",
                "الوحدة (P)",
                "الوحدة الصغيرة (U)",
                "عدد U داخل P",
                "سعر الشراء",
                "سعر البيع",
                "حد الخصم",
                "الحد الأدنى"
            };

            var missing = required
                .Where(x => !headers.ContainsKey(x))
                .ToList();

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "ملف Excel لا يحتوي على الأعمدة المطلوبة:\n" +
                    string.Join("\n", missing));
            }
        }

        private string ValidateRow(
            IXLWorksheet sheet,
            int row,
            Dictionary<string, int> headers,
            ProductExcelRow item)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(item.Name))
                errors.Add("اسم الصنف مطلوب.");

            if (string.IsNullOrWhiteSpace(item.UnitName))
                errors.Add("الوحدة (P) مطلوبة.");

            if (string.IsNullOrWhiteSpace(item.SmallUnitName))
                errors.Add("الوحدة الصغيرة (U) مطلوبة.");

            if (!TryReadNumber(
                sheet, row, headers["عدد U داخل P"],
                out double units) ||
                units < 1 ||
                units > int.MaxValue ||
                units != Math.Truncate(units))
            {
                errors.Add("عدد U داخل P يجب أن يكون عددًا صحيحًا أكبر من صفر.");
            }
            else
            {
                item.UnitsPerPackage = (int)units;
            }

            if (!TryReadNumber(
                sheet, row, headers["سعر الشراء"],
                out double purchase) || purchase < 0)
            {
                errors.Add("سعر الشراء غير صحيح.");
            }
            else
            {
                item.PurchasePrice = purchase;
            }

            if (!TryReadNumber(
                sheet, row, headers["سعر البيع"],
                out double sale) || sale < 0)
            {
                errors.Add("سعر البيع غير صحيح.");
            }
            else
            {
                item.SalePrice = sale;
            }

            if (!TryReadNumber(
                sheet, row, headers["حد الخصم"],
                out double discount) ||
                discount < 0 ||
                discount > 100)
            {
                errors.Add("حد الخصم يجب أن يكون من 0 إلى 100.");
            }
            else
            {
                item.MaxDiscountPercentage = discount;
            }

            if (!TryReadNumber(
                sheet, row, headers["الحد الأدنى"],
                out double minimum) || minimum < 0)
            {
                errors.Add("الحد الأدنى للمخزون غير صحيح.");
            }
            else
            {
                item.MinimumStockLevel = minimum;
            }

            return string.Join(" ", errors);
        }

        private void ValidateFileDuplicates(
            List<ProductExcelRow> rows)
        {
            var duplicateCodes = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => Normalize(x.ProductCode),
                    StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1);

            foreach (var group in duplicateCodes)
            {
                foreach (var item in group)
                    AppendError(item, "كود الصنف مكرر داخل الملف.");
            }

            var duplicateBarcodes = rows
                .Where(x => !string.IsNullOrWhiteSpace(x.Barcode))
                .GroupBy(x => Normalize(x.Barcode),
                    StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1);

            foreach (var group in duplicateBarcodes)
            {
                foreach (var item in group)
                    AppendError(item, "الباركود مكرر داخل الملف.");
            }
        }

        private Dictionary<int, string> GetCategoryNames()
        {
            var result = new Dictionary<int, string>();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    "SELECT Id, Name FROM Categories;",
                    connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result[Convert.ToInt32(reader["Id"])] =
                            Convert.ToString(reader["Name"]);
                    }
                }
            }

            return result;
        }

        private bool IsEmptyRow(
            IXLWorksheet sheet,
            int row,
            Dictionary<string, int> headers)
        {
            foreach (var column in headers.Values)
            {
                if (!string.IsNullOrWhiteSpace(
                    sheet.Cell(row, column).GetString()))
                    return false;
            }

            return true;
        }

        private string ReadText(
            IXLWorksheet sheet,
            int row,
            Dictionary<string, int> headers,
            string header)
        {
            return Normalize(
                sheet.Cell(row, headers[header]).GetFormattedString());
        }

        private bool TryReadNumber(
            IXLWorksheet sheet,
            int row,
            int column,
            out double value)
        {
            var cell = sheet.Cell(row, column);

            if (cell.TryGetValue<double>(out value))
            {
                return !double.IsNaN(value) &&
                       !double.IsInfinity(value);
            }

            string text = Normalize(cell.GetFormattedString());

            return double.TryParse(
                       text,
                       NumberStyles.Any,
                       CultureInfo.CurrentCulture,
                       out value) ||
                   double.TryParse(
                       text,
                       NumberStyles.Any,
                       CultureInfo.InvariantCulture,
                       out value);
        }

        private string Normalize(string value)
        {
            return (value ?? "").Trim();
        }

        private void AppendError(
            ProductExcelRow item,
            string error)
        {
            item.Error = string.IsNullOrWhiteSpace(item.Error)
                ? error
                : item.Error + " " + error;
        }
    }
}
