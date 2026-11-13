
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class ProductService
    {
        private readonly string connectionString;
        private readonly PermissionService permissionService;

        public ProductService()
        {
            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";

            permissionService = new PermissionService();
        }

        #region Automatic Product Code And Barcode

        private void EnsureProductCodeSequence(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            using (var command = new SQLiteCommand(@"
                CREATE TABLE IF NOT EXISTS ProductCodeSequence
                (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    LastValue INTEGER NOT NULL
                );",
                connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            long maximumExistingCode;

            using (var command = new SQLiteCommand(@"
                SELECT COALESCE(MAX(CAST(ProductCode AS INTEGER)), 0)
                FROM Products
                WHERE ProductCode <> ''
                  AND ProductCode NOT GLOB '*[^0-9]*';",
                connection, transaction))
            {
                maximumExistingCode =
                    Convert.ToInt64(command.ExecuteScalar());
            }

            using (var command = new SQLiteCommand(@"
                INSERT OR IGNORE INTO ProductCodeSequence (Id, LastValue)
                VALUES (1, @InitialValue);

                UPDATE ProductCodeSequence
                SET LastValue = MAX(LastValue, @InitialValue)
                WHERE Id = 1;",
                connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@InitialValue", maximumExistingCode);

                command.ExecuteNonQuery();
            }
        }

        private void GenerateProductIdentifiers(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            out string productCode,
            out string barcode)
        {
            EnsureProductCodeSequence(connection, transaction);

            while (true)
            {
                using (var command = new SQLiteCommand(@"
                    UPDATE ProductCodeSequence
                    SET LastValue = LastValue + 1
                    WHERE Id = 1;",
                    connection, transaction))
                {
                    command.ExecuteNonQuery();
                }

                long serial;

                using (var command = new SQLiteCommand(
                    "SELECT LastValue FROM ProductCodeSequence WHERE Id = 1;",
                    connection, transaction))
                {
                    serial = Convert.ToInt64(command.ExecuteScalar());
                }

                if (serial > 9999999999L)
                {
                    throw new InvalidOperationException(
                        "وصل تسلسل الباركود إلى الحد الأقصى المسموح به.");
                }

                string candidateCode = serial.ToString("D6");

                string barcodeBase = "20" + serial.ToString("D10");

                string candidateBarcode =
                    barcodeBase + CalculateEan13CheckDigit(barcodeBase);

                bool codeExists;
                bool barcodeExists;

                using (var command = new SQLiteCommand(@"
                    SELECT COUNT(*)
                    FROM Products
                    WHERE ProductCode = @Code;",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue(
                        "@Code", candidateCode);

                    codeExists =
                        Convert.ToInt32(command.ExecuteScalar()) > 0;
                }

                using (var command = new SQLiteCommand(@"
                    SELECT COUNT(*)
                    FROM ProductBarcodes
                    WHERE Barcode = @Barcode;",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue(
                        "@Barcode", candidateBarcode);

                    barcodeExists =
                        Convert.ToInt32(command.ExecuteScalar()) > 0;
                }

                if (!codeExists && !barcodeExists)
                {
                    productCode = candidateCode;
                    barcode = candidateBarcode;
                    return;
                }
            }
        }

        private int CalculateEan13CheckDigit(string barcodeBase)
        {
            if (barcodeBase == null || barcodeBase.Length != 12)
            {
                throw new ArgumentException(
                    "قاعدة باركود EAN-13 يجب أن تتكون من 12 رقمًا.");
            }

            int sum = 0;

            for (int i = 0; i < barcodeBase.Length; i++)
            {
                int digit = barcodeBase[i] - '0';

                sum += digit * (i % 2 == 0 ? 1 : 3);
            }

            return (10 - (sum % 10)) % 10;
        }

        #endregion

        #region Permissions

        private string GetActiveUserRole(
            SQLiteConnection connection,
            int userId)
        {
            using (var command = new SQLiteCommand(
                @"SELECT Role FROM Users
                  WHERE Id = @UserId AND IsActive = 1;",
                connection))
            {
                command.Parameters.AddWithValue("@UserId", userId);

                object result = command.ExecuteScalar();

                if (result == null)
                {
                    throw new UnauthorizedAccessException(
                        "المستخدم غير موجود أو حسابه غير نشط.");
                }

                return result.ToString();
            }
        }

        private void CheckProductCreatePermission(
            SQLiteConnection connection,
            int currentUserId)
        {
            string role = GetActiveUserRole(
                connection, currentUserId);

            if (!permissionService.HasPermission(
                role, "Products.Create"))
            {
                throw new UnauthorizedAccessException(
                    "ليس لديك صلاحية إدارة التصنيفات.");
            }
        }

        private void CheckManagerPermission(
            SQLiteConnection connection,
            int currentUserId)
        {
            string role = GetActiveUserRole(
                connection, currentUserId);

            if (!string.Equals(
                role,
                "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "هذه العملية متاحة للمدير فقط.");
            }
        }

        public bool IsManager(int currentUserId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(connection, currentUserId);

                return string.Equals(
                    role,
                    "Manager",
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        #endregion

        #region Drug Catalog

        private void ValidateDrugCatalogLink(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int drugCatalogId)
        {
            using (var command = new SQLiteCommand(
                @"SELECT COUNT(*)
                  FROM DrugCatalog
                  WHERE Id = @DrugCatalogId
                    AND IsActive = 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@DrugCatalogId", drugCatalogId);

                if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                {
                    throw new InvalidOperationException(
                        "الدواء المحدد غير موجود أو غير نشط في دليل الأدوية.");
                }
            }

            using (var command = new SQLiteCommand(
                @"SELECT COUNT(*)
                  FROM Products
                  WHERE DrugCatalogId = @DrugCatalogId;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@DrugCatalogId", drugCatalogId);

                if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                {
                    throw new InvalidOperationException(
                        "هذا الدواء مرتبط بصنف آخر بالفعل.");
                }
            }
        }

        #endregion

        #region Get Products

        public List<Product> GetProductsForManagement(int currentUserId)
        {
            var products = new List<Product>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(connection, currentUserId);

                if (!permissionService.HasPermission(role, "Products.View"))
                {
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية عرض الأصناف.");
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
         LIMIT 1), ''
    ) AS Barcode

FROM Products p
ORDER BY p.Name;";

                using (var command = new SQLiteCommand(sql, connection))
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
                                ProductCode = reader["ProductCode"].ToString(),
                                Name = reader["Name"].ToString(),

                                DrugCatalogId =
                                    reader["DrugCatalogId"] == DBNull.Value
                                        ? (int?)null
                                        : Convert.ToInt32(reader["DrugCatalogId"]),

                                CategoryId = reader["CategoryId"] == DBNull.Value
                                    ? (int?)null
                                    : Convert.ToInt32(reader["CategoryId"]),

                                UnitName = reader["UnitName"].ToString(),
                                SmallUnitName = reader["SmallUnitName"].ToString(),

                                UnitsPerPackage = Convert.ToInt32(
                                    reader["UnitsPerPackage"]),

                                SalePrice = Convert.ToDouble(reader["SalePrice"]),
                                PurchasePrice = Convert.ToDouble(reader["PurchasePrice"]),

                                MaxDiscountPercentage = Convert.ToDouble(
                                    reader["MaxDiscountPercentage"]),
                                MinimumStockLevel = Convert.ToDouble(
    reader["MinimumStockLevel"]),

                                Quantity = Convert.ToDouble(reader["Quantity"]),
                                TotalQuantity = Convert.ToDouble(reader["TotalQuantity"]),
                                ExpiredQuantity = Convert.ToDouble(reader["ExpiredQuantity"]),

                                IsArchived =
                                    Convert.ToInt32(reader["IsArchived"]) == 1,

                                Barcode = reader["Barcode"].ToString()
                            });
                        }
                    }
                }
            }

            return products;
        }

        public List<Product> GetActiveProducts(int currentUserId)
        {
            var products = new List<Product>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(
                    connection, currentUserId);

                if (!permissionService.HasPermission(
                    role, "Products.View"))
                {
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية عرض الأصناف.");
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
         LIMIT 1), ''
    ) AS Barcode

FROM Products p
WHERE p.IsArchived = 0
ORDER BY p.Name;";

                using (var command = new SQLiteCommand(sql, connection))
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
                                ProductCode = reader["ProductCode"].ToString(),
                                Name = reader["Name"].ToString(),

                                DrugCatalogId =
                                    reader["DrugCatalogId"] == DBNull.Value
                                        ? (int?)null
                                        : Convert.ToInt32(reader["DrugCatalogId"]),

                                CategoryId = reader["CategoryId"] == DBNull.Value
                                    ? (int?)null
                                    : Convert.ToInt32(reader["CategoryId"]),

                                UnitName = reader["UnitName"].ToString(),
                                SmallUnitName = reader["SmallUnitName"].ToString(),

                                UnitsPerPackage = Convert.ToInt32(
                                    reader["UnitsPerPackage"]),

                                SalePrice = Convert.ToDouble(reader["SalePrice"]),
                                PurchasePrice = Convert.ToDouble(reader["PurchasePrice"]),

                                MaxDiscountPercentage = Convert.ToDouble(
                                    reader["MaxDiscountPercentage"]),

                                Quantity = Convert.ToDouble(reader["Quantity"]),
                                TotalQuantity = Convert.ToDouble(reader["TotalQuantity"]),
                                ExpiredQuantity = Convert.ToDouble(reader["ExpiredQuantity"]),

                                IsArchived =
                                    Convert.ToInt32(reader["IsArchived"]) == 1,

                                Barcode = reader["Barcode"].ToString()
                            });
                        }
                    }
                }
            }

            return products;
        }

        public List<Product> GetProductsAvailableForSale(int currentUserId)
        {
            var products = new List<Product>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(
                    connection, currentUserId);

                if (!permissionService.HasPermission(
                    role, "Sales.SearchProducts"))
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
         LIMIT 1), ''
    ) AS Barcode

