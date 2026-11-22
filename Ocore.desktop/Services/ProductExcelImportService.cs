
using Ocore.desktop.Data;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;

namespace Ocore.desktop.Services
{
    public class ProductExcelImportPreviewRow
    {
        public int RowNumber { get; set; }
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public string Action { get; set; }
        public string Error { get; set; }

        public bool HasError
        {
            get { return !string.IsNullOrWhiteSpace(Error); }
        }
    }

    public class ProductExcelImportResult
    {
        public int AddedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int ErrorCount { get; set; }
    }

    public class ProductExcelImportService
    {
        private readonly string connectionString;
        private readonly PermissionService permissionService;

        private class ImportPlan
        {
            public ProductExcelRow Row { get; set; }
            public int ExistingProductId { get; set; }
            public string ExistingProductCode { get; set; }
            public string ExistingBarcode { get; set; }
            public double ExistingDiscount { get; set; }
            public double ExistingMinimum { get; set; }
            public string Action { get; set; }
            public string Error { get; set; }
        }

        public ProductExcelImportService()
        {
            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";

            permissionService = new PermissionService();
        }

        public List<ProductExcelImportPreviewRow> PreviewImport(
            int currentUserId,
            IList<ProductExcelRow> rows)
        {
            ValidateInput(rows);

            var result = new List<ProductExcelImportPreviewRow>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(connection, currentUserId);
                bool isManager = IsManager(role);

                var duplicateErrors = GetDuplicateRowErrors(rows);
                var plans = new List<ImportPlan>();

                foreach (var row in rows)
                {
                    var plan = BuildPlan(
                        connection,
                        null,
                        row,
                        isManager,
                        duplicateErrors.ContainsKey(row.RowNumber)
                            ? duplicateErrors[row.RowNumber]
                            : null);

                    plans.Add(plan);
                }

                MarkDuplicateExistingProducts(plans);

                foreach (var plan in plans)
                {
                    result.Add(new ProductExcelImportPreviewRow
                    {
                        RowNumber = plan.Row.RowNumber,
                        ProductCode = plan.Row.ProductCode,
                        ProductName = plan.Row.Name,
                        Action = plan.Action,
                        Error = plan.Error
                    });
                }

                ValidatePreviewPermissions(role, result);
            }

            return result;
        }

        public ProductExcelImportResult Import(
            int currentUserId,
            IList<ProductExcelRow> rows)
        {
            ValidateInput(rows);

            var result = new ProductExcelImportResult();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string role = GetActiveUserRole(
                            connection,
                            transaction,
                            currentUserId);

                        bool isManager = IsManager(role);

                        var plans = new List<ImportPlan>();

                        var duplicateErrors = GetDuplicateRowErrors(rows);

                        // فحص جميع الصفوف قبل أي كتابة.
                        foreach (var row in rows)
                        {
                            var plan = BuildPlan(
                                connection,
                                transaction,
                                row,
                                isManager,
                                duplicateErrors.ContainsKey(row.RowNumber)
                                    ? duplicateErrors[row.RowNumber]
                                    : null);

                            plans.Add(plan);
                        }

                        // منع تحديث الصنف نفسه من أكثر من صف.
                        MarkDuplicateExistingProducts(plans);

                        var errors = plans
                            .Where(x => !string.IsNullOrWhiteSpace(x.Error))
                            .ToList();

                        if (errors.Count > 0)
                        {
                            throw new InvalidOperationException(
                                "تم إلغاء الاستيراد لوجود أخطاء في " +
                                errors.Count +
                                " صف. راجع المعاينة وصحح الأخطاء.");
                        }

                        bool hasNew = plans.Any(
                            x => x.ExistingProductId == 0);

                        bool hasUpdates = plans.Any(
                            x => x.ExistingProductId > 0);

                        if (hasNew &&
                            !permissionService.HasPermission(
                                role,
                                "Products.Create"))
                        {
                            throw new UnauthorizedAccessException(
                                "ليس لديك صلاحية إضافة أصناف جديدة.");
                        }

                        if (hasUpdates &&
                            !permissionService.HasPermission(
                                role,
                                "Products.Edit"))
                        {
                            throw new UnauthorizedAccessException(
                                "ليس لديك صلاحية تعديل الأصناف.");
                        }

                        foreach (var plan in plans)
                        {
                            if (plan.ExistingProductId == 0)
                            {
                                InsertProduct(
                                    connection,
                                    transaction,
                                    currentUserId,
                                    plan,
                                    isManager);

                                result.AddedCount++;
                            }
                            else
                            {
                                UpdateProduct(
                                    connection,
                                    transaction,
                                    currentUserId,
                                    plan,
                                    isManager);

                                result.UpdatedCount++;
                            }
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

            return result;
        }

        private void ValidateInput(IList<ProductExcelRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                throw new ArgumentException(
                    "لا توجد أصناف لاستيرادها.");
            }

            foreach (var row in rows)
            {
                if (row == null)
                {
                    throw new ArgumentException(
                        "يوجد صف فارغ في بيانات الاستيراد.");
                }

                // تطبيع كود الصنف قبل فحص التكرار أو المطابقة.
                row.ProductCode = NormalizeImportedProductCode(
                    row.ProductCode);
            }
        }

