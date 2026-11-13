
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class SalesReturnsService
    {
        private readonly string connectionString;

        private const double Epsilon = 0.00000001;

        public SalesReturnsService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            // تجهيز جداول الشيفت وعمود ShiftId قبل تنفيذ المرتجعات.
            new ShiftService();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        // ==========================================
        // التحقق من صلاحية تنفيذ المرتجعات
        // ==========================================

        private string ValidateReturnPermission(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId)
        {
            using (var command = new SQLiteCommand(@"
                SELECT u.Username
                FROM Users u
                INNER JOIN Roles r ON r.RoleCode = u.Role
                INNER JOIN RolePermissions rp ON rp.RoleId = r.Id
                INNER JOIN Permissions p ON p.Id = rp.PermissionId
                WHERE u.Id = @UserId
                  AND u.IsActive = 1
                  AND p.PermissionCode = 'Sales.Return'
                  AND rp.IsAllowed = 1
                LIMIT 1;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@UserId", userId);

                object result = command.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                {
                    throw new Exception(
                        "ليس لديك صلاحية تنفيذ مرتجعات البيع.");
                }

                return Convert.ToString(result);
            }
        }

        // ==========================================
        // البحث عن الأصناف القابلة للمرتجع
        // ==========================================

        public List<SalesReturnItem> SearchReturnableItems(
            int userId,
            string searchText)
        {
            if (userId <= 0)
                throw new Exception("رقم المستخدم غير صحيح.");

            var items = new List<SalesReturnItem>();

            string search = (searchText ?? "").Trim();

            if (string.IsNullOrWhiteSpace(search))
                return items;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                ValidateReturnPermission(connection, null, userId);

                string sql = @"
                    SELECT
                        si.Id AS InvoiceId,
                        si.InvoiceNumber,
                        si.CreatedAt AS InvoiceDate,
                        u.FullName AS EmployeeName,

                        sii.Id AS InvoiceItemId,
                        sii.ProductId,
                        p.ProductCode,
                        sii.ItemName,
                        sii.UnitName,

                        sii.Quantity AS SoldQuantity,
                        sii.UnitPrice,
                        sii.PackageQuantity,

                        COALESCE(p.UnitName, sii.UnitName) AS PackageUnitName,
                        COALESCE(p.SmallUnitName, '') AS SmallUnitName,
                        COALESCE(p.UnitsPerPackage, 0) AS UnitsPerPackage,

                        COALESCE(
                            (
                                SELECT SUM(sri.PackageQuantity)
                                FROM SalesReturnItems sri
                                INNER JOIN SalesReturns sr
                                    ON sr.Id = sri.ReturnId
                                WHERE sri.InvoiceItemId = sii.Id
                                  AND sr.Status = 'Completed'
                            ), 0
                        ) AS ReturnedPackageQuantity

                    FROM SalesInvoiceItems sii

                    INNER JOIN SalesInvoices si
                        ON si.Id = sii.InvoiceId

                    INNER JOIN Users u
                        ON u.Id = si.UserId

                    LEFT JOIN Products p
                        ON p.Id = sii.ProductId

                    WHERE si.Status = 'Completed'

                      AND (
                            sii.ItemName LIKE @SearchPattern
                            OR COALESCE(p.Name, '') LIKE @SearchPattern
                            OR COALESCE(p.ProductCode, '') LIKE @SearchPattern
                          )

                    ORDER BY si.Id DESC, sii.Id;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@SearchPattern", "%" + search + "%");

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            SalesReturnItem item =
                                CreateReturnItemFromReader(reader);

                            if (item.RemainingQuantity > Epsilon)
                            {
                                item.InvoiceId = Convert.ToInt32(
                                    reader["InvoiceId"]);

                                item.InvoiceNumber = Convert.ToString(
                                    reader["InvoiceNumber"]);

                                DateTime invoiceDate;

                                DateTime.TryParse(
                                    Convert.ToString(reader["InvoiceDate"]),
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.None,
                                    out invoiceDate);

                                item.InvoiceDate = invoiceDate.ToString(
                                    "yyyy-MM-dd HH:mm");

                                item.EmployeeName = Convert.ToString(
                                    reader["EmployeeName"]);

                                item.ProductCode = Convert.ToString(
                                    reader["ProductCode"]);

                                items.Add(item);
                            }
                        }
                    }
                }
            }

            return items;
        }

        // ==========================================
        // جلب فواتير البيع المكتملة القابلة للمرتجع
        // ==========================================

        public List<SalesReturnDetails> GetInvoicesForReturn(
            int userId,
            string searchText)
        {
            if (userId <= 0)
                throw new Exception("رقم المستخدم غير صحيح.");

            var invoices = new List<SalesReturnDetails>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                ValidateReturnPermission(connection, null, userId);

                string sql = @"
                    SELECT
                        si.Id,
                        si.InvoiceNumber,
                        si.UserId,
                        si.Total,
                        si.Status,
                        si.CreatedAt,
                        u.FullName AS EmployeeName

                    FROM SalesInvoices si

                    INNER JOIN Users u
                        ON u.Id = si.UserId

                    WHERE si.Status = 'Completed'

                    AND EXISTS
                    (
                        SELECT 1
                        FROM SalesInvoiceItems sii

                        WHERE sii.InvoiceId = si.Id

                        AND (
                            sii.PackageQuantity -
                            COALESCE(
                                (
                                    SELECT SUM(sri.PackageQuantity)
                                    FROM SalesReturnItems sri
                                    INNER JOIN SalesReturns sr
                                        ON sr.Id = sri.ReturnId
                                    WHERE sri.InvoiceItemId = sii.Id
                                      AND sr.Status = 'Completed'
                                ), 0
                            )
                        ) > @Epsilon
                    )

                    AND (
                        @SearchText = ''

                        OR si.InvoiceNumber LIKE @SearchPattern

                        OR u.FullName LIKE @SearchPattern

                        OR EXISTS
                        (
                            SELECT 1
                            FROM SalesInvoiceItems sii

                            INNER JOIN Products p
                                ON p.Id = sii.ProductId

                            WHERE sii.InvoiceId = si.Id

                            AND (
                                sii.PackageQuantity -
                                COALESCE(
                                    (
                                        SELECT SUM(sri.PackageQuantity)
                                        FROM SalesReturnItems sri
                                        INNER JOIN SalesReturns sr
                                            ON sr.Id = sri.ReturnId
                                        WHERE sri.InvoiceItemId = sii.Id
                                          AND sr.Status = 'Completed'
                                    ), 0
                                )
                            ) > @Epsilon

                            AND (
                                sii.ItemName LIKE @SearchPattern
                                OR p.Name LIKE @SearchPattern
                                OR p.ProductCode LIKE @SearchPattern
                            )
                        )
                    )

                    ORDER BY si.Id DESC;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    string search = (searchText ?? "").Trim();

                    command.Parameters.AddWithValue(
                        "@SearchText", search);

                    command.Parameters.AddWithValue(
                        "@SearchPattern", "%" + search + "%");

                    command.Parameters.AddWithValue(
                        "@Epsilon", Epsilon);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime createdAt;

                            DateTime.TryParse(
                                Convert.ToString(reader["CreatedAt"]),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out createdAt);

                            invoices.Add(new SalesReturnDetails
                            {
                                InvoiceId = Convert.ToInt32(reader["Id"]),

                                InvoiceNumber = Convert.ToString(
                                    reader["InvoiceNumber"]),

                                UserId = Convert.ToInt32(
                                    reader["UserId"]),

                                EmployeeName = Convert.ToString(
                                    reader["EmployeeName"]),

                                Total = Convert.ToDouble(
                                    reader["Total"]),

                                Status = Convert.ToString(
                                    reader["Status"]),

                                CreatedAt = createdAt
                            });
                        }
                    }
                }
            }

            return invoices;
        }

        // ==========================================
        // جلب أصناف فاتورة محددة
        // ==========================================

        public List<SalesReturnItem> GetInvoiceItemsForReturn(
            int userId,
            int invoiceId)
        {
            if (userId <= 0)
                throw new Exception("رقم المستخدم غير صحيح.");

            if (invoiceId <= 0)
                throw new Exception("رقم الفاتورة غير صحيح.");

            var items = new List<SalesReturnItem>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                ValidateReturnPermission(connection, null, userId);

                string sql = @"
                    SELECT
                        sii.Id AS InvoiceItemId,
                        sii.ProductId,
                        sii.ItemName,
                        sii.UnitName,
                        sii.Quantity AS SoldQuantity,
                        sii.UnitPrice,
                        sii.PackageQuantity,

                        COALESCE(p.UnitName, sii.UnitName) AS PackageUnitName,
                        COALESCE(p.SmallUnitName, '') AS SmallUnitName,
                        COALESCE(p.UnitsPerPackage, 0) AS UnitsPerPackage,

                        COALESCE(
                            (
                                SELECT SUM(sri.PackageQuantity)
                                FROM SalesReturnItems sri
                                INNER JOIN SalesReturns sr
                                    ON sr.Id = sri.ReturnId
                                WHERE sri.InvoiceItemId = sii.Id
                                  AND sr.Status = 'Completed'
                            ), 0
                        ) AS ReturnedPackageQuantity

                    FROM SalesInvoiceItems sii

                    INNER JOIN SalesInvoices si
                        ON si.Id = sii.InvoiceId

                    LEFT JOIN Products p
                        ON p.Id = sii.ProductId

                    WHERE si.Id = @InvoiceId
                      AND si.Status = 'Completed'

                    ORDER BY sii.Id;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@InvoiceId", invoiceId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            SalesReturnItem item =
                                CreateReturnItemFromReader(reader);

                            if (item.RemainingQuantity > Epsilon)
                                items.Add(item);
                        }
                    }
                }
            }

            return items;
        }

        // ==========================================
        // إنشاء بيانات الصنف مع وحدات المرتجع
        // ==========================================

        private SalesReturnItem CreateReturnItemFromReader(
            SQLiteDataReader reader)
        {
            double soldQuantity =
                Convert.ToDouble(reader["SoldQuantity"]);

            double originalPackageQuantity =
                Convert.ToDouble(reader["PackageQuantity"]);

            double originalUnitPrice =
                Convert.ToDouble(reader["UnitPrice"]);

            double returnedPackageQuantity =
                Convert.ToDouble(reader["ReturnedPackageQuantity"]);

            string originalUnitName =
                Convert.ToString(reader["UnitName"]);

            string packageUnitName =
                Convert.ToString(reader["PackageUnitName"]);

            string smallUnitName =
                Convert.ToString(reader["SmallUnitName"]);

            double unitsPerPackage =
                Convert.ToDouble(reader["UnitsPerPackage"]);

            if (string.IsNullOrWhiteSpace(packageUnitName))
                packageUnitName = originalUnitName;

            if (soldQuantity <= 0 || originalPackageQuantity <= 0)
            {
                throw new Exception(
                    "بيانات كمية الصنف في الفاتورة الأصلية غير صحيحة.");
            }

            double remainingPackageQuantity =
                originalPackageQuantity - returnedPackageQuantity;

            if (remainingPackageQuantity < 0 &&
                Math.Abs(remainingPackageQuantity) <= Epsilon)
            {
                remainingPackageQuantity = 0;
            }

            double returnedQuantity =
                returnedPackageQuantity *
                soldQuantity / originalPackageQuantity;

            double remainingQuantity =
                remainingPackageQuantity *
                soldQuantity / originalPackageQuantity;

            // السعر التاريخي للعبوة وقت البيع
            double historicalPackagePrice =
                originalUnitPrice *
                soldQuantity / originalPackageQuantity;

            double defaultReturnPrice = originalUnitPrice;

            var returnUnits = new List<string>();

            if (!string.IsNullOrWhiteSpace(packageUnitName))
                returnUnits.Add(packageUnitName);

            bool hasSmallUnit =
                !string.IsNullOrWhiteSpace(smallUnitName) &&
                unitsPerPackage > 0;

            if (hasSmallUnit &&
                !returnUnits.Contains(smallUnitName))
            {
                returnUnits.Add(smallUnitName);
            }

            if (string.Equals(
                originalUnitName,
                smallUnitName,
                StringComparison.OrdinalIgnoreCase) &&
                hasSmallUnit)
            {
                defaultReturnPrice =
                    historicalPackagePrice / unitsPerPackage;
            }

            if (returnUnits.Count == 0 &&
                !string.IsNullOrWhiteSpace(originalUnitName))
            {
                returnUnits.Add(originalUnitName);
            }

            return new SalesReturnItem
            {
                InvoiceItemId = Convert.ToInt32(
                    reader["InvoiceItemId"]),

                ProductId = Convert.ToInt32(
                    reader["ProductId"]),

                ItemName = Convert.ToString(
                    reader["ItemName"]),

                UnitName = originalUnitName,

                SmallUnitName = smallUnitName,

                UnitsPerPackage = unitsPerPackage,

                ReturnUnits = returnUnits,

                ReturnUnitName = originalUnitName,

                ReturnUnitQuantity = 0,

                ReturnUnitPrice = defaultReturnPrice,

                SoldQuantity = soldQuantity,

                ReturnedQuantity = returnedQuantity,

                RemainingQuantity = remainingQuantity,

                Quantity = 0,

                UnitPrice = originalUnitPrice,

                PackageQuantity = originalPackageQuantity,

                Total = 0
            };
        }

        // ==========================================
        // قراءة بيانات الصنف الأصلية من الفاتورة
        // ==========================================

        private InvoiceReturnInfo GetInvoiceReturnInfo(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int invoiceItemId,
            int invoiceId)
        {
            using (var command = new SQLiteCommand(@"
                SELECT
                    sii.Id,
                    sii.ProductId,
                    sii.ItemName,
                    sii.UnitName,
                    sii.Quantity,
                    sii.UnitPrice,
                    sii.PackageQuantity,

                    COALESCE(p.UnitName, sii.UnitName) AS PackageUnitName,
                    COALESCE(p.SmallUnitName, '') AS SmallUnitName,
                    COALESCE(p.UnitsPerPackage, 0) AS UnitsPerPackage,

                    COALESCE(
                        (
                            SELECT SUM(sri.PackageQuantity)
                            FROM SalesReturnItems sri
                            INNER JOIN SalesReturns sr
                                ON sr.Id = sri.ReturnId
                            WHERE sri.InvoiceItemId = sii.Id
                              AND sr.Status = 'Completed'
                        ), 0
                    ) AS ReturnedPackageQuantity

                FROM SalesInvoiceItems sii

                LEFT JOIN Products p
                    ON p.Id = sii.ProductId

                WHERE sii.Id = @InvoiceItemId
                  AND sii.InvoiceId = @InvoiceId;",
                connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@InvoiceItemId", invoiceItemId);

                command.Parameters.AddWithValue(
                    "@InvoiceId", invoiceId);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        throw new Exception(
                            "أحد الأصناف لا يتبع فاتورة البيع المحددة.");
                    }

                    return new InvoiceReturnInfo
                    {
                        ProductId = Convert.ToInt32(
                            reader["ProductId"]),

                        ItemName = Convert.ToString(
                            reader["ItemName"]),

                        OriginalUnitName = Convert.ToString(
                            reader["UnitName"]),

                        PackageUnitName = Convert.ToString(
                            reader["PackageUnitName"]),

                        SmallUnitName = Convert.ToString(
                            reader["SmallUnitName"]),

                        SoldQuantity = Convert.ToDouble(
                            reader["Quantity"]),

                        OriginalUnitPrice = Convert.ToDouble(
                            reader["UnitPrice"]),

                        OriginalPackageQuantity = Convert.ToDouble(
                            reader["PackageQuantity"]),

                        UnitsPerPackage = Convert.ToDouble(
                            reader["UnitsPerPackage"]),

                        ReturnedPackageQuantity = Convert.ToDouble(
                            reader["ReturnedPackageQuantity"])
                    };
                }
            }
        }

        // ==========================================
        // تجهيز سطر المرتجع والتحويل إلى وحدة العبوة
        // ==========================================

        private PreparedReturnItem PrepareReturnItem(
            SalesReturnItem item,
            InvoiceReturnInfo info)
        {
            if (info.SoldQuantity <= 0 ||
                info.OriginalPackageQuantity <= 0)
            {
                throw new Exception(
                    "بيانات الصنف الأصلية غير صحيحة: " +
                    info.ItemName);
            }

            string selectedUnit =
                (item.ReturnUnitName ?? "").Trim();

            if (string.IsNullOrWhiteSpace(selectedUnit))
                selectedUnit = info.OriginalUnitName;

            double selectedQuantity =
                item.ReturnUnitQuantity;

            // توافق مؤقت مع الشاشة القديمة قبل تحديثها
            if (selectedQuantity <= 0 &&
                item.Quantity > 0)
            {
                selectedQuantity = item.Quantity;
            }

            if (selectedQuantity <= 0 ||
                double.IsNaN(selectedQuantity) ||
                double.IsInfinity(selectedQuantity))
            {
                throw new Exception(
                    "أدخل كمية مرتجع صحيحة للصنف: " +
                    info.ItemName);
            }

            // عدد صحيح في الوحدة المختارة: علبة أو شريط أو قرص
            if (Math.Abs(
                selectedQuantity - Math.Round(selectedQuantity)) > Epsilon)
            {
                throw new Exception(
                    "كمية المرتجع للصنف " + info.ItemName +
                    " يجب أن تكون عددًا صحيحًا من الوحدة المختارة.");
            }

            bool isPackageUnit = string.Equals(
                selectedUnit,
                info.PackageUnitName,
                StringComparison.OrdinalIgnoreCase);

            bool isSmallUnit =
                !string.IsNullOrWhiteSpace(info.SmallUnitName) &&
                string.Equals(
                    selectedUnit,
                    info.SmallUnitName,
                    StringComparison.OrdinalIgnoreCase) &&
                info.UnitsPerPackage > 0;

            if (!isPackageUnit && !isSmallUnit)
            {
                throw new Exception(
                    "وحدة المرتجع المحددة غير معرفة للصنف: " +
                    info.ItemName);
            }

            double historicalPackagePrice =
                info.OriginalUnitPrice *
                info.SoldQuantity /
                info.OriginalPackageQuantity;

            double selectedUnitPrice;

            double packageQuantity;

            if (isPackageUnit)
            {
                selectedUnitPrice = historicalPackagePrice;
                packageQuantity = selectedQuantity;
            }
            else
            {
                selectedUnitPrice =
                    historicalPackagePrice / info.UnitsPerPackage;

                packageQuantity =
                    selectedQuantity / info.UnitsPerPackage;
            }

            packageQuantity = Math.Round(
                packageQuantity,
                8,
                MidpointRounding.AwayFromZero);

            double remainingPackageQuantity =
                info.OriginalPackageQuantity -
                info.ReturnedPackageQuantity;

            if (packageQuantity <= Epsilon)
            {
                throw new Exception(
                    "كمية المرتجع المحولة غير صحيحة للصنف: " +
                    info.ItemName);
            }

            if (packageQuantity >
                remainingPackageQuantity + Epsilon)
            {
                double availableSelectedQuantity =
                    isPackageUnit
                        ? remainingPackageQuantity
                        : remainingPackageQuantity *
                          info.UnitsPerPackage;

                throw new Exception(
                    "الكمية المطلوبة أكبر من المتبقي للصنف: " +
                    info.ItemName +
                    ". الكمية المتاحة تقريبًا: " +
                    Math.Floor(
                        availableSelectedQuantity + Epsilon)
                        .ToString(CultureInfo.InvariantCulture) +
                    " " + selectedUnit);
            }

            double invoiceUnitEquivalent =
                packageQuantity *
                info.SoldQuantity /
                info.OriginalPackageQuantity;

            double lineTotal = Math.Round(
                selectedQuantity * selectedUnitPrice,
                2,
                MidpointRounding.AwayFromZero);

            return new PreparedReturnItem
            {
                InvoiceItemId = item.InvoiceItemId,

                ProductId = info.ProductId,

                ItemName = info.ItemName,

                OriginalUnitName = info.OriginalUnitName,

                OriginalUnitPrice = info.OriginalUnitPrice,

                InvoiceUnitQuantity = invoiceUnitEquivalent,

                PackageQuantity = packageQuantity,

                ReturnUnitName = selectedUnit,

                ReturnUnitQuantity = selectedQuantity,

                ReturnUnitPrice = selectedUnitPrice,

                Total = lineTotal
            };
        }

        // ==========================================
        // حفظ مرتجع البيع - توافق مع الاستدعاءات القديمة
        // ==========================================

        public int SaveReturn(
            int userId,
            int invoiceId,
            string reason,
            List<SalesReturnItem> items)
        {
            return SaveReturn(
                userId,
                invoiceId,
                reason,
                items,
                0,
                "");
        }

        // ==========================================
        // حفظ مرتجع البيع مع مبلغ الرد الفعلي ووسيلته
        // ==========================================

        public int SaveReturn(
            int userId,
            int invoiceId,
            string reason,
            List<SalesReturnItem> items,
            double refundAmount,
            string refundPaymentMethod)
        {
            if (userId <= 0)
                throw new Exception("رقم المستخدم غير صحيح.");

            if (invoiceId <= 0)
                throw new Exception("رقم الفاتورة غير صحيح.");

            if (items == null || items.Count == 0)
                throw new Exception("يجب اختيار صنف واحد على الأقل.");

            if (double.IsNaN(refundAmount) ||
                double.IsInfinity(refundAmount) ||
                refundAmount < 0)
            {
                throw new Exception("مبلغ الرد الفعلي غير صحيح.");
            }

            refundAmount = Math.Round(
                refundAmount,
                2,
                MidpointRounding.AwayFromZero);

            refundPaymentMethod =
                (refundPaymentMethod ?? "").Trim();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var pragma = new SQLiteCommand(
                    "PRAGMA foreign_keys = ON;",
                    connection))
                {
                    pragma.ExecuteNonQuery();
                }

                using (var transaction =
                    connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        string username = ValidateReturnPermission(
                            connection, transaction, userId);

                        string invoiceNumber;

                        using (var command = new SQLiteCommand(@"
                            SELECT InvoiceNumber
                            FROM SalesInvoices
                            WHERE Id = @InvoiceId
                              AND Status = 'Completed';",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@InvoiceId", invoiceId);

                            object result = command.ExecuteScalar();

                            if (result == null || result == DBNull.Value)
                            {
                                throw new Exception(
                                    "فاتورة البيع غير موجودة أو غير مكتملة.");
                            }

                            invoiceNumber = Convert.ToString(result);
                        }

                        var usedInvoiceItems = new HashSet<int>();

                        var preparedItems =
                            new List<PreparedReturnItem>();

                        double returnTotal = 0;

                        // التحقق من الأصناف وحساب قيمة المرتجع
                        foreach (var item in items)
                        {
                            if (item == null)
                                throw new Exception("يوجد صنف غير صالح.");

                            if (item.InvoiceItemId <= 0)
                                throw new Exception(
                                    "رقم صنف الفاتورة غير صحيح.");

                            if (!usedInvoiceItems.Add(item.InvoiceItemId))
                            {
                                throw new Exception(
                                    "لا يمكن إضافة الصنف نفسه أكثر من مرة في المرتجع.");
                            }

                            InvoiceReturnInfo info =
                                GetInvoiceReturnInfo(
                                    connection,
                                    transaction,
                                    item.InvoiceItemId,
                                    invoiceId);

                            PreparedReturnItem prepared =
                                PrepareReturnItem(item, info);

                            preparedItems.Add(prepared);

                            returnTotal += prepared.Total;
                        }

                        returnTotal = Math.Round(
                            returnTotal,
                            2,
                            MidpointRounding.AwayFromZero);

                        if (double.IsNaN(returnTotal) ||
                            double.IsInfinity(returnTotal))
                        {
                            throw new Exception(
                                "إجمالي المرتجع غير صحيح.");
                        }

                        // التحقق من أن مبلغ الرد لا يتجاوز إجمالي المرتجع.
                        if (refundAmount > returnTotal + Epsilon)
                        {
                            throw new Exception(
                                "مبلغ الرد الفعلي لا يمكن أن يكون أكبر من إجمالي المرتجع.");
                        }

                        // لا تُسجل وسيلة رد إذا لم يتم صرف أي مبلغ.
                        if (refundAmount <= Epsilon)
                        {
                            refundAmount = 0;
                            refundPaymentMethod = "";
                        }
                        else
                        {
                            if (refundPaymentMethod != "Cash" &&
                                refundPaymentMethod != "Card" &&
                                refundPaymentMethod != "InstaPay")
                            {
                                throw new Exception(
                                    "اختر وسيلة رد صحيحة: نقدي أو فيزا أو إنستا باي.");
                            }
                        }

                        string returnNumber =
                            "RET-" +
                            DateTime.Now.ToString("yyyyMMddHHmmss") +
                            "-" +
                            Guid.NewGuid().ToString("N")
                                .Substring(0, 6)
                                .ToUpperInvariant();

                        string createdAt = DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);

                        // فتح شيفت تلقائيًا مع أول عملية مرتجع،
                        // أو استخدام الشيفت المفتوح بالفعل.
                        // يتم ذلك داخل نفس معاملة حفظ المرتجع.
                        int shiftId = ShiftService.EnsureOpenShift(
                            connection,
                            transaction,
                            userId,
                            createdAt);

                        int returnId;

                        using (var command = new SQLiteCommand(@"
                            INSERT INTO SalesReturns
                            (
                                ReturnNumber,
                                InvoiceId,
                                UserId,
                                ShiftId,
                                Total,
                                RefundAmount,
                                RefundPaymentMethod,
                                Reason,
                                Status,
                                CreatedAt
                            )
                            VALUES
                            (
                                @ReturnNumber,
                                @InvoiceId,
                                @UserId,
                                @ShiftId,
                                @Total,
                                @RefundAmount,
                                @RefundPaymentMethod,
                                @Reason,
                                'Completed',
                                @CreatedAt
                            );

                            SELECT last_insert_rowid();",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ReturnNumber", returnNumber);

                            command.Parameters.AddWithValue(
                                "@InvoiceId", invoiceId);

                            command.Parameters.AddWithValue(
                                "@UserId", userId);

                            command.Parameters.AddWithValue(
                                "@ShiftId", shiftId);

                            command.Parameters.AddWithValue(
                                "@Total", returnTotal);

                            command.Parameters.AddWithValue(
                                "@RefundAmount", refundAmount);

                            command.Parameters.AddWithValue(
                                "@RefundPaymentMethod",
                                refundPaymentMethod);

                            command.Parameters.AddWithValue(
                                "@Reason", (reason ?? "").Trim());

                            command.Parameters.AddWithValue(
                                "@CreatedAt", createdAt);

                            returnId = Convert.ToInt32(
                                command.ExecuteScalar());
                        }

                        // حفظ عناصر المرتجع وتوزيعها على التشغيلات الأصلية
                        foreach (var item in preparedItems)
                        {
                            int returnItemId;

                            using (var command = new SQLiteCommand(@"
                                INSERT INTO SalesReturnItems
                                (
                                    ReturnId,
                                    InvoiceItemId,
                                    ProductId,
                                    ItemName,
                                    UnitName,
                                    Quantity,
                                    UnitPrice,
                                    Total,
                                    PackageQuantity,
                                    ReturnUnitName,
                                    ReturnUnitQuantity,
                                    ReturnUnitPrice
                                )
                                VALUES
                                (
                                    @ReturnId,
                                    @InvoiceItemId,
                                    @ProductId,
                                    @ItemName,
                                    @UnitName,
                                    @Quantity,
                                    @UnitPrice,
                                    @Total,
                                    @PackageQuantity,
                                    @ReturnUnitName,
                                    @ReturnUnitQuantity,
                                    @ReturnUnitPrice
                                );

                                SELECT last_insert_rowid();",
                                connection, transaction))
                            {
                                command.Parameters.AddWithValue(
                                    "@ReturnId", returnId);

                                command.Parameters.AddWithValue(
                                    "@InvoiceItemId", item.InvoiceItemId);

                                command.Parameters.AddWithValue(
                                    "@ProductId", item.ProductId);

                                command.Parameters.AddWithValue(
                                    "@ItemName", item.ItemName);

                                command.Parameters.AddWithValue(
                                    "@UnitName", item.OriginalUnitName);

                                command.Parameters.AddWithValue(
                                    "@Quantity", item.InvoiceUnitQuantity);

                                command.Parameters.AddWithValue(
                                    "@UnitPrice", item.OriginalUnitPrice);

                                command.Parameters.AddWithValue(
                                    "@Total", item.Total);

                                command.Parameters.AddWithValue(
                                    "@PackageQuantity", item.PackageQuantity);

                                command.Parameters.AddWithValue(
                                    "@ReturnUnitName", item.ReturnUnitName);

                                command.Parameters.AddWithValue(
                                    "@ReturnUnitQuantity", item.ReturnUnitQuantity);

                                command.Parameters.AddWithValue(
                                    "@ReturnUnitPrice", item.ReturnUnitPrice);

                                returnItemId = Convert.ToInt32(
                                    command.ExecuteScalar());
                            }

                            RestoreOriginalBatches(
                                connection,
                                transaction,
                                item.InvoiceItemId,
                                returnItemId,
                                item.PackageQuantity);
                        }

                        // تسجيل العملية ومبلغ الرد ووسيلته في سجل المراجعة.
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
                            command.Parameters.AddWithValue(
                                "@UserId", userId);

                            command.Parameters.AddWithValue(
                                "@Username", username);

                            command.Parameters.AddWithValue(
                                "@ActionType", "Create");

                            command.Parameters.AddWithValue(
                                "@EntityType", "SalesReturn");

                            command.Parameters.AddWithValue(
                                "@EntityId", returnId);

                            string refundDetails =
                                refundAmount > 0
                                    ? "، المبلغ المصروف فعليًا " +
                                      refundAmount.ToString(
                                          "0.00",
                                          CultureInfo.InvariantCulture) +
                                      "، وسيلة الرد " +
                                      refundPaymentMethod
                                    : "، لم يتم صرف مبلغ للعميل";

                            command.Parameters.AddWithValue(
                                "@Details",
                                "إنشاء مرتجع رقم " + returnNumber +
                                " للفاتورة " + invoiceNumber +
                                " بإجمالي " +
                                returnTotal.ToString(
                                    "0.00",
                                    CultureInfo.InvariantCulture) +
                                refundDetails);

                            command.Parameters.AddWithValue(
                                "@DeviceName", Environment.MachineName);

                            command.Parameters.AddWithValue(
                                "@CreatedAt", createdAt);

                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();

                        return returnId;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        // ==========================================
        // إعادة الكمية إلى نفس تشغيلات البيع الأصلية
        // ==========================================

        private void RestoreOriginalBatches(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int invoiceItemId,
            int returnItemId,
            double packageQuantity)
        {
            if (packageQuantity <= Epsilon)
                return;

            string sql = @"
                SELECT
                    a.BatchId,
                    SUM(a.Quantity) AS OriginalQuantity,
                    b.ExpiryDate,

                    SUM(a.Quantity) -
                    COALESCE(
                        (
                            SELECT SUM(rba.Quantity)
                            FROM SalesReturnBatchAllocations rba
                            INNER JOIN SalesReturnItems sri
                                ON sri.Id = rba.ReturnItemId
                            INNER JOIN SalesReturns sr
                                ON sr.Id = sri.ReturnId
                            WHERE sri.InvoiceItemId = @InvoiceItemId
                              AND rba.BatchId = a.BatchId
                              AND sr.Status = 'Completed'
                        ), 0
                    ) AS RemainingAllocation

                FROM SalesInvoiceBatchAllocations a

                INNER JOIN Batches b
                    ON b.Id = a.BatchId

                WHERE a.InvoiceItemId = @InvoiceItemId

                GROUP BY a.BatchId, b.ExpiryDate

                ORDER BY MIN(a.Id);";

            double remainingToAllocate = Math.Round(
                packageQuantity,
                8,
                MidpointRounding.AwayFromZero);

            using (var command = new SQLiteCommand(
                sql, connection, transaction))
            {
                command.Parameters.AddWithValue(
                    "@InvoiceItemId", invoiceItemId);

                using (var reader = command.ExecuteReader())
                {
                    var allocations = new List<ReturnAllocation>();

                    while (reader.Read())
                    {
                        allocations.Add(new ReturnAllocation
                        {
                            BatchId = Convert.ToInt32(
                                reader["BatchId"]),

                            ExpiryDate = Convert.ToString(
                                reader["ExpiryDate"]),

                            RemainingQuantity = Math.Round(
                                Convert.ToDouble(
                                    reader["RemainingAllocation"]),
                                8,
                                MidpointRounding.AwayFromZero)
                        });
                    }

                    reader.Close();

                    foreach (var allocation in allocations)
                    {
                        if (remainingToAllocate <= Epsilon)
                            break;

                        if (allocation.RemainingQuantity <= Epsilon)
                            continue;

                        double allocatedQuantity = Math.Round(
                            Math.Min(
                                remainingToAllocate,
                                allocation.RemainingQuantity),
                            8,
                            MidpointRounding.AwayFromZero);

                        if (allocatedQuantity <= Epsilon)
                            continue;

                        bool isExpired = IsBatchExpired(
                            allocation.ExpiryDate);

                        using (var insertCommand = new SQLiteCommand(@"
                            INSERT INTO SalesReturnBatchAllocations
                            (
                                ReturnItemId,
                                BatchId,
                                Quantity,
                                Restocked
                            )
                            VALUES
                            (
                                @ReturnItemId,
                                @BatchId,
                                @Quantity,
                                @Restocked
                            );",
                            connection, transaction))
                        {
                            insertCommand.Parameters.AddWithValue(
                                "@ReturnItemId", returnItemId);

                            insertCommand.Parameters.AddWithValue(
                                "@BatchId", allocation.BatchId);

                            insertCommand.Parameters.AddWithValue(
                                "@Quantity", allocatedQuantity);

                            insertCommand.Parameters.AddWithValue(
                                "@Restocked", isExpired ? 0 : 1);

                            insertCommand.ExecuteNonQuery();
                        }

                        // لا نعيد الكمية للمخزون إذا كانت التشغيلة منتهية
                        if (!isExpired)
                        {
                            using (var updateCommand = new SQLiteCommand(@"
                                UPDATE Batches
                                SET Quantity = Quantity + @Quantity
                                WHERE Id = @BatchId;",
                                connection, transaction))
                            {
                                updateCommand.Parameters.AddWithValue(
                                    "@Quantity", allocatedQuantity);

                                updateCommand.Parameters.AddWithValue(
                                    "@BatchId", allocation.BatchId);

                                if (updateCommand.ExecuteNonQuery() != 1)
                                {
                                    throw new Exception(
                                        "تعذر تحديث كمية التشغيلة المرتجعة.");
                                }
                            }
                        }

                        remainingToAllocate = Math.Round(
                            remainingToAllocate - allocatedQuantity,
                            8,
                            MidpointRounding.AwayFromZero);
                    }
                }
            }

            if (remainingToAllocate > Epsilon)
            {
                throw new Exception(
                    "تعذر توزيع كمية المرتجع بالكامل على التشغيلات الأصلية. " +
                    "تم إلغاء العملية دون حفظ أي تغييرات.");
            }
        }

        // ==========================================
        // التحقق من انتهاء صلاحية التشغيلة
        // ==========================================

        private bool IsBatchExpired(string expiryDate)
        {
            DateTime parsedDate;

            bool validDate = DateTime.TryParseExact(
                expiryDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedDate);

            if (!validDate)
                return true;

            return parsedDate.Date < DateTime.Today;
        }

        // ==========================================
        // نماذج داخلية
        // ==========================================

        private class InvoiceReturnInfo
        {
            public int ProductId { get; set; }

            public string ItemName { get; set; }

            public string OriginalUnitName { get; set; }

            public string PackageUnitName { get; set; }

            public string SmallUnitName { get; set; }

            public double SoldQuantity { get; set; }

            public double OriginalUnitPrice { get; set; }

            public double OriginalPackageQuantity { get; set; }

            public double UnitsPerPackage { get; set; }

            public double ReturnedPackageQuantity { get; set; }
        }

        private class PreparedReturnItem
        {
            public int InvoiceItemId { get; set; }

            public int ProductId { get; set; }

            public string ItemName { get; set; }

            public string OriginalUnitName { get; set; }

            public double OriginalUnitPrice { get; set; }

            public double InvoiceUnitQuantity { get; set; }

            public double PackageQuantity { get; set; }

            public string ReturnUnitName { get; set; }

            public double ReturnUnitQuantity { get; set; }

            public double ReturnUnitPrice { get; set; }

            public double Total { get; set; }
        }

        private class ReturnAllocation
        {
            public int BatchId { get; set; }

            public string ExpiryDate { get; set; }

            public double RemainingQuantity { get; set; }
        }
    }
}
