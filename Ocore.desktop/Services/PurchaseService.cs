
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class PurchaseService
    {
        private readonly string connectionString;

        public PurchaseService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        private static bool IsInvalidNumber(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
        }

        private static double RoundMoney(double value)
        {
            return Math.Round(
                value,
                2,
                MidpointRounding.AwayFromZero);
        }

        private static bool IsWholeNumber(double value)
        {
            return Math.Abs(value - Math.Round(
                value,
                0,
                MidpointRounding.AwayFromZero)) < 0.0000001;
        }

        private static void ValidateWholeQuantity(
            double quantity,
            string description,
            string itemName)
        {
            if (IsInvalidNumber(quantity) ||
                quantity < 0 ||
                !IsWholeNumber(quantity))
            {
                throw new Exception(
                    description +
                    " يجب أن تكون رقمًا صحيحًا غير سالب للصنف: " +
                    itemName);
            }
        }

        // ==========================================
        // عرض فواتير الشراء
        // ==========================================

        public List<PurchaseInvoice> GetInvoices(
            int userId,
            string searchText,
            DateTime? fromDate,
            DateTime? toDate)
        {
            var invoices = new List<PurchaseInvoice>();

            if (userId <= 0)
                throw new Exception("رقم المستخدم غير صحيح.");

            if (fromDate.HasValue && toDate.HasValue &&
                fromDate.Value.Date > toDate.Value.Date)
            {
                throw new Exception(
                    "تاريخ البداية يجب أن يكون قبل تاريخ النهاية.");
            }

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string sql = @"
                    SELECT
                        pi.Id,
                        pi.InvoiceNumber,
                        pi.SupplierId,
                        s.Name AS SupplierName,
                        pi.UserId,
                        pi.Total,
                        pi.Status,
                        pi.CreatedAt,
                        COALESCE(
                            NULLIF(pi.InvoiceDate, ''),
                            substr(pi.CreatedAt, 1, 10)
                        ) AS EffectiveInvoiceDate

                    FROM PurchaseInvoices pi
                    INNER JOIN Suppliers s ON s.Id = pi.SupplierId

                    WHERE EXISTS
                    (
                        SELECT 1
                        FROM Users u
                        INNER JOIN Roles r ON r.RoleCode = u.Role
                        INNER JOIN RolePermissions rp ON rp.RoleId = r.Id
                        INNER JOIN Permissions p ON p.Id = rp.PermissionId
                        WHERE u.Id = @UserId
                          AND u.IsActive = 1
                          AND p.PermissionCode = 'Purchases.View'
                          AND rp.IsAllowed = 1
                    )

                    AND (
                        @SearchText = ''
                        OR pi.InvoiceNumber LIKE @SearchPattern
                        OR s.Name LIKE @SearchPattern
                    )

                    AND (
                        @FromDate IS NULL
                        OR date(COALESCE(NULLIF(pi.InvoiceDate, ''),
                            substr(pi.CreatedAt, 1, 10))) >= date(@FromDate)
                    )

                    AND (
                        @ToDate IS NULL
                        OR date(COALESCE(NULLIF(pi.InvoiceDate, ''),
                            substr(pi.CreatedAt, 1, 10))) <= date(@ToDate)
                    )

                    ORDER BY pi.Id DESC;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    string search = (searchText ?? "").Trim();

                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@SearchText", search);
                    command.Parameters.AddWithValue(
                        "@SearchPattern", "%" + search + "%");

                    command.Parameters.AddWithValue(
                        "@FromDate",
                        fromDate.HasValue
                            ? fromDate.Value.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture)
                            : (object)DBNull.Value);

                    command.Parameters.AddWithValue(
                        "@ToDate",
                        toDate.HasValue
                            ? toDate.Value.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture)
                            : (object)DBNull.Value);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime invoiceDate;
                            DateTime createdAt;

                            DateTime.TryParse(
                                Convert.ToString(reader["EffectiveInvoiceDate"]),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out invoiceDate);

                            DateTime.TryParse(
                                Convert.ToString(reader["CreatedAt"]),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out createdAt);

                            invoices.Add(new PurchaseInvoice
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                InvoiceNumber = Convert.ToString(reader["InvoiceNumber"]),
                                SupplierId = Convert.ToInt32(reader["SupplierId"]),
                                SupplierName = Convert.ToString(reader["SupplierName"]),
                                UserId = Convert.ToInt32(reader["UserId"]),
                                Total = Convert.ToDouble(reader["Total"]),
                                Status = Convert.ToString(reader["Status"]),
                                InvoiceDate = invoiceDate,
                                CreatedAt = createdAt
                            });
                        }
                    }
                }
            }

            return invoices;
        }

        // ==========================================
        // عرض تفاصيل فاتورة
        // ==========================================

        public PurchaseInvoice GetInvoiceDetails(
            int userId,
            int invoiceId)
        {
            if (userId <= 0)
                throw new Exception("رقم المستخدم غير صحيح.");

            if (invoiceId <= 0)
                throw new Exception("رقم الفاتورة غير صحيح.");

            var invoice = new PurchaseInvoice();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    SELECT
                        pi.Id,
                        pi.InvoiceNumber,
                        pi.SupplierId,
                        s.Name AS SupplierName,
                        pi.UserId,
                        pi.Total,
                        pi.Status,
                        pi.CreatedAt,
                        COALESCE(
                            NULLIF(pi.InvoiceDate, ''),
                            substr(pi.CreatedAt, 1, 10)
                        ) AS EffectiveInvoiceDate

                    FROM PurchaseInvoices pi
                    INNER JOIN Suppliers s ON s.Id = pi.SupplierId

                    WHERE pi.Id = @InvoiceId

                      AND EXISTS
                      (
                          SELECT 1
                          FROM Users u
                          INNER JOIN Roles r ON r.RoleCode = u.Role
                          INNER JOIN RolePermissions rp ON rp.RoleId = r.Id
                          INNER JOIN Permissions p ON p.Id = rp.PermissionId
                          WHERE u.Id = @UserId
                            AND u.IsActive = 1
                            AND p.PermissionCode = 'Purchases.View'
                            AND rp.IsAllowed = 1
                      );",
                    connection))
                {
                    command.Parameters.AddWithValue("@InvoiceId", invoiceId);
                    command.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            throw new Exception(
                                "الفاتورة غير موجودة أو ليس لديك صلاحية عرضها.");
                        }

                        DateTime invoiceDate;
                        DateTime createdAt;

                        DateTime.TryParse(
                            Convert.ToString(reader["EffectiveInvoiceDate"]),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out invoiceDate);

                        DateTime.TryParse(
                            Convert.ToString(reader["CreatedAt"]),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out createdAt);

                        invoice.Id = Convert.ToInt32(reader["Id"]);
                        invoice.InvoiceNumber = Convert.ToString(reader["InvoiceNumber"]);
                        invoice.SupplierId = Convert.ToInt32(reader["SupplierId"]);
                        invoice.SupplierName = Convert.ToString(reader["SupplierName"]);
                        invoice.UserId = Convert.ToInt32(reader["UserId"]);
                        invoice.Total = Convert.ToDouble(reader["Total"]);
                        invoice.Status = Convert.ToString(reader["Status"]);
                        invoice.InvoiceDate = invoiceDate;
                        invoice.CreatedAt = createdAt;
                    }
                }

                using (var command = new SQLiteCommand(@"
                    SELECT
                        pii.ProductId,
                        pii.ItemName,

                        COALESCE(
                            NULLIF(pii.ProductCode, ''),
                            p.ProductCode,
                            ''
                        ) AS ProductCode,

                        COALESCE(
                            NULLIF(pii.Manufacturer, ''),
                            dc.Manufacturer,
                            ''
                        ) AS Manufacturer,

                        COALESCE(
                            NULLIF(pii.PurchaseUnitName, ''),
                            p.UnitName,
                            ''
                        ) AS PurchaseUnitName,

                        COALESCE(
                            NULLIF(pii.PackageUnitName, ''),
                            p.UnitName,
                            ''
                        ) AS PackageUnitName,

                        COALESCE(
                            NULLIF(pii.SmallUnitName, ''),
                            p.SmallUnitName,
                            ''
                        ) AS SmallUnitName,

                        COALESCE(
                            NULLIF(pii.UnitsPerPackage, 0),
                            p.UnitsPerPackage,
                            1
                        ) AS UnitsPerPackage,

                        pii.BatchNumber,
                        pii.ExpiryDate,
                        pii.Quantity,
                        pii.UnitPrice,

                        COALESCE(pii.FreeQuantity, 0) AS FreeQuantity,
                        COALESCE(pii.DiscountPercent, 0) AS DiscountPercent,
                        COALESCE(pii.TaxPercent, 0) AS TaxPercent,
                        COALESCE(pii.Notes, '') AS Notes

                    FROM PurchaseInvoiceItems pii

                    LEFT JOIN Products p ON p.Id = pii.ProductId
                    LEFT JOIN DrugCatalog dc ON dc.Id = p.DrugCatalogId

                    WHERE pii.InvoiceId = @InvoiceId

                    ORDER BY pii.Id;",
                    connection))
                {
                    command.Parameters.AddWithValue("@InvoiceId", invoiceId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string expiryText =
                                reader["ExpiryDate"] == DBNull.Value
                                    ? ""
                                    : Convert.ToString(reader["ExpiryDate"]);

                            DateTime parsedExpiry;
                            DateTime? expiryDate = null;

                            if (!string.IsNullOrWhiteSpace(expiryText) &&
                                DateTime.TryParse(
                                    expiryText,
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.None,
                                    out parsedExpiry))
                            {
                                expiryDate = parsedExpiry;
                            }

                            invoice.Items.Add(new PurchaseInvoiceItem
                            {
                                ProductId = Convert.ToInt32(reader["ProductId"]),
                                ItemName = Convert.ToString(reader["ItemName"]),
                                ProductCode = Convert.ToString(reader["ProductCode"]),
                                Manufacturer = Convert.ToString(reader["Manufacturer"]),

                                UnitName = Convert.ToString(reader["PurchaseUnitName"]),
                                PackageUnitName = Convert.ToString(reader["PackageUnitName"]),
                                SmallUnitName = Convert.ToString(reader["SmallUnitName"]),
                                UnitsPerPackage = Convert.ToInt32(reader["UnitsPerPackage"]),

                                BatchNumber = reader["BatchNumber"] == DBNull.Value
                                    ? ""
                                    : Convert.ToString(reader["BatchNumber"]),

                                ExpiryDate = expiryDate,

                                ExpiryDateText = expiryDate.HasValue
                                    ? expiryDate.Value.ToString("yyyy-MM-dd")
                                    : "",

                                Quantity = Convert.ToDouble(reader["Quantity"]),
                                UnitPrice = Convert.ToDouble(reader["UnitPrice"]),

                                FreeQuantity = Convert.ToDouble(reader["FreeQuantity"]),
                                DiscountPercent = Convert.ToDouble(reader["DiscountPercent"]),
                                TaxPercent = Convert.ToDouble(reader["TaxPercent"]),
                                Notes = Convert.ToString(reader["Notes"])
                            });
                        }
                    }
                }
            }

            return invoice;
        }

        // ==========================================
        // التحقق من بيانات الصنف ووحدة الشراء
        // ==========================================

        private void PreparePurchaseItem(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            PurchaseInvoiceItem item)
        {
            string packageUnitName;
            string smallUnitName;
            string productCode;
            string manufacturer;
            int unitsPerPackage;

            using (var command = new SQLiteCommand(@"
                SELECT
                    p.UnitName,
                    p.SmallUnitName,
                    p.UnitsPerPackage,
                    p.ProductCode,
                    p.DrugCatalogId,
                    COALESCE(dc.Manufacturer, '') AS Manufacturer

                FROM Products p

                LEFT JOIN DrugCatalog dc ON dc.Id = p.DrugCatalogId

                WHERE p.Id = @ProductId
                  AND p.IsArchived = 0;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@ProductId", item.ProductId);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        throw new Exception("أحد الأصناف غير موجود أو مؤرشف.");

                    packageUnitName = Convert.ToString(reader["UnitName"]);
                    smallUnitName = Convert.ToString(reader["SmallUnitName"]);
                    unitsPerPackage = Convert.ToInt32(reader["UnitsPerPackage"]);
                    productCode = Convert.ToString(reader["ProductCode"]);
                    manufacturer = Convert.ToString(reader["Manufacturer"]);

                    item.DrugCatalogId = reader["DrugCatalogId"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(reader["DrugCatalogId"]);
                }
            }

            packageUnitName = (packageUnitName ?? "").Trim();
            smallUnitName = (smallUnitName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(packageUnitName))
                throw new Exception("لم يتم تحديد وحدة العبوة الأساسية للصنف.");

            if (unitsPerPackage <= 0)
                throw new Exception("عدد الوحدات داخل العبوة غير صحيح.");

            if (string.IsNullOrWhiteSpace(item.UnitName))
                item.UnitName = packageUnitName;

            item.UnitName = item.UnitName.Trim();

            bool isPackageUnit = string.Equals(
                item.UnitName,
                packageUnitName,
                StringComparison.OrdinalIgnoreCase);

            bool isSmallUnit =
                !string.IsNullOrWhiteSpace(smallUnitName) &&
                string.Equals(
                    item.UnitName,
                    smallUnitName,
                    StringComparison.OrdinalIgnoreCase);

            if (!isPackageUnit && !isSmallUnit)
            {
                throw new Exception(
                    "وحدة الشراء المختارة غير معرفة لهذا الصنف: " +
                    item.ItemName);
            }

            item.PackageUnitName = packageUnitName;
            item.SmallUnitName = smallUnitName;
            item.UnitsPerPackage = unitsPerPackage;

            if (string.IsNullOrWhiteSpace(item.ProductCode))
                item.ProductCode = productCode;

            if (string.IsNullOrWhiteSpace(item.Manufacturer))
                item.Manufacturer = manufacturer;

            ValidateWholeQuantity(
                item.Quantity,
                "الكمية المشتراة",
                item.ItemName);

            ValidateWholeQuantity(
                item.FreeQuantity,
                "الكمية المجانية",
                item.ItemName);

            if (item.Quantity <= 0)
            {
                throw new Exception(
                    "يجب أن تكون الكمية المشتراة أكبر من صفر للصنف: " +
                    item.ItemName);
            }

            // تاريخ الصلاحية إلزامي للأدوية المرتبطة بدليل الأدوية
            if (item.DrugCatalogId.HasValue)
            {
                if (!item.ExpiryDate.HasValue)
                {
                    throw new Exception(
                        "يجب إدخال تاريخ صلاحية للصنف الدوائي: " +
                        item.ItemName);
                }

                if (item.ExpiryDate.Value == DateTime.MinValue)
                {
                    throw new Exception(
                        "تاريخ الصلاحية غير صحيح للصنف: " +
                        item.ItemName);
                }

                if (item.ExpiryDate.Value.Date < DateTime.Today)
                {
                    throw new Exception(
                        "لا يمكن إضافة دفعة منتهية الصلاحية للصنف: " +
                        item.ItemName);
                }
            }
        }

        // ==========================================
        // تحويل كمية وحدة الشراء إلى العبوة الأساسية
        // ==========================================

        private double ConvertToPackageQuantity(
            double quantity,
            PurchaseInvoiceItem item)
        {
            bool isSmallUnit = string.Equals(
                item.UnitName,
                item.SmallUnitName,
                StringComparison.OrdinalIgnoreCase);

            double result = isSmallUnit
                ? quantity / item.UnitsPerPackage
                : quantity;

            if (IsInvalidNumber(result) || result < 0)
                throw new Exception("كمية المخزون المحولة غير صحيحة.");

            return result;
        }

        // ==========================================
        // حساب تكلفة العبوة الفعلية
        // ==========================================

        private double CalculateInventoryPackageCost(
            PurchaseInvoiceItem item,
            double totalPackageQuantity)
        {
            if (totalPackageQuantity <= 0 ||
                IsInvalidNumber(totalPackageQuantity))
            {
                throw new Exception(
                    "إجمالي كمية المخزون بعد التحويل غير صحيح.");
            }

            double inventoryCost =
                item.Total / totalPackageQuantity;

            if (IsInvalidNumber(inventoryCost) || inventoryCost < 0)
                throw new Exception("تكلفة المخزون الفعلية غير صحيحة.");

            return RoundMoney(inventoryCost);
        }

        // ==========================================
        // التحقق من بيانات البند وحساب الإجمالي
        // ==========================================

        private double ValidateAndGetLineTotal(
            PurchaseInvoiceItem item)
        {
            if (item.UnitPrice < 0 ||
                IsInvalidNumber(item.UnitPrice))
            {
                throw new Exception(
                    "سعر الشراء غير صحيح للصنف: " + item.ItemName);
            }

            if (item.DiscountPercent < 0 ||
                item.DiscountPercent > 100 ||
                IsInvalidNumber(item.DiscountPercent))
            {
                throw new Exception(
                    "نسبة الخصم يجب أن تكون من صفر إلى 100 للصنف: " +
                    item.ItemName);
            }

            if (item.TaxPercent < 0 ||
                IsInvalidNumber(item.TaxPercent))
            {
                throw new Exception(
                    "نسبة الضريبة لا يمكن أن تكون سالبة للصنف: " +
                    item.ItemName);
            }

            double lineTotal = item.Total;

            if (IsInvalidNumber(lineTotal) || lineTotal < 0)
            {
                throw new Exception(
                    "إجمالي الصنف بعد الخصم والضريبة غير صحيح: " +
                    item.ItemName);
            }

            if (IsInvalidNumber(item.DiscountAmount) ||
                IsInvalidNumber(item.TaxAmount) ||
                IsInvalidNumber(item.NetUnitPrice))
            {
                throw new Exception(
                    "تعذر حساب الخصم أو الضريبة للصنف: " +
                    item.ItemName);
            }

            return RoundMoney(lineTotal);
        }

        // ==========================================
        // حفظ فاتورة شراء جديدة
        // ==========================================

        public int SaveInvoice(
            int userId,
            int supplierId,
            DateTime invoiceDate,
            List<PurchaseInvoiceItem> items)
        {
            if (userId <= 0)
                throw new Exception("رقم المستخدم غير صحيح.");

            if (supplierId <= 0)
                throw new Exception("يجب اختيار المورد.");

            if (invoiceDate == DateTime.MinValue)
                throw new Exception("يجب تحديد تاريخ الفاتورة.");

            if (invoiceDate.Date > DateTime.Today)
                throw new Exception(
                    "لا يمكن أن يكون تاريخ الفاتورة في المستقبل.");

            if (items == null || items.Count == 0)
                throw new Exception(
                    "يجب إضافة صنف واحد على الأقل للفاتورة.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var pragmaCommand = new SQLiteCommand(
                    "PRAGMA foreign_keys = ON;", connection))
                {
                    pragmaCommand.ExecuteNonQuery();
                }

                using (var transaction =
                    connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        string username = null;

                        using (var command = new SQLiteCommand(@"
                            SELECT u.Username
                            FROM Users u
                            INNER JOIN Roles r ON r.RoleCode = u.Role
                            INNER JOIN RolePermissions rp ON rp.RoleId = r.Id
                            INNER JOIN Permissions p ON p.Id = rp.PermissionId
                            WHERE u.Id = @UserId
                              AND u.IsActive = 1
                              AND p.PermissionCode = 'Purchases.Create'
                              AND rp.IsAllowed = 1
                            LIMIT 1;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);

                            var result = command.ExecuteScalar();

                            if (result == null || result == DBNull.Value)
                            {
                                throw new Exception(
                                    "ليس لديك صلاحية إنشاء فواتير الشراء.");
                            }

                            username = Convert.ToString(result);
                        }

                        using (var command = new SQLiteCommand(@"
                            SELECT COUNT(*)
                            FROM Suppliers
                            WHERE Id = @SupplierId
                              AND IsActive = 1;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@SupplierId", supplierId);

                            int supplierExists =
                                Convert.ToInt32(command.ExecuteScalar());

                            if (supplierExists == 0)
                            {
                                throw new Exception(
                                    "المورد غير موجود أو غير نشط.");
                            }
                        }

                        double invoiceTotal = 0;

                        foreach (var item in items)
                        {
                            if (item == null)
                                throw new Exception(
                                    "يوجد صنف غير صالح في الفاتورة.");

                            if (item.ProductId <= 0)
                                throw new Exception(
                                    "يوجد صنف برقم غير صحيح.");

                            PreparePurchaseItem(
                                connection,
                                transaction,
                                item);

                            // تاريخ الصلاحية للأصناف اليدوية اختياري،
                            // لكن إذا تم إدخاله فيجب أن يكون صالحًا.
                            if (item.ExpiryDate.HasValue)
                            {
                                if (item.ExpiryDate.Value == DateTime.MinValue)
                                {
                                    throw new Exception(
                                        "تاريخ الصلاحية المدخل غير صحيح.");
                                }

                                if (item.ExpiryDate.Value.Date < DateTime.Today)
                                {
                                    throw new Exception(
                                        "لا يمكن إضافة دفعة منتهية الصلاحية.");
                                }
                            }

                            double lineTotal =
                                ValidateAndGetLineTotal(item);

                            invoiceTotal += lineTotal;

                            if (IsInvalidNumber(invoiceTotal))
                                throw new Exception(
                                    "إجمالي الفاتورة غير صحيح.");
                        }

                        invoiceTotal = RoundMoney(invoiceTotal);

                        string invoiceNumber =
                            "PUR-" +
                            DateTime.Now.ToString("yyyyMMddHHmmss") +
                            "-" +
                            Guid.NewGuid().ToString("N")
                                .Substring(0, 6)
                                .ToUpperInvariant();

                        string createdAt =
                            DateTime.Now.ToString(
                                "yyyy-MM-dd HH:mm:ss",
                                CultureInfo.InvariantCulture);

                        string invoiceDateText =
                            invoiceDate.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture);

                        int invoiceId;

                        using (var command = new SQLiteCommand(@"
                            INSERT INTO PurchaseInvoices
                            (
                                InvoiceNumber,
                                SupplierId,
                                UserId,
                                Total,
                                Status,
                                CreatedAt,
                                InvoiceDate
                            )
                            VALUES
                            (
                                @InvoiceNumber,
                                @SupplierId,
                                @UserId,
                                @Total,
                                'Completed',
                                @CreatedAt,
                                @InvoiceDate
                            );

                            SELECT last_insert_rowid();",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@InvoiceNumber", invoiceNumber);

                            command.Parameters.AddWithValue(
                                "@SupplierId", supplierId);

                            command.Parameters.AddWithValue(
                                "@UserId", userId);

                            command.Parameters.AddWithValue(
                                "@Total", invoiceTotal);

                            command.Parameters.AddWithValue(
                                "@CreatedAt", createdAt);

                            command.Parameters.AddWithValue(
                                "@InvoiceDate", invoiceDateText);

                            invoiceId =
                                Convert.ToInt32(command.ExecuteScalar());
                        }

                        foreach (var item in items)
                        {
                            string expiryDate = item.ExpiryDate.HasValue
                                ? item.ExpiryDate.Value.ToString(
                                    "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture)
                                : "";

                            double lineTotal =
                                ValidateAndGetLineTotal(item);

                            double paidPackageQuantity =
                                ConvertToPackageQuantity(
                                    item.Quantity,
                                    item);

                            double freePackageQuantity =
                                ConvertToPackageQuantity(
                                    item.FreeQuantity,
                                    item);

                            double totalPackageQuantity =
                                paidPackageQuantity + freePackageQuantity;

                            if (IsInvalidNumber(totalPackageQuantity) ||
                                totalPackageQuantity <= 0)
                            {
                                throw new Exception(
                                    "إجمالي كمية المخزون غير صحيح للصنف: " +
                                    item.ItemName);
                            }

                            double inventoryPackageCost =
                                CalculateInventoryPackageCost(
                                    item,
                                    totalPackageQuantity);

                            int batchId;

                            using (var command = new SQLiteCommand(@"
                                INSERT INTO Batches
                                (
                                    ProductId,
                                    SupplierId,
                                    BatchNumber,
                                    ExpiryDate,
                                    PurchasePrice,
                                    Quantity
                                )
                                VALUES
                                (
                                    @ProductId,
                                    @SupplierId,
                                    @BatchNumber,
                                    @ExpiryDate,
                                    @PurchasePrice,
                                    @Quantity
                                );

                                SELECT last_insert_rowid();",
                                connection, transaction))
                            {
                                command.Parameters.AddWithValue(
                                    "@ProductId", item.ProductId);

                                command.Parameters.AddWithValue(
                                    "@SupplierId", supplierId);

                                command.Parameters.AddWithValue(
                                    "@BatchNumber",
                                    string.IsNullOrWhiteSpace(item.BatchNumber)
                                        ? (object)DBNull.Value
                                        : item.BatchNumber.Trim());

                                command.Parameters.AddWithValue(
                                    "@ExpiryDate", expiryDate);

                                command.Parameters.AddWithValue(
                                    "@PurchasePrice", inventoryPackageCost);

                                command.Parameters.AddWithValue(
                                    "@Quantity", totalPackageQuantity);

                                batchId =
                                    Convert.ToInt32(command.ExecuteScalar());
                            }

                            using (var command = new SQLiteCommand(@"
                                INSERT INTO PurchaseInvoiceItems
                                (
                                    InvoiceId,
                                    ProductId,
                                    BatchId,
                                    ItemName,
                                    BatchNumber,
                                    ExpiryDate,
                                    Quantity,
                                    UnitPrice,
                                    Total,

                                    PurchaseUnitName,
                                    PackageUnitName,
                                    SmallUnitName,
                                    UnitsPerPackage,
                                    PackageQuantity,
                                    PackageUnitPrice,

                                    ProductCode,
                                    Manufacturer,

                                    FreeQuantity,
                                    FreePackageQuantity,

                                    DiscountPercent,
                                    DiscountAmount,

                                    TaxPercent,
                                    TaxAmount,

                                    Notes
                                )
                                VALUES
                                (
                                    @InvoiceId,
                                    @ProductId,
                                    @BatchId,
                                    @ItemName,
                                    @BatchNumber,
                                    @ExpiryDate,
                                    @Quantity,
                                    @UnitPrice,
                                    @Total,

                                    @PurchaseUnitName,
                                    @PackageUnitName,
                                    @SmallUnitName,
                                    @UnitsPerPackage,
                                    @PackageQuantity,
                                    @PackageUnitPrice,

                                    @ProductCode,
                                    @Manufacturer,

                                    @FreeQuantity,
                                    @FreePackageQuantity,

                                    @DiscountPercent,
                                    @DiscountAmount,

                                    @TaxPercent,
                                    @TaxAmount,

                                    @Notes
                                );",
                                connection, transaction))
                            {
                                command.Parameters.AddWithValue("@InvoiceId", invoiceId);
                                command.Parameters.AddWithValue("@ProductId", item.ProductId);
                                command.Parameters.AddWithValue("@BatchId", batchId);

                                command.Parameters.AddWithValue(
                                    "@ItemName",
                                    string.IsNullOrWhiteSpace(item.ItemName)
                                        ? "صنف"
                                        : item.ItemName.Trim());

                                command.Parameters.AddWithValue(
                                    "@BatchNumber",
                                    string.IsNullOrWhiteSpace(item.BatchNumber)
                                        ? (object)DBNull.Value
                                        : item.BatchNumber.Trim());

                                command.Parameters.AddWithValue("@ExpiryDate", expiryDate);
                                command.Parameters.AddWithValue("@Quantity", item.Quantity);
                                command.Parameters.AddWithValue("@UnitPrice", item.UnitPrice);
                                command.Parameters.AddWithValue("@Total", lineTotal);

                                command.Parameters.AddWithValue("@PurchaseUnitName", item.UnitName);
                                command.Parameters.AddWithValue("@PackageUnitName", item.PackageUnitName);
                                command.Parameters.AddWithValue("@SmallUnitName", item.SmallUnitName ?? "");
                                command.Parameters.AddWithValue("@UnitsPerPackage", item.UnitsPerPackage);
                                command.Parameters.AddWithValue("@PackageQuantity", paidPackageQuantity);
                                command.Parameters.AddWithValue("@PackageUnitPrice", inventoryPackageCost);

                                command.Parameters.AddWithValue("@ProductCode", item.ProductCode ?? "");
                                command.Parameters.AddWithValue("@Manufacturer", item.Manufacturer ?? "");

                                command.Parameters.AddWithValue("@FreeQuantity", item.FreeQuantity);
                                command.Parameters.AddWithValue("@FreePackageQuantity", freePackageQuantity);

                                command.Parameters.AddWithValue("@DiscountPercent", item.DiscountPercent);
                                command.Parameters.AddWithValue("@DiscountAmount", item.DiscountAmount);

                                command.Parameters.AddWithValue("@TaxPercent", item.TaxPercent);
                                command.Parameters.AddWithValue("@TaxAmount", item.TaxAmount);

                                command.Parameters.AddWithValue("@Notes", item.Notes ?? "");

                                command.ExecuteNonQuery();
                            }

                            using (var command = new SQLiteCommand(@"
                                UPDATE Products
                                SET PurchasePrice = @PurchasePrice
                                WHERE Id = @ProductId
                                  AND IsArchived = 0;",
                                connection, transaction))
                            {
                                command.Parameters.AddWithValue(
                                    "@PurchasePrice", inventoryPackageCost);

                                command.Parameters.AddWithValue(
                                    "@ProductId", item.ProductId);

                                command.ExecuteNonQuery();
                            }
                        }

                        using (var command = new SQLiteCommand(@"
                            INSERT INTO AuditLogs
                            (
                                UserId,
                                Username,
                                ActionType,
                                EntityType,
                                EntityId,
                                Details,
                                DeviceName,
                                CreatedAt
                            )
                            VALUES
                            (
                                @UserId,
                                @Username,
                                @ActionType,
                                @EntityType,
                                @EntityId,
                                @Details,
                                @DeviceName,
                                @CreatedAt
                            );",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);
                            command.Parameters.AddWithValue("@Username", username);
                            command.Parameters.AddWithValue("@ActionType", "Create");
                            command.Parameters.AddWithValue("@EntityType", "PurchaseInvoice");
                            command.Parameters.AddWithValue("@EntityId", invoiceId);

                            command.Parameters.AddWithValue(
                                "@Details",
                                "إنشاء فاتورة شراء رقم " +
                                invoiceNumber +
                                " بإجمالي " +
                                invoiceTotal.ToString(
                                    "0.00",
                                    CultureInfo.InvariantCulture));

                            command.Parameters.AddWithValue(
                                "@DeviceName", Environment.MachineName);

                            command.Parameters.AddWithValue("@CreatedAt", createdAt);

                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();

                        return invoiceId;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}
