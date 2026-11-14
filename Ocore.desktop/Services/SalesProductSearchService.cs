
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
        private readonly PermissionService permissionService;
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
            permissionService = new PermissionService();

            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        public List<SalesProductSearchResult> GetSearchResults(
            int currentUserId)
        {
            // جلب جميع الكروت النشطة المسموح بالبحث عنها للبيع،
            // بما فيها الكروت التي رصيدها صفر.
            var products = GetActiveProductsForSalesSearch(currentUserId);

            var results = products
                .Where(p => p != null)
                .Select(p => new SalesProductSearchResult
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
                })
                .ToList();

            // تحميل بيانات دليل الأدوية.
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

            // فهرس دليل الأدوية حسب المعرف.
            var catalogById = catalogDrugs
                .GroupBy(d => d.Id)
                .ToDictionary(g => g.Key, g => g.First());

            // فهرس أسماء الأدوية في الدليل.
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

            // فهرس أسماء جميع كروت المخزون.
            var inventoryNameIndex =
                new Dictionary<string, List<SalesProductSearchResult>>(
                    StringComparer.Ordinal);

            foreach (var result in results)
            {
                if (result.Product == null)
                    continue;

                string normalizedName = Normalize(result.Name);

                if (normalizedName.Length == 0)
                    continue;

                AddToIndex(
                    inventoryNameIndex,
                    normalizedName,
                    result);
            }

            // أولًا: الربط المباشر باستخدام DrugCatalogId.
            // يسمح ذلك بإثراء جميع كروت السعر التابعة للدواء نفسه.
            foreach (var result in results)
            {
                if (result.Product == null ||
                    !result.Product.DrugCatalogId.HasValue)
                {
                    continue;
                }

                CatalogDrug drug;

                if (catalogById.TryGetValue(
                    result.Product.DrugCatalogId.Value,
                    out drug))
                {
                    ApplyCatalogData(result, drug);
                }
            }

            // ثانيًا: محاولة المطابقة بالاسم للأصناف القديمة
            // التي لا تحتوي على DrugCatalogId.
            foreach (var drug in catalogDrugs)
            {
                var aliases = GetAliases(
                    drug.TradeName,
                    drug.TradeNameArabic);

                var catalogMatches = new HashSet<int>();

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

                // لا نعتمد على الاسم وحده إذا كان مشتركًا
                // بين أكثر من دواء في الدليل.
                if (catalogMatches.Count != 1)
                    continue;

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

                        // لا نغير بيانات كارت مرتبط بالفعل
                        // بسجل مختلف في دليل الأدوية.
                        if (match.Product.DrugCatalogId.HasValue)
                            continue;

                        inventoryMatches[match.Product.Id] = match;
                    }
                }

                // إذا كان هناك أكثر من كارت سعر للاسم نفسه،
                // نُثري جميع الكروت المطابقة بدل رفض المطابقة.
                foreach (var existing in inventoryMatches.Values)
                {
                    ApplyCatalogData(existing, drug);
                }
            }

            // لا نستبعد الكروت ذات الرصيد صفر.
            // فحص الرصيد الصالح ومنع البيع يتم عند اختيار
            // الكارت ومحاولة تخصيص تشغيلاته.
            return results
                .Where(r => r.Product != null)
                .OrderBy(r => r.Name)
                .ThenBy(r => r.ProductCode)
                .ToList();
        }

        private List<Product> GetActiveProductsForSalesSearch(
            int currentUserId)
        {
            var products = new List<Product>();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role;

                using (var command = new SQLiteCommand(
                    @"SELECT Role
                      FROM Users
                      WHERE Id = @UserId
                        AND IsActive = 1;",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@UserId", currentUserId);

                    object value = command.ExecuteScalar();

                    if (value == null)
                    {
                        throw new UnauthorizedAccessException(
                            "المستخدم غير موجود أو حسابه غير نشط.");
                    }

                    role = Convert.ToString(value);
                }

                if (!permissionService.HasPermission(
                    role,
                    "Sales.SearchProducts"))
                {
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية البحث عن الأصناف للبيع.");
                }

                string sql = @"
SELECT
    p.Id,
    p.ProductCode,
    p.Name,
    p.DrugCatalogId,
    p.CategoryId,
    p.UnitName,
    p.SmallUnitName,
    p.UnitsPerPackage,
    p.SalePrice,
    p.PurchasePrice,
    p.MaxDiscountPercentage,
    p.MinimumStockLevel,
    p.IsArchived,

    COALESCE(
        (SELECT SUM(b.Quantity)
         FROM Batches b
         WHERE b.ProductId = p.Id
           AND b.Quantity > 0),
        0
    ) AS TotalQuantity,

    COALESCE(
        (SELECT SUM(b.Quantity)
         FROM Batches b
         WHERE b.ProductId = p.Id
           AND (
                b.ExpiryDate = ''
                OR b.ExpiryDate >= @Today
           )
           AND b.Quantity > 0),
        0
    ) AS Quantity,

    COALESCE(
        (SELECT SUM(b.Quantity)
         FROM Batches b
         WHERE b.ProductId = p.Id
           AND b.ExpiryDate <> ''
           AND b.ExpiryDate < @Today
           AND b.Quantity > 0),
        0
    ) AS ExpiredQuantity,

    COALESCE(
        (SELECT Barcode
         FROM ProductBarcodes pb
         WHERE pb.ProductId = p.Id
         LIMIT 1),
        ''
    ) AS Barcode

FROM Products p
WHERE p.IsArchived = 0
ORDER BY p.Name, p.ProductCode;";

                using (var command =
                    new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@Today",
                        DateTime.Today.ToString("yyyy-MM-dd"));

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            products.Add(new Product
                            {
                                Id = Convert.ToInt32(reader["Id"]),

                                ProductCode =
                                    Convert.ToString(reader["ProductCode"]),

                                Name =
                                    Convert.ToString(reader["Name"]),

                                DrugCatalogId =
                                    reader["DrugCatalogId"] == DBNull.Value
                                        ? (int?)null
                                        : Convert.ToInt32(reader["DrugCatalogId"]),

                                CategoryId =
                                    reader["CategoryId"] == DBNull.Value
                                        ? (int?)null
                                        : Convert.ToInt32(reader["CategoryId"]),

                                UnitName =
                                    Convert.ToString(reader["UnitName"]),

                                SmallUnitName =
                                    Convert.ToString(reader["SmallUnitName"]),

                                UnitsPerPackage =
                                    Convert.ToInt32(reader["UnitsPerPackage"]),

                                SalePrice =
                                    Convert.ToDouble(reader["SalePrice"]),

                                PurchasePrice =
                                    Convert.ToDouble(reader["PurchasePrice"]),

                                MaxDiscountPercentage =
                                    Convert.ToDouble(reader["MaxDiscountPercentage"]),

                                MinimumStockLevel =
                                    Convert.ToDouble(reader["MinimumStockLevel"]),

                                Quantity =
                                    Convert.ToDouble(reader["Quantity"]),

                                TotalQuantity =
                                    Convert.ToDouble(reader["TotalQuantity"]),

                                ExpiredQuantity =
                                    Convert.ToDouble(reader["ExpiredQuantity"]),

                                IsArchived =
                                    Convert.ToInt32(reader["IsArchived"]) == 1,

                                Barcode =
                                    Convert.ToString(reader["Barcode"])
                            });
                        }
                    }
                }
            }

            return products;
        }

        private void ApplyCatalogData(
            SalesProductSearchResult result,
            CatalogDrug drug)
        {
            result.CatalogId = drug.Id;
            result.TradeNameArabic = drug.TradeNameArabic;
            result.GenericName = drug.GenericName;
            result.Strength = drug.Strength;
            result.DosageForm = drug.DosageForm;
            result.RouteOfAdministration = drug.RouteOfAdministration;
            result.Manufacturer = drug.Manufacturer;
            result.DrugClass = drug.DrugClass;
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
