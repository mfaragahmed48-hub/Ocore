
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public class ExpiryAlertBatch
    {
        public int BatchId { get; set; }

        public int ProductId { get; set; }

        public string ProductCode { get; set; }

        public string ProductName { get; set; }

        public string BatchNumber { get; set; }

        public DateTime ExpiryDate { get; set; }

        public string ExpiryDateText
        {
            get
            {
                return ExpiryDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);
            }
        }

        public double Quantity { get; set; }

        public int DaysRemaining { get; set; }

        public string StatusText
        {
            get
            {
                if (DaysRemaining < 0)
                    return "منتهي الصلاحية";

                if (DaysRemaining == 0)
                    return "ينتهي اليوم";

                if (DaysRemaining <= 30)
                    return "خلال 30 يومًا";

                if (DaysRemaining <= 60)
                    return "خلال 60 يومًا";

                return "خلال 90 يومًا";
            }
        }

        public string DaysRemainingText
        {
            get
            {
                if (DaysRemaining < 0)
                    return "منتهي منذ " +
                           Math.Abs(DaysRemaining) + " يوم";

                if (DaysRemaining == 0)
                    return "ينتهي اليوم";

                return "متبقي " + DaysRemaining + " يوم";
            }
        }
    }

    public class ExpiryAlertService
    {
        private readonly string connectionString;

        private readonly PermissionService permissionService;

        public ExpiryAlertService()
        {
            var database = new DatabaseManager();

            database.InitializeDatabase();

            connectionString =
                "Data Source=" +
                database.GetDatabasePath() +
                ";Version=3;";

            permissionService = new PermissionService();
        }

        public List<ExpiryAlertBatch> GetExpiryAlerts(int userId)
        {
            var alerts = new List<ExpiryAlertBatch>();

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
                        "@UserId",
                        userId);

                    object result = command.ExecuteScalar();

                    if (result == null)
                    {
                        throw new UnauthorizedAccessException(
                            "المستخدم غير موجود أو غير نشط.");
                    }

                    role = Convert.ToString(result);
                }

                if (!permissionService.HasPermission(
                    role,
                    "Inventory.View"))
                {
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية عرض تنبيهات صلاحية المخزون.");
                }

                DateTime today = DateTime.Today;

                DateTime lastAlertDate = today.AddDays(90);

                string sql = @"
SELECT
    b.Id AS BatchId,
    p.Id AS ProductId,
    COALESCE(p.ProductCode, '') AS ProductCode,
    p.Name AS ProductName,
    COALESCE(b.BatchNumber, '') AS BatchNumber,
    b.ExpiryDate,
    b.Quantity

FROM Batches b

INNER JOIN Products p
    ON p.Id = b.ProductId

WHERE
    p.IsArchived = 0

    AND b.Quantity > 0

    AND TRIM(COALESCE(b.ExpiryDate, '')) <> ''

    AND date(b.ExpiryDate) IS NOT NULL

    AND date(b.ExpiryDate) <= date(@LastAlertDate)

ORDER BY
    date(b.ExpiryDate) ASC,
    p.Name COLLATE NOCASE ASC,
    b.Id ASC;";

                using (var command = new SQLiteCommand(
                    sql,
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@LastAlertDate",
                        lastAlertDate.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture));

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string expiryText =
                                Convert.ToString(
                                    reader["ExpiryDate"]).Trim();

                            DateTime expiryDate;

                            if (!DateTime.TryParseExact(
                                expiryText,
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out expiryDate))
                            {
                                continue;
                            }

                            int daysRemaining =
                                (expiryDate.Date - today.Date).Days;

                            alerts.Add(new ExpiryAlertBatch
                            {
                                BatchId = Convert.ToInt32(
                                    reader["BatchId"]),

                                ProductId = Convert.ToInt32(
                                    reader["ProductId"]),

                                ProductCode = Convert.ToString(
                                    reader["ProductCode"]),

                                ProductName = Convert.ToString(
                                    reader["ProductName"]),

                                BatchNumber = Convert.ToString(
                                    reader["BatchNumber"]),

                                ExpiryDate = expiryDate,

                                Quantity = Convert.ToDouble(
                                    reader["Quantity"]),

                                DaysRemaining = daysRemaining
                            });
                        }
                    }
                }
            }

            return alerts;
        }
    }
}
