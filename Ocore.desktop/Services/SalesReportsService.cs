
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class SalesReportsService
    {
        private readonly DatabaseManager databaseManager;
        private readonly PermissionService permissionService;

        public SalesReportsService()
        {
            databaseManager = new DatabaseManager();
            databaseManager.InitializeDatabase();

            permissionService = new PermissionService();
        }

        // ملخص المبيعات والتكلفة والأرباح، بنفس قواعد KPI
        public SalesReportSummary GetSummary(
            int userId,
            DateTime fromDate,
            DateTime toDate)
        {
            ValidateReportPermission(userId);
            ValidateDates(fromDate, toDate);

            var result = new SalesReportSummary
            {
                FromDate = fromDate.Date,
                ToDate = toDate.Date
            };

            string connectionString =
                "Data Source=" + databaseManager.GetDatabasePath() +
                ";Version=3;";

            // تهيئة جدول المصروفات قبل قراءة ملخص التقرير.
            ExpenseService.EnsureDatabaseSchema(connectionString);

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // إجمالي الفواتير المكتملة قبل خصم المرتجعات.
                string salesSql = @"
SELECT
    COUNT(*) AS InvoiceCount,
    COALESCE(SUM(Total), 0) AS TotalSales
FROM SalesInvoices
WHERE Status = 'Completed'
AND CreatedAt >= @FromDate
AND CreatedAt < @ToDate;";

                using (var command = new SQLiteCommand(salesSql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            result.InvoiceCount = Convert.ToInt32(reader["InvoiceCount"]);
                            result.TotalSales = Convert.ToDouble(reader["TotalSales"]);
                        }
                    }
                }

                // قيمة المرتجعات المكتملة خلال الفترة، بنفس طريقة KPI.
                string returnsSql = @"
SELECT COALESCE(SUM(
    CASE
        WHEN LOWER(COALESCE(sri.TaxStatus, 'Legacy')) = 'calculated'
        THEN COALESCE(sri.TaxableAmount, 0)
           + COALESCE(sri.VatAmount, 0)
           + COALESCE(sri.ScheduleTaxAmount, 0)
        ELSE COALESCE(sri.Total, 0) - COALESCE(sri.DiscountAmount, 0)
    END
), 0)
FROM SalesReturnItems sri
INNER JOIN SalesReturns sr ON sr.Id = sri.ReturnId
WHERE sr.Status = 'Completed'
AND sr.CreatedAt >= @FromDate
AND sr.CreatedAt < @ToDate;";

                using (var command = new SQLiteCommand(returnsSql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);
                    result.TotalReturns = Convert.ToDouble(command.ExecuteScalar());
                }

                // تكلفة الكميات المباعة خلال الفترة.
                string costSql = @"
SELECT COALESCE(SUM(a.Quantity * b.PurchasePrice), 0)
FROM SalesInvoiceBatchAllocations a
INNER JOIN SalesInvoiceItems si ON si.Id = a.InvoiceItemId
INNER JOIN SalesInvoices inv ON inv.Id = si.InvoiceId
INNER JOIN Batches b ON b.Id = a.BatchId
WHERE inv.Status = 'Completed'
AND inv.CreatedAt >= @FromDate
AND inv.CreatedAt < @ToDate;";

                using (var command = new SQLiteCommand(costSql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);
                    result.TotalCost = Convert.ToDouble(command.ExecuteScalar());
                }

                // عكس تكلفة المرتجعات التي أعيدت فعليًا إلى المخزون فقط.
                string returnedCostSql = @"
SELECT COALESCE(SUM(a.Quantity * b.PurchasePrice), 0)
FROM SalesReturnBatchAllocations a
INNER JOIN SalesReturnItems sri ON sri.Id = a.ReturnItemId
INNER JOIN SalesReturns sr ON sr.Id = sri.ReturnId
INNER JOIN Batches b ON b.Id = a.BatchId
WHERE sr.Status = 'Completed'
AND sr.CreatedAt >= @FromDate
AND sr.CreatedAt < @ToDate
AND a.Restocked = 1;";

                using (var command = new SQLiteCommand(returnedCostSql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);
                    result.TotalCost -= Convert.ToDouble(command.ExecuteScalar());
                }

                // مصروفات الفترة غير الملغاة، بنفس شرط KPI.
                string expensesSql = @"
SELECT COALESCE(SUM(Amount), 0)
FROM ExpenseTransactions
WHERE IsVoided = 0
AND CreatedAt >= @FromDate
AND CreatedAt < @ToDate;";

                using (var command = new SQLiteCommand(expensesSql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);
                    result.TotalExpenses = Convert.ToDouble(command.ExecuteScalar());
                }

                // التحقق من اكتمال تخصيص تكلفة جميع الأصناف المباعة.
                string salesCompletenessSql = @"
SELECT COUNT(*)
FROM SalesInvoiceItems si
INNER JOIN SalesInvoices inv ON inv.Id = si.InvoiceId
WHERE inv.Status = 'Completed'
AND inv.CreatedAt >= @FromDate
AND inv.CreatedAt < @ToDate
AND (
    si.PackageQuantity <= 0
    OR ABS(
        si.PackageQuantity - COALESCE(
            (SELECT SUM(a.Quantity)
             FROM SalesInvoiceBatchAllocations a
             WHERE a.InvoiceItemId = si.Id), 0
        )
    ) > 0.000001
);";

                int incompleteSales;
                using (var command = new SQLiteCommand(salesCompletenessSql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);
                    incompleteSales = Convert.ToInt32(command.ExecuteScalar());
                }

                // التحقق من اكتمال تخصيص كل مرتجع في الفترة.
                string returnsCompletenessSql = @"
SELECT COUNT(*)
FROM SalesReturnItems sri
INNER JOIN SalesReturns sr ON sr.Id = sri.ReturnId
WHERE sr.Status = 'Completed'
AND sr.CreatedAt >= @FromDate
AND sr.CreatedAt < @ToDate
AND (
    sri.PackageQuantity <= 0
    OR ABS(
        sri.PackageQuantity - COALESCE(
            (SELECT SUM(a.Quantity)
             FROM SalesReturnBatchAllocations a
             WHERE a.ReturnItemId = sri.Id), 0
        )
    ) > 0.000001
);";

                int incompleteReturns;
                using (var command = new SQLiteCommand(returnsCompletenessSql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);
                    incompleteReturns = Convert.ToInt32(command.ExecuteScalar());
                }

                result.IsCostComplete = incompleteSales == 0 && incompleteReturns == 0;
            }

            // إجمالي المبيعات الصافي بعد المرتجعات، ثم الربح بعد التكلفة والمصروفات.
            result.TotalSales -= result.TotalReturns;
            result.NetProfit = result.IsCostComplete
                ? Math.Round(
                    result.TotalSales - result.TotalCost - result.TotalExpenses,
                    2,
                    MidpointRounding.AwayFromZero)
                : 0;

            return result;
        }

        // تقرير جميع الأصناف المباعة خلال الفترة.
        // الكميات موحدة على PackageQuantity.
        // المرتجعات تخصم فقط إذا كانت مرتبطة ببنود مبيعات الفترة.
        public List<TopSellingProduct> GetTopSellingProducts(
            int userId,
            DateTime fromDate,
            DateTime toDate)
        {
            ValidateReportPermission(userId);
            ValidateDates(fromDate, toDate);

            var result = new List<TopSellingProduct>();

            string sql = @"
WITH SalesLines AS
(
    SELECT
        si.Id AS InvoiceItemId,
        si.ProductId,
        si.InvoiceId,
        si.ItemName,
        COALESCE(si.PackageQuantity, 0) AS PackageQuantity,

        CASE
            WHEN LOWER(COALESCE(si.TaxStatus, 'Legacy')) = 'calculated'
            THEN
                COALESCE(si.TaxableAmount, 0)
                + COALESCE(si.VatAmount, 0)
                + COALESCE(si.ScheduleTaxAmount, 0)
            ELSE
                COALESCE(si.Total, 0)
                - COALESCE(si.DiscountAmount, 0)
        END AS NetLineValue

    FROM SalesInvoiceItems si
    INNER JOIN SalesInvoices inv
        ON inv.Id = si.InvoiceId

    WHERE inv.Status = 'Completed'
      AND inv.CreatedAt >= @FromDate
      AND inv.CreatedAt < @ToDate
),

SalesByProduct AS
(
    SELECT
        ProductId,
        MAX(ItemName) AS HistoricalProductName,
        SUM(PackageQuantity) AS TotalQuantity,
        SUM(NetLineValue) AS TotalSales,
        COUNT(DISTINCT InvoiceId) AS InvoiceCount
    FROM SalesLines
    GROUP BY ProductId
),

ReturnsByProduct AS
(
    SELECT
        sl.ProductId,
        SUM(COALESCE(sri.PackageQuantity, 0)) AS ReturnedQuantity,

        SUM(
            CASE
                WHEN LOWER(COALESCE(sri.TaxStatus, 'Legacy')) = 'calculated'
                THEN
                    COALESCE(sri.TaxableAmount, 0)
                    + COALESCE(sri.VatAmount, 0)
                    + COALESCE(sri.ScheduleTaxAmount, 0)
                ELSE
                    COALESCE(sri.Total, 0)
                    - COALESCE(sri.DiscountAmount, 0)
            END
        ) AS ReturnedSales

    FROM SalesLines sl
    INNER JOIN SalesReturnItems sri
        ON sri.InvoiceItemId = sl.InvoiceItemId
    INNER JOIN SalesReturns sr
        ON sr.Id = sri.ReturnId

    WHERE sr.Status = 'Completed'

    GROUP BY sl.ProductId
),

StockByProduct AS
(
    SELECT
        p.Id AS ProductId,

        COALESCE(
            SUM(
                CASE
                    WHEN b.Quantity > 0
                     AND (
                        COALESCE(b.ExpiryDate, '') = ''
                        OR b.ExpiryDate >= @Today
                     )
                    THEN b.Quantity
                    ELSE 0
                END
            ),
            0
        ) AS CurrentStock,

        COALESCE(
            SUM(
                CASE
                    WHEN b.Quantity > 0
                     AND COALESCE(b.ExpiryDate, '') <> ''
                     AND b.ExpiryDate < @Today
                    THEN b.Quantity
                    ELSE 0
                END
            ),
            0
        ) AS ExpiredQuantity

    FROM Products p
    LEFT JOIN Batches b
        ON b.ProductId = p.Id

    GROUP BY p.Id
)

SELECT
    s.ProductId,

    COALESCE(
        NULLIF(p.Name, ''),
        s.HistoricalProductName,
        ''
    ) AS ProductName,

    COALESCE(s.TotalQuantity, 0) AS TotalQuantity,

    COALESCE(r.ReturnedQuantity, 0) AS ReturnedQuantity,

    (
        COALESCE(s.TotalQuantity, 0)
        - COALESCE(r.ReturnedQuantity, 0)
    ) AS NetQuantity,

    COALESCE(s.TotalSales, 0) AS TotalSales,

    COALESCE(r.ReturnedSales, 0) AS ReturnedSales,

    (
        COALESCE(s.TotalSales, 0)
        - COALESCE(r.ReturnedSales, 0)
    ) AS NetSales,

    COALESCE(s.InvoiceCount, 0) AS InvoiceCount,

    COALESCE(st.CurrentStock, 0) AS CurrentStock,

    COALESCE(st.ExpiredQuantity, 0) AS ExpiredQuantity

FROM SalesByProduct s

LEFT JOIN Products p
    ON p.Id = s.ProductId

LEFT JOIN ReturnsByProduct r
    ON r.ProductId = s.ProductId

LEFT JOIN StockByProduct st
    ON st.ProductId = s.ProductId

ORDER BY
    NetQuantity DESC,
    TotalQuantity DESC,
    ProductName COLLATE NOCASE;";

            string connectionString =
                "Data Source=" + databaseManager.GetDatabasePath() +
                ";Version=3;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(sql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);

                    command.Parameters.AddWithValue(
                        "@Today",
                        DateTime.Today.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture));

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new TopSellingProduct
                            {
                                ProductId =
                                    Convert.ToInt32(reader["ProductId"]),

                                ProductName =
                                    Convert.ToString(reader["ProductName"]),

                                TotalQuantity =
                                    Convert.ToDouble(reader["TotalQuantity"]),

                                ReturnedQuantity =
                                    Convert.ToDouble(reader["ReturnedQuantity"]),

                                NetQuantity =
                                    Convert.ToDouble(reader["NetQuantity"]),

                                TotalSales =
                                    Convert.ToDouble(reader["TotalSales"]),

                                ReturnedSales =
                                    Convert.ToDouble(reader["ReturnedSales"]),

                                NetSales =
                                    Convert.ToDouble(reader["NetSales"]),

                                InvoiceCount =
                                    Convert.ToInt32(reader["InvoiceCount"]),

                                CurrentStock =
                                    Convert.ToDouble(reader["CurrentStock"]),

                                ExpiredQuantity =
                                    Convert.ToDouble(reader["ExpiredQuantity"])
                            });
                        }
                    }
                }
            }

            return result;
        }

        // أداء الموظفين
        public List<SalesEmployeePerformance> GetEmployeePerformance(
            int userId,
            DateTime fromDate,
            DateTime toDate)
        {
            ValidateReportPermission(userId);
            ValidateDates(fromDate, toDate);

            var result = new List<SalesEmployeePerformance>();

            string sql = @"
SELECT
    u.Id AS UserId,
    u.FullName AS EmployeeName,
    COUNT(inv.Id) AS InvoiceCount,
    COALESCE(SUM(inv.Total), 0) AS TotalSales,
    CASE
        WHEN COUNT(inv.Id) > 0
        THEN SUM(inv.Total) / COUNT(inv.Id)
        ELSE 0
    END AS AverageInvoiceValue
FROM SalesInvoices inv
INNER JOIN Users u
    ON u.Id = inv.UserId
WHERE inv.Status = 'Completed'
AND inv.CreatedAt >= @FromDate
AND inv.CreatedAt < @ToDate
GROUP BY u.Id, u.FullName
ORDER BY TotalSales DESC;";

            string connectionString =
                "Data Source=" + databaseManager.GetDatabasePath() +
                ";Version=3;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(sql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new SalesEmployeePerformance
                            {
                                UserId =
                                    Convert.ToInt32(reader["UserId"]),

                                EmployeeName =
                                    reader["EmployeeName"].ToString(),

                                InvoiceCount =
                                    Convert.ToInt32(reader["InvoiceCount"]),

                                TotalSales =
                                    Convert.ToDouble(reader["TotalSales"]),

                                AverageInvoiceValue =
                                    Convert.ToDouble(
                                        reader["AverageInvoiceValue"])
                            });
                        }
                    }
                }
            }

            return result;
        }

        // حركة المبيعات اليومية
        public List<SalesDailySales> GetDailySales(
            int userId,
            DateTime fromDate,
            DateTime toDate)
        {
            ValidateReportPermission(userId);
            ValidateDates(fromDate, toDate);

            var result = new List<SalesDailySales>();

            string sql = @"
SELECT
    substr(CreatedAt, 1, 10) AS SaleDate,
    COALESCE(SUM(Total), 0) AS TotalSales
FROM SalesInvoices
WHERE Status = 'Completed'
AND CreatedAt >= @FromDate
AND CreatedAt < @ToDate
GROUP BY substr(CreatedAt, 1, 10)
ORDER BY SaleDate;";

            string connectionString =
                "Data Source=" + databaseManager.GetDatabasePath() +
                ";Version=3;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(sql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(new SalesDailySales
                            {
                                SaleDate =
                                    reader["SaleDate"].ToString(),

                                TotalSales =
                                    Convert.ToDouble(reader["TotalSales"])
                            });
                        }
                    }
                }
            }

            return result;
        }

        // التحقق من صلاحية عرض التقارير
        private void ValidateReportPermission(int userId)
        {
            string sql = @"
SELECT Role, IsActive
FROM Users
WHERE Id = @UserId;";

            string connectionString =
                "Data Source=" + databaseManager.GetDatabasePath() +
                ";Version=3;";

            string role = null;
            bool isActive = false;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@UserId", userId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            role = reader["Role"].ToString();

                            isActive =
                                Convert.ToInt32(reader["IsActive"]) == 1;
                        }
                    }
                }
            }

            if (!isActive || string.IsNullOrWhiteSpace(role))
            {
                throw new UnauthorizedAccessException(
                    "المستخدم غير نشط أو غير موجود.");
            }

            if (!permissionService.HasPermission(role, "Reports.View"))
            {
                throw new UnauthorizedAccessException(
                    "ليس لديك صلاحية عرض تقارير المبيعات.");
            }
        }

        // التحقق من صحة الفترة الزمنية
        private void ValidateDates(
            DateTime fromDate,
            DateTime toDate)
        {
            if (toDate.Date < fromDate.Date)
            {
                throw new ArgumentException(
                    "تاريخ النهاية يجب ألا يسبق تاريخ البداية.");
            }
        }

        // إضافة معاملات التاريخ للاستعلام
        private void AddDateParameters(
            SQLiteCommand command,
            DateTime fromDate,
            DateTime toDate)
        {
            command.Parameters.AddWithValue(
                "@FromDate",
                fromDate.Date.ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "@ToDate",
                toDate.Date.AddDays(1).ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture));
        }
    }
}