FROM Products p

WHERE p.IsArchived = 0

  AND EXISTS (
      SELECT 1
      FROM Batches b
      WHERE b.ProductId = p.Id
        AND b.Quantity > 0
        AND (
            b.ExpiryDate = ''
            OR b.ExpiryDate >= @Today
        )
  )

ORDER BY p.Name;";

                using (var command = new SQLiteCommand(sql, connection))
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
                                    reader["ProductCode"].ToString(),

                                Name =
                                    reader["Name"].ToString(),

                                DrugCatalogId =
                                    reader["DrugCatalogId"] == DBNull.Value
                                        ? (int?)null
                                        : Convert.ToInt32(reader["DrugCatalogId"]),

                                CategoryId =
                                    reader["CategoryId"] == DBNull.Value
                                        ? (int?)null
                                        : Convert.ToInt32(reader["CategoryId"]),

                                UnitName =
                                    reader["UnitName"].ToString(),

                                SmallUnitName =
                                    reader["SmallUnitName"].ToString(),

                                UnitsPerPackage =
                                    Convert.ToInt32(reader["UnitsPerPackage"]),

                                SalePrice =
                                    Convert.ToDouble(reader["SalePrice"]),

                                PurchasePrice =
                                    Convert.ToDouble(reader["PurchasePrice"]),

                                MaxDiscountPercentage =
                                    Convert.ToDouble(reader["MaxDiscountPercentage"]),

                                Quantity =
                                    Convert.ToDouble(reader["Quantity"]),

                                TotalQuantity =
                                    Convert.ToDouble(reader["TotalQuantity"]),

                                ExpiredQuantity =
                                    Convert.ToDouble(reader["ExpiredQuantity"]),

                                IsArchived =
                                    Convert.ToInt32(reader["IsArchived"]) == 1,

                                Barcode =
                                    reader["Barcode"].ToString()
                            });
                        }
                    }
                }
            }

            return products;
        }

        // جلب التشغيلات المتاحة للبيع لصنف محدد.
        // لا يتم اختيار أي تشغيلة تلقائيًا ولا يتم خصم أي كمية هنا.
        public List<ProductBatch> GetAvailableBatches(
            int currentUserId,
            int productId)
        {
            if (productId <= 0)
            {
                throw new ArgumentException(
                    "الصنف المحدد غير صحيح.");
            }

            var batches = new List<ProductBatch>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(
                    connection, currentUserId);

                if (!permissionService.HasPermission(
                    role, "Sales.SearchProducts"))
                {
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية البحث عن الأصناف للبيع.");
                }

                string sql = @"
SELECT
    b.Id,
    b.BatchNumber,
    b.ExpiryDate,
    b.Quantity
FROM Batches b
INNER JOIN Products p ON p.Id = b.ProductId
WHERE b.ProductId = @ProductId
  AND p.IsArchived = 0
  AND b.Quantity > 0
  AND (
        b.ExpiryDate = ''
        OR b.ExpiryDate >= @Today
      )
ORDER BY
    CASE
        WHEN b.ExpiryDate = '' THEN 1
        ELSE 0
    END,
    b.ExpiryDate,
    b.Id;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@ProductId", productId);

                    command.Parameters.AddWithValue(
                        "@Today",
                        DateTime.Today.ToString("yyyy-MM-dd"));

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            batches.Add(new ProductBatch
                            {
                                Id = Convert.ToInt32(reader["Id"]),

                                BatchNumber =
                                    reader["BatchNumber"] == DBNull.Value
                                        ? string.Empty
                                        : reader["BatchNumber"].ToString(),

                                ExpiryDate =
                                    reader["ExpiryDate"] == DBNull.Value
                                        ? string.Empty
                                        : reader["ExpiryDate"].ToString(),

                                Quantity =
                                    Convert.ToDouble(reader["Quantity"])
                            });
                        }
                    }
                }
            }

            return batches;
        }

        #endregion

        #region Categories

        public List<KeyValuePair<int, string>> GetActiveCategories(
            int currentUserId)
        {
            var categories = new List<KeyValuePair<int, string>>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                CheckProductCreatePermission(connection, currentUserId);

                string sql = @"
SELECT Id, Name
FROM Categories
WHERE IsActive = 1
ORDER BY Name;";

                using (var command = new SQLiteCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        categories.Add(
                            new KeyValuePair<int, string>(
                                Convert.ToInt32(reader["Id"]),
                                reader["Name"].ToString()));
                    }
                }
            }

            return categories;
        }

        public List<KeyValuePair<int, string>> GetCategoriesForManagement(
            int currentUserId)
        {
            var categories = new List<KeyValuePair<int, string>>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                CheckProductCreatePermission(connection, currentUserId);

                string sql = @"
SELECT Id, Name
FROM Categories
WHERE IsActive = 1
ORDER BY Name;";

                using (var command = new SQLiteCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        categories.Add(
                            new KeyValuePair<int, string>(
                                Convert.ToInt32(reader["Id"]),
                                reader["Name"].ToString()));
                    }
                }
            }

            return categories;
        }

        public int AddCategory(int currentUserId, string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                throw new ArgumentException("اكتب اسم التصنيف.");

            categoryName = categoryName.Trim();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                CheckProductCreatePermission(connection, currentUserId);

                using (var command = new SQLiteCommand(
                    @"SELECT COUNT(*)
                      FROM Categories
                      WHERE Name = @Name COLLATE NOCASE;",
                    connection))
                {
                    command.Parameters.AddWithValue("@Name", categoryName);

                    if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                    {
                        throw new InvalidOperationException(
                            "هذا التصنيف موجود بالفعل.");
                    }
                }

                using (var command = new SQLiteCommand(
                    @"INSERT INTO Categories (Name, IsActive)
                      VALUES (@Name, 1);",
                    connection))
                {
                    command.Parameters.AddWithValue("@Name", categoryName);
                    command.ExecuteNonQuery();
                }

                using (var command = new SQLiteCommand(
                    "SELECT last_insert_rowid();",
                    connection))
                {
                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        public void UpdateCategory(
            int currentUserId,
            int categoryId,
            string newName)
        {
            if (categoryId <= 0)
                throw new ArgumentException("التصنيف المحدد غير صحيح.");

            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("اكتب اسم التصنيف الجديد.");

            newName = newName.Trim();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                CheckProductCreatePermission(connection, currentUserId);

                using (var command = new SQLiteCommand(
                    @"SELECT COUNT(*)
                      FROM Categories
                      WHERE Name = @Name COLLATE NOCASE
                        AND Id <> @Id;",
                    connection))
                {
                    command.Parameters.AddWithValue("@Name", newName);
                    command.Parameters.AddWithValue("@Id", categoryId);

                    if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                    {
                        throw new InvalidOperationException(
                            "يوجد تصنيف آخر بنفس الاسم.");
                    }
                }

                using (var command = new SQLiteCommand(
                    @"UPDATE Categories
                      SET Name = @Name
                      WHERE Id = @Id AND IsActive = 1;",
                    connection))
                {
                    command.Parameters.AddWithValue("@Name", newName);
                    command.Parameters.AddWithValue("@Id", categoryId);

                    if (command.ExecuteNonQuery() == 0)
                    {
                        throw new InvalidOperationException(
                            "التصنيف غير موجود أو متوقف بالفعل.");
                    }
                }
            }
        }

        public void DeactivateCategory(
            int currentUserId,
            int categoryId)
        {
            if (categoryId <= 0)
                throw new ArgumentException("التصنيف المحدد غير صحيح.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                CheckProductCreatePermission(connection, currentUserId);

                using (var command = new SQLiteCommand(
                    @"UPDATE Categories
                      SET IsActive = 0
                      WHERE Id = @Id AND IsActive = 1;",
                    connection))
                {
                    command.Parameters.AddWithValue("@Id", categoryId);

                    if (command.ExecuteNonQuery() == 0)
                    {
                        throw new InvalidOperationException(
                            "التصنيف غير موجود أو متوقف بالفعل.");
                    }
                }
            }
        }

        #endregion

        #region Create Product

        public int CreateProduct(
            int currentUserId,
            Product product,
            double initialQuantity,
            string batchNumber,
            DateTime? expiryDate)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            if (string.IsNullOrWhiteSpace(product.Name))
                throw new ArgumentException("اسم الصنف مطلوب.");

            if (product.SalePrice < 0 || product.PurchasePrice < 0)
                throw new ArgumentException("الأسعار لا يمكن أن تكون سالبة.");

            if (initialQuantity < 0)
                throw new ArgumentException("الكمية لا يمكن أن تكون سالبة.");

            if (product.UnitsPerPackage < 1)
                throw new ArgumentException(
                    "عدد الوحدات داخل العبوة يجب أن يكون واحدًا على الأقل.");

            if (string.IsNullOrWhiteSpace(product.SmallUnitName))
                throw new ArgumentException("اسم الوحدة الصغيرة مطلوب.");

            if (initialQuantity > 0 && !expiryDate.HasValue)
                throw new ArgumentException(
                    "يجب تحديد تاريخ الصلاحية عند إدخال رصيد افتتاحي.");

            int productId;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string role = GetActiveUserRole(
                            connection, currentUserId);

                        if (!permissionService.HasPermission(
                            role, "Products.Create"))
                        {
                            throw new UnauthorizedAccessException(
                                "ليس لديك صلاحية إضافة الأصناف.");
                        }

                        if (product.DrugCatalogId.HasValue)
                        {
                            ValidateDrugCatalogLink(
                                connection,
                                transaction,
                                product.DrugCatalogId.Value);
                        }

                        string generatedCode;
                        string generatedBarcode;

                        GenerateProductIdentifiers(
                            connection,
                            transaction,
                            out generatedCode,
                            out generatedBarcode);

                        using (var command = new SQLiteCommand(
                            @"INSERT INTO Products
                              (ProductCode, Name, DrugCatalogId,
                               CategoryId, UnitName,
                               SmallUnitName, UnitsPerPackage,
                               SalePrice, PurchasePrice, IsArchived, CreatedAt)
                              VALUES
                              (@Code, @Name, @DrugCatalogId,
                               @CategoryId, @Unit,
                               @SmallUnitName, @UnitsPerPackage,
                               @SalePrice, @PurchasePrice, 0, @CreatedAt);",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@Code", generatedCode);

                            command.Parameters.AddWithValue(
                                "@Name", product.Name.Trim());

                            command.Parameters.AddWithValue(
                                "@DrugCatalogId",
                                (object)product.DrugCatalogId ?? DBNull.Value);

                            command.Parameters.AddWithValue(
                                "@CategoryId",
                                (object)product.CategoryId ?? DBNull.Value);

                            command.Parameters.AddWithValue(
                                "@Unit",
                                string.IsNullOrWhiteSpace(product.UnitName)
                                    ? "علبة"
                                    : product.UnitName.Trim());

                            command.Parameters.AddWithValue(
                                "@SmallUnitName",
                                product.SmallUnitName.Trim());

                            command.Parameters.AddWithValue(
                                "@UnitsPerPackage",
                                product.UnitsPerPackage);

                            command.Parameters.AddWithValue(
                                "@SalePrice", product.SalePrice);

                            command.Parameters.AddWithValue(
                                "@PurchasePrice", product.PurchasePrice);

                            command.Parameters.AddWithValue(
                                "@CreatedAt",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                            command.ExecuteNonQuery();
                        }

                        using (var command = new SQLiteCommand(
                            "SELECT last_insert_rowid();",
                            connection, transaction))
                        {
                            productId = Convert.ToInt32(
                                command.ExecuteScalar());
                        }

                        using (var command = new SQLiteCommand(
                            @"INSERT INTO ProductBarcodes
                              (ProductId, Barcode)
                              VALUES (@ProductId, @Barcode);",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ProductId", productId);

                            command.Parameters.AddWithValue(
                                "@Barcode", generatedBarcode);

                            command.ExecuteNonQuery();
                        }

                        product.ProductCode = generatedCode;
                        product.Barcode = generatedBarcode;
                        product.MaxDiscountPercentage = 0;

                        if (initialQuantity > 0)
                        {
                            using (var command = new SQLiteCommand(
                                @"INSERT INTO Batches
                                  (ProductId, BatchNumber, ExpiryDate,
                                   PurchasePrice, Quantity)
                                  VALUES
                                  (@ProductId, @BatchNumber, @ExpiryDate,
                                   @PurchasePrice, @Quantity);",
                                connection, transaction))
                            {
                                command.Parameters.AddWithValue(
                                    "@ProductId", productId);

                                command.Parameters.AddWithValue(
                                    "@BatchNumber",
                                    string.IsNullOrWhiteSpace(batchNumber)
                                        ? (object)DBNull.Value
                                        : batchNumber.Trim());

                                command.Parameters.AddWithValue(
                                    "@ExpiryDate",
                                    expiryDate.Value.ToString("yyyy-MM-dd"));

                                command.Parameters.AddWithValue(
                                    "@PurchasePrice", product.PurchasePrice);

                                command.Parameters.AddWithValue(
                                    "@Quantity", initialQuantity);

                                command.ExecuteNonQuery();
                            }
                        }

                        WriteProductAudit(
                            connection,
                            transaction,
                            currentUserId,
                            productId,
                            "Create",
                            "إضافة الصنف: " + product.Name.Trim()
                            + " - كود الصنف: " + generatedCode
                            + " - الباركود: " + generatedBarcode
                            + " - العبوة: " + product.UnitName
                            + " - الوحدة الصغيرة: " + product.SmallUnitName
                            + " - عدد الوحدات داخل العبوة: "
                            + product.UnitsPerPackage
                            + " - الحد الأقصى للخصم: 0%"
                            + (product.DrugCatalogId.HasValue
                                ? " - مرتبط بدليل الأدوية: "
                                  + product.DrugCatalogId.Value
                                : " - صنف يدوي غير مرتبط بدليل الأدوية"));

                        transaction.Commit();
                    }
                    catch (SQLiteException ex)
                    {
                        transaction.Rollback();

                        if (ex.Message.IndexOf(
                            "UNIQUE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            throw new InvalidOperationException(
                                "حدث تعارض في كود الصنف أو الباركود. حاول الحفظ مرة أخرى.");
                        }

                        throw;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }

            return productId;
        }

        #endregion

        #region Update Product

        public void UpdateProduct(
            int currentUserId,
            Product product)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            if (product.Id <= 0)
                throw new ArgumentException("الصنف المحدد غير صحيح.");

            if (string.IsNullOrWhiteSpace(product.Name))
                throw new ArgumentException("اسم الصنف مطلوب.");

            if (string.IsNullOrWhiteSpace(product.SmallUnitName))
                throw new ArgumentException("اسم الوحدة الصغيرة مطلوب.");

            if (product.UnitsPerPackage < 1)
                throw new ArgumentException(
                    "عدد الوحدات داخل العبوة غير صحيح.");

            if (product.SalePrice < 0 || product.PurchasePrice < 0)
                throw new ArgumentException("الأسعار لا يمكن أن تكون سالبة.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        CheckManagerPermission(connection, currentUserId);

                        Product oldProduct = null;

                        using (var command = new SQLiteCommand(
                            @"SELECT ProductCode, Name, CategoryId,
                                     UnitName, SmallUnitName, UnitsPerPackage,
                                     SalePrice, PurchasePrice
                              FROM Products
                              WHERE Id = @Id;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", product.Id);

                            using (var reader = command.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    oldProduct = new Product
                                    {
                                        Id = product.Id,
                                        ProductCode = reader["ProductCode"].ToString(),
                                        Name = reader["Name"].ToString(),
                                        CategoryId = reader["CategoryId"] == DBNull.Value
                                            ? (int?)null
                                            : Convert.ToInt32(reader["CategoryId"]),
                                        UnitName = reader["UnitName"].ToString(),
                                        SmallUnitName = reader["SmallUnitName"].ToString(),
                                        UnitsPerPackage = Convert.ToInt32(
                                            reader["UnitsPerPackage"]),
                                        SalePrice = Convert.ToDouble(reader["SalePrice"]),
                                        PurchasePrice = Convert.ToDouble(
                                            reader["PurchasePrice"])
                                    };
                                }
                            }
                        }

                        if (oldProduct == null)
                            throw new InvalidOperationException("الصنف غير موجود.");

                        bool unitConversionChanged =
                            oldProduct.UnitsPerPackage != product.UnitsPerPackage ||
                            !string.Equals(
                                oldProduct.UnitName,
                                product.UnitName,
                                StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(
                                oldProduct.SmallUnitName,
                                product.SmallUnitName,
                                StringComparison.OrdinalIgnoreCase);

                        if (unitConversionChanged)
                        {
                            using (var command = new SQLiteCommand(
                                @"SELECT
                                    (SELECT COUNT(*) FROM Batches
                                     WHERE ProductId = @Id) +
                                    (SELECT COUNT(*) FROM SalesInvoiceItems
                                     WHERE ProductId = @Id) +
                                    (SELECT COUNT(*) FROM PurchaseInvoiceItems
                                     WHERE ProductId = @Id) +
                                    (SELECT COUNT(*) FROM SalesReturnItems
                                     WHERE ProductId = @Id);",
                                connection, transaction))
                            {
                                command.Parameters.AddWithValue("@Id", product.Id);

                                if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                                {
                                    throw new InvalidOperationException(
                                        "لا يمكن تغيير أسماء الوحدات أو عددها بعد وجود " +
                                        "تشغيلات أو فواتير أو مرتجعات للصنف، حفاظًا على " +
                                        "صحة أرصدة المخزون والعمليات السابقة.");
                                }
                            }
                        }

                        using (var command = new SQLiteCommand(
                            @"UPDATE Products
                              SET Name = @Name,
                                  CategoryId = @CategoryId,
                                  UnitName = @UnitName,
                                  SmallUnitName = @SmallUnitName,
                                  UnitsPerPackage = @UnitsPerPackage,
                                  SalePrice = @SalePrice,
                                  PurchasePrice = @PurchasePrice
                              WHERE Id = @Id;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@Name", product.Name.Trim());

                            command.Parameters.AddWithValue(
                                "@CategoryId",
                                (object)product.CategoryId ?? DBNull.Value);

                            command.Parameters.AddWithValue(
                                "@UnitName",
                                string.IsNullOrWhiteSpace(product.UnitName)
                                    ? "علبة"
                                    : product.UnitName.Trim());

                            command.Parameters.AddWithValue(
                                "@SmallUnitName", product.SmallUnitName.Trim());

                            command.Parameters.AddWithValue(
                                "@UnitsPerPackage", product.UnitsPerPackage);

                            command.Parameters.AddWithValue(
                                "@SalePrice", product.SalePrice);

                            command.Parameters.AddWithValue(
                                "@PurchasePrice", product.PurchasePrice);

                            command.Parameters.AddWithValue("@Id", product.Id);

                            command.ExecuteNonQuery();
                        }

                        WriteProductAudit(
                            connection,
                            transaction,
                            currentUserId,
                            product.Id,
                            "Update",
                            "تعديل بيانات الصنف: " + oldProduct.Name +
                            "، الاسم الجديد: " + product.Name +
                            "، كود الصنف المحفوظ: " + oldProduct.ProductCode);

                        transaction.Commit();

                        product.ProductCode = oldProduct.ProductCode;
                    }
                    catch (SQLiteException ex)
                    {
                        transaction.Rollback();

                        if (ex.Message.IndexOf(
                            "UNIQUE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            throw new InvalidOperationException(
                                "حدث تعارض أثناء تحديث بيانات الصنف.");
                        }

                        throw;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        #endregion

        #region Product Discount Limit

        // تغيير الحد الأقصى للخصم عملية مستقلة ومخصصة للمدير فقط.
        // لا يتم تغيير هذا الحد من خلال UpdateProduct العادي.
        public void SetProductMaxDiscountPercentage(
            int currentUserId,
            int productId,
            double maxDiscountPercentage)
        {
            if (productId <= 0)
            {
                throw new ArgumentException(
                    "الصنف المحدد غير صحيح.");
            }

            if (double.IsNaN(maxDiscountPercentage) ||
                double.IsInfinity(maxDiscountPercentage) ||
                maxDiscountPercentage < 0 ||
                maxDiscountPercentage > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDiscountPercentage),
                    "الحد الأقصى للخصم يجب أن يكون من 0 إلى 100%.");
            }

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // تحقق مباشر من دور المستخدم النشط.
                        // لا نعتمد على صلاحية يمكن منحها لمستخدم آخر.
                        CheckManagerPermission(connection, currentUserId);

                        string productName;
                        double oldMaxDiscountPercentage;

                        using (var command = new SQLiteCommand(
                            @"SELECT Name, MaxDiscountPercentage
                              FROM Products
                              WHERE Id = @ProductId;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ProductId", productId);

                            using (var reader = command.ExecuteReader())
                            {
                                if (!reader.Read())
                                {
                                    throw new InvalidOperationException(
                                        "الصنف غير موجود.");
                                }

                                productName = Convert.ToString(
                                    reader["Name"]);

                                oldMaxDiscountPercentage = Convert.ToDouble(
                                    reader["MaxDiscountPercentage"]);
                            }
                        }

                        // لا نسجل عملية تعديل إذا لم تتغير القيمة.
                        if (Math.Abs(
                            oldMaxDiscountPercentage -
                            maxDiscountPercentage) < 0.00000001)
                        {
                            transaction.Commit();
                            return;
                        }

                        using (var command = new SQLiteCommand(
                            @"UPDATE Products
                              SET MaxDiscountPercentage = @MaxDiscountPercentage
                              WHERE Id = @ProductId;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@MaxDiscountPercentage",
                                maxDiscountPercentage);

                            command.Parameters.AddWithValue(
                                "@ProductId", productId);

                            if (command.ExecuteNonQuery() != 1)
                            {
                                throw new InvalidOperationException(
                                    "تعذر تحديث حد الخصم للصنف.");
                            }
                        }

                        string oldValue = oldMaxDiscountPercentage.ToString(
                            "0.##",
                            System.Globalization.CultureInfo.InvariantCulture);

                        string newValue = maxDiscountPercentage.ToString(
                            "0.##",
                            System.Globalization.CultureInfo.InvariantCulture);

                        WriteProductAudit(
                            connection,
                            transaction,
                            currentUserId,
                            productId,
                            "UpdateDiscountLimit",
                            "تعديل الحد الأقصى للخصم للصنف: " +
                            productName +
                            " - القيمة القديمة: " + oldValue + "%" +
                            " - القيمة الجديدة: " + newValue + "%");

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        #endregion

        #region Product Minimum Stock Level

        public void SetProductMinimumStockLevel(
            int currentUserId,
            int productId,
            double minimumStockLevel)
        {
            if (productId <= 0)
            {
                throw new ArgumentException(
                    "الصنف المحدد غير صحيح.");
            }

            if (double.IsNaN(minimumStockLevel) ||
                double.IsInfinity(minimumStockLevel) ||
                minimumStockLevel < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumStockLevel),
                    "الحد الأدنى للمخزون لا يمكن أن يكون سالبًا.");
            }

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        CheckManagerPermission(connection, currentUserId);

                        string productName;
                        double oldMinimumStockLevel;

                        using (var command = new SQLiteCommand(
                            @"SELECT Name, MinimumStockLevel
                      FROM Products
                      WHERE Id = @ProductId;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ProductId", productId);

                            using (var reader = command.ExecuteReader())
                            {
                                if (!reader.Read())
                                {
                                    throw new InvalidOperationException(
                                        "الصنف غير موجود.");
                                }

                                productName = Convert.ToString(
                                    reader["Name"]);

                                oldMinimumStockLevel = Convert.ToDouble(
                                    reader["MinimumStockLevel"]);
                            }
                        }

                        if (Math.Abs(
                            oldMinimumStockLevel -
                            minimumStockLevel) < 0.00000001)
                        {
                            transaction.Commit();
                            return;
                        }

                        using (var command = new SQLiteCommand(
                            @"UPDATE Products
                      SET MinimumStockLevel = @MinimumStockLevel
                      WHERE Id = @ProductId;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@MinimumStockLevel",
                                minimumStockLevel);

                            command.Parameters.AddWithValue(
                                "@ProductId", productId);

                            if (command.ExecuteNonQuery() != 1)
                            {
                                throw new InvalidOperationException(
                                    "تعذر تحديث الحد الأدنى للمخزون.");
                            }
                        }

                        string oldValue = oldMinimumStockLevel.ToString(
                            "0.##",
                            System.Globalization.CultureInfo.InvariantCulture);

                        string newValue = minimumStockLevel.ToString(
                            "0.##",
                            System.Globalization.CultureInfo.InvariantCulture);

                        WriteProductAudit(
                            connection,
                            transaction,
                            currentUserId,
                            productId,
                            "UpdateMinimumStockLevel",
                            "تعديل الحد الأدنى للمخزون للصنف: " +
                            productName +
                            " - القيمة القديمة: " + oldValue +
                            " - القيمة الجديدة: " + newValue);

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        #endregion
        #region Archive Product

        public void ArchiveProduct(int currentUserId, int productId)
        {
            if (productId <= 0)
                throw new ArgumentException("الصنف المحدد غير صحيح.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        CheckManagerPermission(connection, currentUserId);

                        string productName;

                        using (var command = new SQLiteCommand(
                            "SELECT Name FROM Products WHERE Id = @Id;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", productId);

                            productName = Convert.ToString(
                                command.ExecuteScalar());

                            if (string.IsNullOrEmpty(productName))
                                throw new InvalidOperationException(
                                    "الصنف غير موجود.");
                        }

                        using (var command = new SQLiteCommand(
                            @"UPDATE Products
                              SET IsArchived = 1
                              WHERE Id = @Id AND IsArchived = 0;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", productId);

                            if (command.ExecuteNonQuery() == 0)
                                throw new InvalidOperationException(
                                    "الصنف مؤرشف بالفعل.");
                        }

                        WriteProductAudit(
                            connection,
                            transaction,
                            currentUserId,
                            productId,
                            "Archive",
                            "أرشفة الصنف: " + productName);

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        #endregion

        #region Delete Product

        public void DeleteProduct(int currentUserId, int productId)
        {
            if (productId <= 0)
                throw new ArgumentException("الصنف المحدد غير صحيح.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        CheckManagerPermission(connection, currentUserId);

                        string productName;

                        using (var command = new SQLiteCommand(
                            "SELECT Name FROM Products WHERE Id = @Id;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", productId);

                            productName = Convert.ToString(
                                command.ExecuteScalar());

                            if (string.IsNullOrEmpty(productName))
                                throw new InvalidOperationException(
                                    "الصنف غير موجود.");
                        }

                        using (var command = new SQLiteCommand(
                            @"SELECT
                                (SELECT COUNT(*) FROM Batches
                                 WHERE ProductId = @Id) +
                                (SELECT COUNT(*) FROM SalesInvoiceItems
                                 WHERE ProductId = @Id) +
                                (SELECT COUNT(*) FROM PurchaseInvoiceItems
                                 WHERE ProductId = @Id) +
                                (SELECT COUNT(*) FROM SalesReturnItems
                                 WHERE ProductId = @Id);",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", productId);

                            if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                            {
                                throw new InvalidOperationException(
                                    "لا يمكن حذف هذا الصنف نهائيًا لوجود تشغيلات أو " +
                                    "فواتير أو مرتجعات مرتبطة به. يمكنك أرشفته بدلًا من ذلك.");
                            }
                        }

                        using (var command = new SQLiteCommand(
                            "DELETE FROM ProductBarcodes WHERE ProductId = @Id;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", productId);
                            command.ExecuteNonQuery();
                        }

                        WriteProductAudit(
                            connection,
                            transaction,
                            currentUserId,
                            productId,
                            "Delete",
                            "حذف الصنف نهائيًا: " + productName);

                        using (var command = new SQLiteCommand(
                            "DELETE FROM Products WHERE Id = @Id;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Id", productId);
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        #endregion

        #region Audit

        private void WriteProductAudit(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            int productId,
            string actionType,
            string details)
        {
            string username;

            using (var command = new SQLiteCommand(
                "SELECT Username FROM Users WHERE Id = @UserId;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@UserId", userId);
                username = Convert.ToString(command.ExecuteScalar());
            }

            using (var command = new SQLiteCommand(
                @"INSERT INTO AuditLogs
                  (UserId, Username, ActionType, EntityType,
                   EntityId, Details, DeviceName, CreatedAt)
                  VALUES
                  (@UserId, @Username, @ActionType, 'Product',
                   @EntityId, @Details, @DeviceName, @CreatedAt);",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@ActionType", actionType);
                command.Parameters.AddWithValue("@EntityId", productId);
                command.Parameters.AddWithValue("@Details", details);
                command.Parameters.AddWithValue("@DeviceName", Environment.MachineName);
                command.Parameters.AddWithValue(
                    "@CreatedAt",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                command.ExecuteNonQuery();
            }
        }

        #endregion
    }
}