        private string NormalizeImportedProductCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            code = code.Trim();

            // لا نقبل في كود الصنف إلا الأرقام الإنجليزية من 0 إلى 9.
            foreach (char character in code)
            {
                if (character < '0' || character > '9')
                    return string.Empty;
            }

            // إزالة الأصفار من البداية مع الاحتفاظ بصفر واحد.
            int index = 0;

            while (index < code.Length - 1 && code[index] == '0')
            {
                index++;
            }

            return code.Substring(index);
        }

        private ImportPlan BuildPlan(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            ProductExcelRow row,
            bool isManager,
            string duplicateError)
        {
            var plan = new ImportPlan
            {
                Row = row,
                Action = "صنف جديد"
            };

            try
            {
                if (!string.IsNullOrWhiteSpace(row.Error))
                {
                    throw new InvalidOperationException(row.Error);
                }

                if (!string.IsNullOrWhiteSpace(duplicateError))
                {
                    throw new InvalidOperationException(duplicateError);
                }

                ValidateRow(row);

                int codeId = FindProductByCode(
                    connection,
                    transaction,
                    row.ProductCode);

                int barcodeId = FindProductByBarcode(
                    connection,
                    transaction,
                    row.Barcode);

                if (codeId > 0 &&
                    barcodeId > 0 &&
                    codeId != barcodeId)
                {
                    throw new InvalidOperationException(
                        "الكود والباركود يشيران إلى صنفين مختلفين.");
                }

                int existingId = codeId > 0 ? codeId : barcodeId;

                if (existingId == 0)
                {
                    if (!isManager &&
                        (Different(row.MaxDiscountPercentage, 0) ||
                         Different(row.MinimumStockLevel, 0)))
                    {
                        throw new InvalidOperationException(
                            "إدخال حد الخصم أو الحد الأدنى للمخزون متاح للمدير فقط.");
                    }

                    plan.ExistingProductId = 0;
                    plan.Action = "إضافة صنف جديد";

                    return plan;
                }

                plan.ExistingProductId = existingId;
                plan.Action = "تحديث صنف موجود";

                using (var command = new SQLiteCommand(
                    @"SELECT ProductCode,
                             COALESCE(MaxDiscountPercentage, 0),
                             COALESCE(MinimumStockLevel, 0),
                             COALESCE(
                                (SELECT Barcode
                                 FROM ProductBarcodes
                                 WHERE ProductId = Products.Id
                                 LIMIT 1), '')
                      FROM Products
                      WHERE Id = @Id;",
                    connection,
                    transaction))
                {
                    command.Parameters.AddWithValue("@Id", existingId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            throw new InvalidOperationException(
                                "الصنف المطابق لم يعد موجودًا.");
                        }

                        plan.ExistingProductCode =
                            Convert.ToString(reader[0]);

                        plan.ExistingDiscount =
                            Convert.ToDouble(reader[1]);

                        plan.ExistingMinimum =
                            Convert.ToDouble(reader[2]);

                        plan.ExistingBarcode =
                            Convert.ToString(reader[3]);
                    }
                }

                if (codeId == 0 &&
                    !string.IsNullOrWhiteSpace(row.ProductCode))
                {
                    throw new InvalidOperationException(
                        "الباركود يطابق صنفًا لكن كود الملف لا يطابق كوده الحالي.");
                }

                if (barcodeId > 0 &&
                    !string.IsNullOrWhiteSpace(row.ProductCode) &&
                    !string.Equals(
                        row.ProductCode.Trim(),
                        plan.ExistingProductCode,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "كود الملف مختلف عن كود الصنف المطابق بالباركود.");
                }

                if (!isManager &&
                    (Different(
                        row.MaxDiscountPercentage,
                        plan.ExistingDiscount) ||
                     Different(
                        row.MinimumStockLevel,
                        plan.ExistingMinimum)))
                {
                    throw new InvalidOperationException(
                        "تعديل حد الخصم والحد الأدنى للمخزون متاح للمدير فقط.");
                }
            }
            catch (Exception ex)
            {
                plan.Error = ex.Message;
            }

            return plan;
        }

        private void ValidateRow(ProductExcelRow row)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                throw new InvalidOperationException(
                    "اسم الصنف مطلوب.");
            }

            if (string.IsNullOrWhiteSpace(row.UnitName))
            {
                throw new InvalidOperationException(
                    "الوحدة (P) مطلوبة.");
            }

            if (string.IsNullOrWhiteSpace(row.SmallUnitName))
            {
                throw new InvalidOperationException(
                    "الوحدة (U) مطلوبة.");
            }

            if (row.UnitsPerPackage < 1)
            {
                throw new InvalidOperationException(
                    "معامل التحويل يجب أن يكون أكبر من صفر.");
            }

            if (InvalidNumber(row.PurchasePrice) ||
                row.PurchasePrice < 0)
            {
                throw new InvalidOperationException(
                    "سعر الشراء غير صحيح.");
            }

            if (InvalidNumber(row.SalePrice) ||
                row.SalePrice < 0)
            {
                throw new InvalidOperationException(
                    "سعر البيع غير صحيح.");
            }

            if (InvalidNumber(row.MaxDiscountPercentage) ||
                row.MaxDiscountPercentage < 0 ||
                row.MaxDiscountPercentage > 100)
            {
                throw new InvalidOperationException(
                    "حد الخصم غير صحيح.");
            }

            if (InvalidNumber(row.MinimumStockLevel) ||
                row.MinimumStockLevel < 0)
            {
                throw new InvalidOperationException(
                    "الحد الأدنى غير صحيح.");
            }
        }

        private bool InvalidNumber(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
        }

        private bool Different(double a, double b)
        {
            return Math.Abs(a - b) > 0.00000001;
        }

        private int FindProductByCode(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return 0;

            using (var command = new SQLiteCommand(
                @"SELECT Id
                  FROM Products
                  WHERE ProductCode = @Code
                  LIMIT 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@Code",
                    code.Trim());

                object value = command.ExecuteScalar();

                return value == null || value == DBNull.Value
                    ? 0
                    : Convert.ToInt32(value);
            }
        }

        private int FindProductByBarcode(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return 0;

            string value = barcode.Trim();

            var candidates = new List<string> { value };

            // نفس قواعد GTIN الموجودة في ProductService.
            if (value.Length == 14 &&
                value.StartsWith("00", StringComparison.Ordinal))
            {
                candidates.Add(value.Substring(2));
            }
            else if (value.Length == 14 &&
                     value.StartsWith("0", StringComparison.Ordinal))
            {
                candidates.Add(value.Substring(1));
            }
            else if (value.Length == 13 || value.Length == 12)
            {
                candidates.Add(value.PadLeft(14, '0'));
            }

            // دعم باركود GS1 الذي يحتوي على AI (01).
            try
            {
                var parsed = GS1BarcodeService.Parse(value);

                if (!string.IsNullOrWhiteSpace(parsed.GTIN))
                {
                    candidates.Add(parsed.GTIN);
                }
            }
            catch
            {
                // قد يكون الباركود رقمًا داخليًا وليس GTIN.
                // تظل المطابقة الدقيقة والصيغ القياسية متاحة.
            }

            foreach (string candidate in candidates.Distinct())
            {
                using (var command = new SQLiteCommand(
                    @"SELECT ProductId
                      FROM ProductBarcodes
                      WHERE Barcode = @Barcode
                      LIMIT 1;",
                    connection,
                    transaction))
                {
                    command.Parameters.AddWithValue(
                        "@Barcode",
                        candidate);

                    object found = command.ExecuteScalar();

                    if (found != null && found != DBNull.Value)
                    {
                        return Convert.ToInt32(found);
                    }
                }
            }

            return 0;
        }

        private void InsertProduct(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            ImportPlan plan,
            bool isManager)
        {
            string code = (plan.Row.ProductCode ?? "").Trim();
            string barcode = (plan.Row.Barcode ?? "").Trim();

            GenerateMissingIdentifiers(
                connection,
                transaction,
                ref code,
                ref barcode);

            int categoryId = GetOrCreateCategory(
                connection,
                transaction,
                plan.Row.CategoryName);

            using (var command = new SQLiteCommand(
                @"INSERT INTO Products
                  (ProductCode, Name, CategoryId,
                   UnitName, SmallUnitName, UnitsPerPackage,
                   SalePrice, PurchasePrice,
                   MaxDiscountPercentage, MinimumStockLevel,
                   IsArchived, CreatedAt, TaxProfileId, TaxPriceMode)
                  VALUES
                  (@Code, @Name, @CategoryId,
                   @Unit, @SmallUnit, @Units,
                   @Sale, @Purchase,
                   @Discount, @Minimum,
                   0, @CreatedAt, NULL, 'Exclusive');",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@Code", code);

                command.Parameters.AddWithValue(
                    "@Name",
                    plan.Row.Name.Trim());

                command.Parameters.AddWithValue(
                    "@CategoryId",
                    categoryId > 0
                        ? (object)categoryId
                        : DBNull.Value);

                command.Parameters.AddWithValue(
                    "@Unit",
                    plan.Row.UnitName.Trim());

                command.Parameters.AddWithValue(
                    "@SmallUnit",
                    plan.Row.SmallUnitName.Trim());

                command.Parameters.AddWithValue(
                    "@Units",
                    plan.Row.UnitsPerPackage);

                command.Parameters.AddWithValue(
                    "@Sale",
                    plan.Row.SalePrice);

                command.Parameters.AddWithValue(
                    "@Purchase",
                    plan.Row.PurchasePrice);

                command.Parameters.AddWithValue(
                    "@Discount",
                    isManager
                        ? plan.Row.MaxDiscountPercentage
                        : 0);

                command.Parameters.AddWithValue(
                    "@Minimum",
                    isManager
                        ? plan.Row.MinimumStockLevel
                        : 0);

                command.Parameters.AddWithValue(
                    "@CreatedAt",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                command.ExecuteNonQuery();
            }

            int productId;

            using (var command = new SQLiteCommand(
                "SELECT last_insert_rowid();",
                connection,
                transaction))
            {
                productId = Convert.ToInt32(
                    command.ExecuteScalar());
            }

            using (var command = new SQLiteCommand(
                @"INSERT INTO ProductBarcodes
                  (ProductId, Barcode)
                  VALUES (@ProductId, @Barcode);",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@ProductId",
                    productId);

                command.Parameters.AddWithValue(
                    "@Barcode",
                    barcode);

                command.ExecuteNonQuery();
            }

            WriteAudit(
                connection,
                transaction,
                userId,
                productId,
                "Create",
                "إضافة صنف من ملف Excel: " + plan.Row.Name);
        }

        private void UpdateProduct(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            ImportPlan plan,
            bool isManager)
        {
            int productId = plan.ExistingProductId;

            int categoryId = GetOrCreateCategory(
                connection,
                transaction,
                plan.Row.CategoryName);

            bool unitsChanged = false;

            using (var command = new SQLiteCommand(
                @"SELECT UnitName, SmallUnitName, UnitsPerPackage
                  FROM Products
                  WHERE Id = @Id;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@Id", productId);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        unitsChanged =
                            !string.Equals(
                                Convert.ToString(reader["UnitName"]),
                                plan.Row.UnitName.Trim(),
                                StringComparison.OrdinalIgnoreCase) ||

                            !string.Equals(
                                Convert.ToString(reader["SmallUnitName"]),
                                plan.Row.SmallUnitName.Trim(),
                                StringComparison.OrdinalIgnoreCase) ||

                            Convert.ToInt32(
                                reader["UnitsPerPackage"]) !=
                                plan.Row.UnitsPerPackage;
                    }
                }
            }

            if (unitsChanged &&
                HasProductMovements(
                    connection,
                    transaction,
                    productId))
            {
                throw new InvalidOperationException(
                    "لا يمكن تغيير الوحدات أو معامل التحويل لصنف له حركات سابقة. الصنف: " +
                    plan.Row.Name);
            }

            using (var command = new SQLiteCommand(
                @"UPDATE Products
                  SET Name = @Name,
                      CategoryId = @CategoryId,
                      UnitName = @Unit,
                      SmallUnitName = @SmallUnit,
                      UnitsPerPackage = @Units,
                      SalePrice = @Sale,
                      PurchasePrice = @Purchase
                  WHERE Id = @Id;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@Name",
                    plan.Row.Name.Trim());

                command.Parameters.AddWithValue(
                    "@CategoryId",
                    categoryId > 0
                        ? (object)categoryId
                        : DBNull.Value);

                command.Parameters.AddWithValue(
                    "@Unit",
                    plan.Row.UnitName.Trim());

                command.Parameters.AddWithValue(
                    "@SmallUnit",
                    plan.Row.SmallUnitName.Trim());

                command.Parameters.AddWithValue(
                    "@Units",
                    plan.Row.UnitsPerPackage);

                command.Parameters.AddWithValue(
                    "@Sale",
                    plan.Row.SalePrice);

                command.Parameters.AddWithValue(
                    "@Purchase",
                    plan.Row.PurchasePrice);

                command.Parameters.AddWithValue(
                    "@Id",
                    productId);

                if (command.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException(
                        "تعذر تحديث الصنف: " + plan.Row.Name);
                }
            }

            if (isManager)
            {
                using (var command = new SQLiteCommand(
                    @"UPDATE Products
                      SET MaxDiscountPercentage = @Discount,
                          MinimumStockLevel = @Minimum
                      WHERE Id = @Id;",
                    connection,
                    transaction))
                {
                    command.Parameters.AddWithValue(
                        "@Discount",
                        plan.Row.MaxDiscountPercentage);

                    command.Parameters.AddWithValue(
                        "@Minimum",
                        plan.Row.MinimumStockLevel);

                    command.Parameters.AddWithValue(
                        "@Id",
                        productId);

                    command.ExecuteNonQuery();
                }
            }

            WriteAudit(
                connection,
                transaction,
                userId,
                productId,
                "Update",
                "تحديث بيانات الصنف من ملف Excel: " + plan.Row.Name);
        }

        private bool HasProductMovements(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int productId)
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
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@Id", productId);

                return Convert.ToInt32(
                    command.ExecuteScalar()) > 0;
            }
        }

        private int GetOrCreateCategory(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                return 0;

            categoryName = categoryName.Trim();

            using (var command = new SQLiteCommand(
                @"SELECT Id
                  FROM Categories
                  WHERE Name = @Name COLLATE NOCASE
                  LIMIT 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@Name",
                    categoryName);

                object value = command.ExecuteScalar();

                if (value != null && value != DBNull.Value)
                {
                    return Convert.ToInt32(value);
                }
            }

            using (var command = new SQLiteCommand(
                @"INSERT INTO Categories (Name, IsActive)
                  VALUES (@Name, 1);",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@Name",
                    categoryName);

                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand(
                "SELECT last_insert_rowid();",
                connection,
                transaction))
            {
                return Convert.ToInt32(
                    command.ExecuteScalar());
            }
        }

        private void GenerateMissingIdentifiers(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            ref string code,
            ref string barcode)
        {
            if (!string.IsNullOrWhiteSpace(code) &&
                !string.IsNullOrWhiteSpace(barcode))
            {
                return;
            }

            EnsureProductCodeSequence(
                connection,
                transaction);

            while (true)
            {
                using (var command = new SQLiteCommand(
                    @"UPDATE ProductCodeSequence
                      SET LastValue = LastValue + 1
                      WHERE Id = 1;",
                    connection,
                    transaction))
                {
                    command.ExecuteNonQuery();
                }

                long serial;

                using (var command = new SQLiteCommand(
                    @"SELECT LastValue
                      FROM ProductCodeSequence
                      WHERE Id = 1;",
                    connection,
                    transaction))
                {
                    serial = Convert.ToInt64(
                        command.ExecuteScalar());
                }

                if (serial > 9999999999L)
                {
                    throw new InvalidOperationException(
                        "وصل تسلسل الأكواد إلى الحد الأقصى.");
                }

                // كود الصنف أرقام فقط دون أصفار بادئة.
                string generatedCode = serial.ToString(
                    CultureInfo.InvariantCulture);

                // لا نغيّر صيغة الباركود أو أصفاره.
                string barcodeBase =
                    "20" + serial.ToString("D10");

                string generatedBarcode =
                    barcodeBase +
                    CalculateEan13CheckDigit(barcodeBase);

                bool codeExists = Exists(
                    connection,
                    transaction,
                    @"SELECT COUNT(*)
                      FROM Products
                      WHERE ProductCode = @Value;",
                    generatedCode);

                bool barcodeExists = Exists(
                    connection,
                    transaction,
                    @"SELECT COUNT(*)
                      FROM ProductBarcodes
                      WHERE Barcode = @Value;",
                    generatedBarcode);

                if (codeExists || barcodeExists)
                    continue;

                if (string.IsNullOrWhiteSpace(code))
                    code = generatedCode;

                if (string.IsNullOrWhiteSpace(barcode))
                    barcode = generatedBarcode;

                if (Exists(
                    connection,
                    transaction,
                    @"SELECT COUNT(*)
                      FROM Products
                      WHERE ProductCode = @Value;",
                    code))
                {
                    if (code == generatedCode)
                        continue;

                    throw new InvalidOperationException(
                        "كود الصنف مستخدم بالفعل: " + code);
                }

                if (Exists(
                    connection,
                    transaction,
                    @"SELECT COUNT(*)
                      FROM ProductBarcodes
                      WHERE Barcode = @Value;",
                    barcode))
                {
                    if (barcode == generatedBarcode)
                        continue;

                    throw new InvalidOperationException(
                        "الباركود مستخدم بالفعل: " + barcode);
                }

                return;
            }
        }

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
                connection,
                transaction))
            {
                command.ExecuteNonQuery();
            }

            long maximumExistingCode;

            using (var command = new SQLiteCommand(@"
                SELECT COALESCE(MAX(CAST(ProductCode AS INTEGER)), 0)
                FROM Products
                WHERE ProductCode <> ''
                  AND ProductCode NOT GLOB '*[^0-9]*';",
                connection,
                transaction))
            {
                maximumExistingCode =
                    Convert.ToInt64(command.ExecuteScalar());
            }

            using (var command = new SQLiteCommand(@"
                INSERT OR IGNORE INTO ProductCodeSequence
                    (Id, LastValue)
                VALUES (1, @InitialValue);

                UPDATE ProductCodeSequence
                SET LastValue = MAX(LastValue, @InitialValue)
                WHERE Id = 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@InitialValue",
                    maximumExistingCode);

                command.ExecuteNonQuery();
            }
        }

        private int CalculateEan13CheckDigit(string barcodeBase)
        {
            int sum = 0;

            for (int i = 0; i < barcodeBase.Length; i++)
            {
                int digit = barcodeBase[i] - '0';

                sum += (i % 2 == 0)
                    ? digit
                    : digit * 3;
            }

            return (10 - (sum % 10)) % 10;
        }

        private bool Exists(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string sql,
            string value)
        {
            using (var command = new SQLiteCommand(
                sql,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@Value",
                    value);

                return Convert.ToInt32(
                    command.ExecuteScalar()) > 0;
            }
        }

        private string GetActiveUserRole(
            SQLiteConnection connection,
            int userId)
        {
            using (var command = new SQLiteCommand(
                @"SELECT Role
                  FROM Users
                  WHERE Id = @UserId
                    AND IsActive = 1;",
                connection))
            {
                command.Parameters.AddWithValue(
                    "@UserId",
                    userId);

                object value = command.ExecuteScalar();

                if (value == null || value == DBNull.Value)
                {
                    throw new UnauthorizedAccessException(
                        "المستخدم غير موجود أو حسابه غير نشط.");
                }

                return Convert.ToString(value);
            }
        }

        private string GetActiveUserRole(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId)
        {
            using (var command = new SQLiteCommand(
                @"SELECT Role
                  FROM Users
                  WHERE Id = @UserId
                    AND IsActive = 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@UserId",
                    userId);

                object value = command.ExecuteScalar();

                if (value == null || value == DBNull.Value)
                {
                    throw new UnauthorizedAccessException(
                        "المستخدم غير موجود أو حسابه غير نشط.");
                }

                return Convert.ToString(value);
            }
        }

        private Dictionary<int, string> GetDuplicateRowErrors(
            IList<ProductExcelRow> rows)
        {
            var errors = new Dictionary<int, string>();

            var codes = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

            var barcodes = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (row == null)
                    continue;

                // الأكواد هنا مطبّعة بالفعل قبل فحص التكرار.
                string code =
                    (row.ProductCode ?? string.Empty).Trim();

                string barcode =
                    (row.Barcode ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(code))
                {
                    if (codes.ContainsKey(code))
                    {
                        errors[row.RowNumber] =
                            "كود الصنف مكرر داخل ملف Excel (موجود أيضًا في الصف " +
                            codes[code] + ").";
                    }
                    else
                    {
                        codes.Add(code, row.RowNumber);
                    }
                }

                if (!string.IsNullOrWhiteSpace(barcode))
                {
                    if (barcodes.ContainsKey(barcode))
                    {
                        if (!errors.ContainsKey(row.RowNumber))
                        {
                            errors[row.RowNumber] =
                                "الباركود مكرر داخل ملف Excel (موجود أيضًا في الصف " +
                                barcodes[barcode] + ").";
                        }
                    }
                    else
                    {
                        barcodes.Add(barcode, row.RowNumber);
                    }
                }
            }

            return errors;
        }

        // يمنع تكرار تحديث الصنف نفسه من أكثر من صف في ملف Excel.
        private void MarkDuplicateExistingProducts(
            List<ImportPlan> plans)
        {
            var duplicateGroups = plans
                .Where(x => x.ExistingProductId > 0)
                .GroupBy(x => x.ExistingProductId)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicateGroups)
            {
                foreach (var plan in group)
                {
                    plan.Error =
                        "أكثر من صف في ملف Excel يشير إلى الصنف نفسه. " +
                        "راجع الكود والباركود في الصفوف المتكررة.";
                }
            }
        }

        private bool IsManager(string role)
        {
            return string.Equals(
                role,
                "Manager",
                StringComparison.OrdinalIgnoreCase);
        }

        private void ValidatePreviewPermissions(
            string role,
            List<ProductExcelImportPreviewRow> rows)
        {
            bool hasNew = rows.Any(
                x => !x.HasError &&
                     x.Action == "إضافة صنف جديد");

            bool hasUpdates = rows.Any(
                x => !x.HasError &&
                     x.Action == "تحديث صنف موجود");

            if (hasNew &&
                !permissionService.HasPermission(
                    role,
                    "Products.Create"))
            {
                foreach (var row in rows.Where(
                    x => !x.HasError &&
                         x.Action == "إضافة صنف جديد"))
                {
                    row.Error =
                        "لا توجد صلاحية لإضافة أصناف جديدة.";
                }
            }

            if (hasUpdates &&
                !permissionService.HasPermission(
                    role,
                    "Products.Edit"))
            {
                foreach (var row in rows.Where(
                    x => !x.HasError &&
                         x.Action == "تحديث صنف موجود"))
                {
                    row.Error =
                        "لا توجد صلاحية لتعديل الأصناف.";
                }
            }
        }

        private void WriteAudit(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            int productId,
            string actionType,
            string details)
        {
            string username;

            using (var command = new SQLiteCommand(
                @"SELECT Username
                  FROM Users
                  WHERE Id = @UserId;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@UserId",
                    userId);

                username = Convert.ToString(
                    command.ExecuteScalar());
            }

            using (var command = new SQLiteCommand(
                @"INSERT INTO AuditLogs
                  (UserId, Username, ActionType, EntityType,
                   EntityId, Details, DeviceName, CreatedAt)
                  VALUES
                  (@UserId, @Username, @ActionType, 'Product',
                   @EntityId, @Details, @DeviceName, @CreatedAt);",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@UserId",
                    userId);

                command.Parameters.AddWithValue(
                    "@Username",
                    username);

                command.Parameters.AddWithValue(
                    "@ActionType",
                    actionType);

                command.Parameters.AddWithValue(
                    "@EntityId",
                    productId);

                command.Parameters.AddWithValue(
                    "@Details",
                    details);

                command.Parameters.AddWithValue(
                    "@DeviceName",
                    Environment.MachineName);

                command.Parameters.AddWithValue(
                    "@CreatedAt",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                command.ExecuteNonQuery();
            }
        }
    }
}
