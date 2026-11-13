
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public class BatchService
    {
        private readonly string connectionString;
        private readonly PermissionService permissionService;

        public BatchService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";

            permissionService = new PermissionService();
        }

        public List<BatchInfo> GetProductBatches(
            int userId,
            int productId)
        {
            var batches = new List<BatchInfo>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(connection, userId);

                if (!permissionService.HasPermission(role, "Inventory.View"))
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية عرض المخزون.");

                using (var command = new SQLiteCommand(
                    @"SELECT Id, BatchNumber, ExpiryDate,
                             PurchasePrice, Quantity
                      FROM Batches
                      WHERE ProductId = @ProductId
                      ORDER BY ExpiryDate ASC, Id ASC;",
                    connection))
                {
                    command.Parameters.AddWithValue("@ProductId", productId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            batches.Add(new BatchInfo
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                BatchNumber = reader["BatchNumber"] == DBNull.Value
                                    ? ""
                                    : reader["BatchNumber"].ToString(),
                                ExpiryDate = DateTime.ParseExact(
                                    reader["ExpiryDate"].ToString(),
                                    "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture),
                                PurchasePrice = Convert.ToDouble(
                                    reader["PurchasePrice"]),
                                Quantity = Convert.ToDouble(
                                    reader["Quantity"])
                            });
                        }
                    }
                }
            }

            return batches;
        }

        // جلب التشغيلات المتاحة للبيع مرتبة حسب FEFO
        public List<BatchInfo> GetAvailableBatchesForSale(
            int userId,
            int productId)
        {
            if (productId <= 0)
                throw new ArgumentException("الصنف المحدد غير صحيح.");

            var batches = new List<BatchInfo>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(connection, userId);

                if (!permissionService.HasPermission(role, "Sales.Create"))
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية البيع.");

                string sql = @"
SELECT
    b.Id,
    b.BatchNumber,
    b.ExpiryDate,
    b.PurchasePrice,
    b.Quantity
FROM Batches b
INNER JOIN Products p ON p.Id = b.ProductId
WHERE b.ProductId = @ProductId
  AND p.IsArchived = 0
  AND b.Quantity > 0
  AND (
        TRIM(COALESCE(b.ExpiryDate, '')) = ''
        OR date(b.ExpiryDate) >= date('now', 'localtime')
      )
ORDER BY
    CASE
        WHEN TRIM(COALESCE(b.ExpiryDate, '')) = '' THEN 1
        ELSE 0
    END ASC,
    date(b.ExpiryDate) ASC,
    b.Id ASC;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ProductId", productId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string expiryText =
                                reader["ExpiryDate"] == DBNull.Value
                                    ? ""
                                    : reader["ExpiryDate"].ToString().Trim();

                            DateTime expiryDate;

                            if (string.IsNullOrWhiteSpace(expiryText))
                            {
                                // التشغيلات بلا تاريخ صلاحية تظهر في نهاية القائمة.
                                expiryDate = DateTime.MaxValue;
                            }
                            else if (!DateTime.TryParseExact(
                                expiryText,
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out expiryDate))
                            {
                                // تجاهل أي تاريخ غير صالح أو غير مطابق للتنسيق.
                                continue;
                            }

                            batches.Add(new BatchInfo
                            {
                                Id = Convert.ToInt32(reader["Id"]),

                                BatchNumber = reader["BatchNumber"] == DBNull.Value
                                    ? ""
                                    : reader["BatchNumber"].ToString(),

                                ExpiryDate = expiryDate,

                                PurchasePrice = Convert.ToDouble(
                                    reader["PurchasePrice"]),

                                Quantity = Convert.ToDouble(
                                    reader["Quantity"])
                            });
                        }
                    }
                }
            }

            return batches;
        }

        public void AddBatch(
            int userId,
            int productId,
            string batchNumber,
            DateTime expiryDate,
            double purchasePrice,
            double quantity)
        {
            if (productId <= 0)
                throw new ArgumentException("الصنف المحدد غير صحيح.");

            if (expiryDate.Date < DateTime.Today)
                throw new ArgumentException(
                    "لا يمكن إضافة تشغيلة منتهية الصلاحية.");

            if (purchasePrice < 0 ||
                double.IsNaN(purchasePrice) ||
                double.IsInfinity(purchasePrice))
                throw new ArgumentException("سعر الشراء غير صحيح.");

            if (quantity <= 0 ||
                double.IsNaN(quantity) ||
                double.IsInfinity(quantity))
                throw new ArgumentException("الكمية يجب أن تكون أكبر من صفر.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction(
                    IsolationLevel.Serializable))
                {
                    try
                    {
                        string username;

                        using (var command = new SQLiteCommand(
                            @"SELECT Username
                              FROM Users
                              WHERE Id = @UserId AND IsActive = 1;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);

                            object result = command.ExecuteScalar();

                            if (result == null)
                                throw new UnauthorizedAccessException(
                                    "المستخدم غير موجود أو غير نشط.");

                            username = result.ToString();
                        }

                        string role;

                        using (var command = new SQLiteCommand(
                            "SELECT Role FROM Users WHERE Id = @UserId;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);
                            role = Convert.ToString(command.ExecuteScalar());
                        }

                        if (!permissionService.HasPermission(
                            role, "Inventory.Edit"))
                            throw new UnauthorizedAccessException(
                                "ليس لديك صلاحية تعديل المخزون.");

                        string productName;

                        using (var command = new SQLiteCommand(
                            @"SELECT Name FROM Products
                              WHERE Id = @ProductId AND IsArchived = 0;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ProductId", productId);

                            object result = command.ExecuteScalar();

                            if (result == null)
                                throw new InvalidOperationException(
                                    "الصنف غير موجود أو مؤرشف.");

                            productName = result.ToString();
                        }

                        string expiryText = expiryDate.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture);

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
                                "@ExpiryDate", expiryText);

                            command.Parameters.AddWithValue(
                                "@PurchasePrice", purchasePrice);

                            command.Parameters.AddWithValue(
                                "@Quantity", quantity);

                            command.ExecuteNonQuery();
                        }

                        long batchId;

                        using (var command = new SQLiteCommand(
                            "SELECT last_insert_rowid();",
                            connection, transaction))
                        {
                            batchId = Convert.ToInt64(command.ExecuteScalar());
                        }

                        using (var command = new SQLiteCommand(
                            @"INSERT INTO AuditLogs
                              (UserId, Username, ActionType, EntityType,
                               EntityId, Details, DeviceName, CreatedAt)
                              VALUES
                              (@UserId, @Username, 'Create', 'Batch',
                               @EntityId, @Details, @DeviceName, @CreatedAt);",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);
                            command.Parameters.AddWithValue("@Username", username);
                            command.Parameters.AddWithValue("@EntityId", batchId);

                            command.Parameters.AddWithValue(
                                "@Details",
                                "إضافة تشغيلة للصنف " + productName +
                                " - رقم التشغيلة: " + batchNumber +
                                " - الصلاحية: " + expiryText +
                                " - الكمية: " +
                                quantity.ToString(
                                    "0.########",
                                    CultureInfo.InvariantCulture));

                            command.Parameters.AddWithValue(
                                "@DeviceName", Environment.MachineName);

                            command.Parameters.AddWithValue(
                                "@CreatedAt",
                                DateTime.Now.ToString(
                                    "yyyy-MM-dd HH:mm:ss",
                                    CultureInfo.InvariantCulture));

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
                    throw new UnauthorizedAccessException(
                        "المستخدم غير موجود أو غير نشط.");

                return result.ToString();
            }
        }
    }

    public class BatchInfo
    {
        public int Id { get; set; }
        public string BatchNumber { get; set; }
        public DateTime ExpiryDate { get; set; }
        public double PurchasePrice { get; set; }
        public double Quantity { get; set; }
    }
}
