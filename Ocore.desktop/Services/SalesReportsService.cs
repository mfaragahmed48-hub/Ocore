
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

        // ملخص المبيعات والتكلفة والأرباح
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

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

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
                            result.InvoiceCount =
                                Convert.ToInt32(reader["InvoiceCount"]);

                            result.TotalSales =
                                Convert.ToDouble(reader["TotalSales"]);
                        }
                    }
                }

                string costSql = @"
SELECT COALESCE(
    SUM(a.Quantity * b.PurchasePrice), 0
)
FROM SalesInvoiceBatchAllocations a
INNER JOIN SalesInvoiceItems si
    ON si.Id = a.InvoiceItemId
INNER JOIN SalesInvoices inv
    ON inv.Id = si.InvoiceId
INNER JOIN Batches b
    ON b.Id = a.BatchId
WHERE inv.Status = 'Completed'
AND inv.CreatedAt >= @FromDate
AND inv.CreatedAt < @ToDate;";

                using (var command = new SQLiteCommand(costSql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);

                    result.TotalCost =
                        Convert.ToDouble(command.ExecuteScalar());
                }

                // التأكد من اكتمال تكلفة جميع الأصناف المباعة
                string completenessSql = @"
SELECT COUNT(*)
FROM SalesInvoiceItems si
INNER JOIN SalesInvoices inv
    ON inv.Id = si.InvoiceId
WHERE inv.Status = 'Completed'
AND inv.CreatedAt >= @FromDate
AND inv.CreatedAt < @ToDate
AND (
    si.PackageQuantity <= 0
    OR ABS(
        si.PackageQuantity -
        COALESCE(
            (
                SELECT SUM(a.Quantity)
                FROM SalesInvoiceBatchAllocations a
                WHERE a.InvoiceItemId = si.Id
            ), 0
        )
    ) > 0.000001
);";

                using (var command = new SQLiteCommand(
                    completenessSql, connection))
                {
                    AddDateParameters(command, fromDate, toDate);

                    int incompleteItems =
                        Convert.ToInt32(command.ExecuteScalar());

                    result.IsCostComplete = incompleteItems == 0;
                }
            }

            result.NetProfit = result.IsCostComplete
                ? Math.Round(
                    result.TotalSales - result.TotalCost,
                    2,
                    MidpointRounding.AwayFromZero)
                : 0;

            return result;
        }

        // أفضل 10 أصناف مبيعاً حسب الكمية
        // قيمة المبيعات تعرض الصافي بعد خصم الخصومات
        public List<TopSellingProduct> GetTopSellingProducts(
            int userId,
            DateTime fromDate,
            DateTime toDate)
        {
            ValidateReportPermission(userId);
            ValidateDates(fromDate, toDate);

            var result = new List<TopSellingProduct>();

            string sql = @"
SELECT
    si.ProductId,
    si.ItemName AS ProductName,
    SUM(si.PackageQuantity) AS TotalQuantity,
    SUM(
        si.Total - COALESCE(si.DiscountAmount, 0)
    ) AS TotalSales,
    COUNT(DISTINCT si.InvoiceId) AS InvoiceCount
FROM SalesInvoiceItems si
INNER JOIN SalesInvoices inv
    ON inv.Id = si.InvoiceId
WHERE inv.Status = 'Completed'
AND inv.CreatedAt >= @FromDate
AND inv.CreatedAt < @ToDate
GROUP BY si.ProductId, si.ItemName
ORDER BY TotalQuantity DESC
LIMIT 10;";

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
                            result.Add(new TopSellingProduct
                            {
                                ProductId =
                                    Convert.ToInt32(reader["ProductId"]),

                                ProductName =
                                    reader["ProductName"].ToString(),

                                TotalQuantity =
                                    Convert.ToDouble(reader["TotalQuantity"]),

                                TotalSales =
                                    Convert.ToDouble(reader["TotalSales"]),

                                InvoiceCount =
                                    Convert.ToInt32(reader["InvoiceCount"])
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
