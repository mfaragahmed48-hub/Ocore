
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class SalesProductSearchService
    {
        private readonly ProductService productService;
        private readonly string connectionString;

        private class CatalogDrug
        {
            public int Id { get; set; }
            public string TradeName { get; set; }
            public string TradeNameArabic { get; set; }
            public string GenericName { get; set; }
            public string Strength { get; set; }
            public string DosageForm { get; set; }
            public string RouteOfAdministration { get; set; }
            public string Manufacturer { get; set; }
            public string DrugClass { get; set; }
        }

        public SalesProductSearchService()
        {
            productService = new ProductService();

            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        public List<SalesProductSearchResult> GetSearchResults(
            int currentUserId)
        {
            // تحميل الأصناف المتاحة للبيع فعليًا
            // واستبعاد الأصناف التي ليس لها رصيد صالح.
            var products =
                productService.GetProductsAvailableForSale(currentUserId)
                .Where(p => p != null && p.Quantity > 0)
                .ToList();

            var results = products.Select(p =>
                new SalesProductSearchResult
                {
                    Product = p,
                    ProductCode = p.ProductCode,
                    Name = p.Name,
                    TradeNameArabic = string.Empty,
                    UnitName = p.UnitName,
                    SmallUnitName = p.SmallUnitName,
                    UnitsPerPackage = p.UnitsPerPackage,
                    SalePrice = p.SalePrice,
                    Quantity = p.Quantity,
                    Barcode = p.Barcode
                }).ToList();

            // تحميل دليل الأدوية لاستخدامه في إثراء بيانات
            // الأصناف الموجودة بالفعل في المخزون فقط.
            var catalogDrugs = new List<CatalogDrug>();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                string sql = @"
SELECT
    Id,
    TradeName,
    TradeNameArabic,
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
  AND UPPER(COALESCE(SourceStatus, '')) NOT LIKE '%N/A%';";

                using (var command =
                    new SQLiteCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var drug = new CatalogDrug
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            TradeName = Convert.ToString(reader["TradeName"]),
                            TradeNameArabic = Convert.ToString(reader["TradeNameArabic"]),
                            GenericName = Convert.ToString(reader["GenericName"]),
                            Strength = Convert.ToString(reader["Strength"]),
                            DosageForm = Convert.ToString(reader["DosageForm"]),
                            RouteOfAdministration = Convert.ToString(reader["RouteOfAdministration"]),
                            Manufacturer = Convert.ToString(reader["Manufacturer"]),
                            DrugClass = Convert.ToString(reader["DrugClass"])
                        };

                        if (string.IsNullOrWhiteSpace(
                            Normalize(drug.TradeName)))
                        {
                            continue;
                        }

                        catalogDrugs.Add(drug);
                    }
                }
            }

            // إزالة السجلات المتطابقة
            catalogDrugs = catalogDrugs
                .GroupBy(d => string.Join("|",
                    Normalize(d.TradeName),
                    Normalize(d.TradeNameArabic),
                    Normalize(d.GenericName),
                    Normalize(d.Strength),
                    Normalize(d.DosageForm),
                    Normalize(d.RouteOfAdministration),
                    Normalize(d.Manufacturer)))
                .Select(g => g.First())
                .ToList();

            // إنشاء فهرس سريع لأسماء الأدوية في الدليل
            var catalogNameIndex =
                new Dictionary<string, List<CatalogDrug>>(
                    StringComparer.Ordinal);

            foreach (var drug in catalogDrugs)
            {
                var aliases = GetAliases(
                    drug.TradeName,
                    drug.TradeNameArabic);

                foreach (string alias in aliases)
                {
                    AddToIndex(
                        catalogNameIndex,
                        alias,
                        drug);
                }
            }

            // إنشاء فهرس سريع لأسماء الأصناف الموجودة بالمخزون
            var inventoryNameIndex =
                new Dictionary<string, List<SalesProductSearchResult>>(
                    StringComparer.Ordinal);

            foreach (var result in results)
            {
                if (result.Product == null)
                    continue;

                string normalizedName =
                    Normalize(result.Name);

                if (normalizedName.Length == 0)
                    continue;

                AddToIndex(
                    inventoryNameIndex,
                    normalizedName,
                    result);
            }

            // مطابقة الأصناف الموجودة بالمخزون مع دليل الأدوية
            foreach (var drug in catalogDrugs)
            {
                string englishName =
                    Normalize(drug.TradeName);

                string arabicName =
                    Normalize(drug.TradeNameArabic);

                var aliases = GetAliases(
                    englishName,
                    arabicName);

                // جمع سجلات الدليل التي تشترك في الاسم
                // مع منع احتساب السجل نفسه أكثر من مرة.
                var catalogMatches =
                    new HashSet<int>();

                foreach (string alias in aliases)
                {
                    List<CatalogDrug> matches;

                    if (!catalogNameIndex.TryGetValue(
                        alias,
                        out matches))
                    {
                        continue;
                    }

                    foreach (var match in matches)
                        catalogMatches.Add(match.Id);
                }

                // البحث عن الصنف المطابق في المخزون
                var inventoryMatches =
                    new Dictionary<int, SalesProductSearchResult>();

                foreach (string alias in aliases)
                {
                    List<SalesProductSearchResult> matches;

                    if (!inventoryNameIndex.TryGetValue(
                        alias,
                        out matches))
                    {
                        continue;
                    }

                    foreach (var match in matches)
                    {
                        if (match.Product == null)
                            continue;

                        inventoryMatches[
                            match.Product.Id] = match;
                    }
                }

                // المطابقة الدقيقة والوحيدة فقط
                if (inventoryMatches.Count == 1 &&
                    catalogMatches.Count == 1)
                {
                    var existing =
                        inventoryMatches.Values.First();

                    existing.CatalogId = drug.Id;
                    existing.TradeNameArabic =
                        drug.TradeNameArabic;
                    existing.GenericName =
                        drug.GenericName;
                    existing.Strength =
                        drug.Strength;
                    existing.DosageForm =
                        drug.DosageForm;
                    existing.RouteOfAdministration =
                        drug.RouteOfAdministration;
                    existing.Manufacturer =
                        drug.Manufacturer;
                    existing.DrugClass =
                        drug.DrugClass;
                }

                // لا نضيف أدوية الدليل غير الموجودة بالمخزون
                // إلى نتائج البحث أو قائمة الأصناف المتاحة للبيع.
            }

            // ضمان أن كل نتيجة بحث مرتبطة بصنف مخزني
            // وأن رصيده أكبر من صفر.
            return results
                .Where(r =>
                    r.Product != null &&
                    r.Product.Quantity > 0)
                .OrderBy(r => r.Name)
                .ToList();
        }

        private List<string> GetAliases(
            string firstName,
            string secondName)
        {
            var aliases = new List<string>();

            string first = Normalize(firstName);
            string second = Normalize(secondName);

            if (first.Length > 0)
                aliases.Add(first);

            if (second.Length > 0 &&
                second != first)
            {
                aliases.Add(second);
            }

            return aliases;
        }

        private void AddToIndex<T>(
            Dictionary<string, List<T>> index,
            string key,
            T value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            List<T> list;

            if (!index.TryGetValue(key, out list))
            {
                list = new List<T>();
                index.Add(key, list);
            }

            list.Add(value);
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
    }
}
