using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public sealed class InventoryProductTrackingService
    {
        private readonly string connectionString;

        public InventoryProductTrackingService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        /// <summary>
        /// يتحقق من أن المستخدم مدير نشط.
        /// لا نعتمد على صلاحية مرسلة من الواجهة فقط.
        /// </summary>
        public void EnsureManagerAccess(int managerId)
        {
            if (managerId <= 0)
                throw new UnauthorizedAccessException(
                    "تعذر التحقق من صلاحيات المستخدم.");

            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(
                "SELECT Role FROM Users WHERE Id=@I AND IsActive=1;",
                connection))
            {
                command.Parameters.AddWithValue("@I", managerId);
                connection.Open();

                object role = command.ExecuteScalar();

                if (role == null ||
                    !string.Equals(
                        Convert.ToString(role),
                        "Manager",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException(
                        "هذه الشاشة متاحة للمدير النشط فقط.");
                }
            }
        }

        /// <summary>
        /// يعرض إعدادات التتبع والأرصدة للمدير النشط فقط.
        /// </summary>
        public DataTable GetAllSettings(int managerId)
        {
            const string sql = @"
SELECT
    p.Id AS ProductId,
    p.ProductCode,
    p.Name AS ProductName,
    COALESCE(t.TrackBatchNumber, 0) AS TrackBatchNumber,
    COALESCE(t.TrackExpiryDate, 0) AS TrackExpiryDate,
    COALESCE(
        (SELECT SUM(b.Quantity)
         FROM Batches b
         WHERE b.ProductId = p.Id),
        0
    ) AS CurrentStock,
    COALESCE(t.UpdatedAt, '') AS UpdatedAt
FROM Products p
LEFT JOIN InventoryProductTracking t
    ON t.ProductId = p.Id
WHERE p.IsArchived = 0
ORDER BY p.Name;";

            var table = new DataTable();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction =
                    connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(
                        connection,
                        transaction,
                        managerId);

                    SeedMissingSettings(connection, transaction, null);

                    using (var command = new SQLiteCommand(
                        sql,
                        connection,
                        transaction))
                    using (var reader = command.ExecuteReader())
                    {
                        table.Load(reader);
                    }

                    transaction.Commit();
                }
            }

            return table;
        }

        public InventoryProductTrackingSettings GetSettings(int productId)
        {
            if (productId <= 0)
                throw new ArgumentOutOfRangeException("productId");

            const string sql = @"
SELECT
    p.Id AS ProductId,
    p.Name AS ProductName,
    COALESCE(t.TrackBatchNumber, 0) AS TrackBatchNumber,
    COALESCE(t.TrackExpiryDate, 0) AS TrackExpiryDate,
    COALESCE(
        (SELECT SUM(b.Quantity)
         FROM Batches b
         WHERE b.ProductId = p.Id),
        0
    ) AS CurrentStock
FROM Products p
LEFT JOIN InventoryProductTracking t
    ON t.ProductId = p.Id
WHERE p.Id = @P
  AND p.IsArchived = 0;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction =
                    connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    SeedMissingSettings(
                        connection,
                        transaction,
                        productId);

                    using (var command = new SQLiteCommand(
                        sql,
                        connection,
                        transaction))
                    {
                        command.Parameters.AddWithValue("@P", productId);

                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                                throw new InvalidOperationException(
                                    "الصنف غير موجود أو مؤرشف.");

                            var result = new InventoryProductTrackingSettings
                            {
                                ProductId = Convert.ToInt32(
                                    reader["ProductId"]),

                                ProductName = Convert.ToString(
                                    reader["ProductName"]),

                                TrackBatchNumber =
                                    Convert.ToInt32(
                                        reader["TrackBatchNumber"]) == 1,

                                TrackExpiryDate =
                                    Convert.ToInt32(
                                        reader["TrackExpiryDate"]) == 1,

                                CurrentStock = Convert.ToDouble(
                                    reader["CurrentStock"])
                            };

                            reader.Close();
                            transaction.Commit();

                            return result;
                        }
                    }
                }
            }
        }

        public void SaveSettings(
            int managerId,
            int productId,
            bool trackBatchNumber,
            bool trackExpiryDate,
            string reason,
            bool confirmedWithExistingStock)
        {
            if (managerId <= 0)
                throw new UnauthorizedAccessException(
                    "تعذر التحقق من المستخدم الحالي.");

            if (productId <= 0)
                throw new ArgumentOutOfRangeException("productId");

            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "سبب تغيير إعدادات التتبع مطلوب.",
                    "reason");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction =
                    connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(
                        connection,
                        transaction,
                        managerId);

                    SeedMissingSettings(
                        connection,
                        transaction,
                        productId);

                    string productName;
                    double currentStock;

                    using (var command = new SQLiteCommand(@"
SELECT
    p.Name,
    COALESCE(SUM(b.Quantity), 0) AS CurrentStock
FROM Products p
LEFT JOIN Batches b
    ON b.ProductId = p.Id
WHERE p.Id = @P
  AND p.IsArchived = 0
GROUP BY p.Id, p.Name;",
                        connection,
                        transaction))
                    {
                        command.Parameters.AddWithValue("@P", productId);

                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                                throw new InvalidOperationException(
                                    "الصنف غير موجود أو مؤرشف.");

                            productName = Convert.ToString(reader["Name"]);

                            currentStock = Convert.ToDouble(
                                reader["CurrentStock"]);
                        }
                    }

                    bool oldBatch;
                    bool oldExpiry;

                    using (var command = new SQLiteCommand(@"
SELECT
    TrackBatchNumber,
    TrackExpiryDate
FROM InventoryProductTracking
WHERE ProductId = @P;",
                        connection,
                        transaction))
                    {
                        command.Parameters.AddWithValue("@P", productId);

                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                                throw new InvalidOperationException(
                                    "تعذر قراءة إعدادات تتبع الصنف.");

                            oldBatch =
                                Convert.ToInt32(
                                    reader["TrackBatchNumber"]) == 1;

                            oldExpiry =
                                Convert.ToInt32(
                                    reader["TrackExpiryDate"]) == 1;
                        }
                    }

                    // لا نسجل تغييرًا إذا كانت الإعدادات كما هي بالفعل.
                    if (oldBatch == trackBatchNumber &&
                        oldExpiry == trackExpiryDate)
                    {
                        transaction.Commit();
                        return;
                    }

                    // لا نسمح بتغيير التتبع أثناء وجود جلسة جرد نشطة
                    // تحتوي على الصنف، حتى لو كان عنصر الصنف معتمدًا.
                    EnsureNoActiveInventoryAudit(
                        connection,
                        transaction,
                        productId);

                    if (currentStock > 0 &&
                        !confirmedWithExistingStock)
                    {
                        throw new InvalidOperationException(
                            "يوجد رصيد مخزني حالي للصنف (" +
                            currentStock.ToString(
                                "0.########",
                                CultureInfo.InvariantCulture) +
                            "). يجب تأكيد مراجعة أثر تغيير التتبع قبل الحفظ.");
                    }

                    string now = DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture);

                    // حفظ سجل تاريخي قبل تحديث الإعدادات.
                    using (var command = new SQLiteCommand(@"
INSERT INTO InventoryProductTrackingHistory
(
    ProductId,
    OldTrackBatchNumber,
    OldTrackExpiryDate,
    NewTrackBatchNumber,
    NewTrackExpiryDate,
    CurrentStockAtChange,
    ChangedBy,
    Reason,
    CreatedAt
)
VALUES
(
    @P,
    @OB,
    @OE,
    @NB,
    @NE,
    @Q,
    @U,
    @R,
    @D
);",
                        connection,
                        transaction))
                    {
                        command.Parameters.AddWithValue("@P", productId);
                        command.Parameters.AddWithValue("@OB", oldBatch ? 1 : 0);
                        command.Parameters.AddWithValue("@OE", oldExpiry ? 1 : 0);
                        command.Parameters.AddWithValue("@NB", trackBatchNumber ? 1 : 0);
                        command.Parameters.AddWithValue("@NE", trackExpiryDate ? 1 : 0);
                        command.Parameters.AddWithValue("@Q", currentStock);
                        command.Parameters.AddWithValue("@U", managerId);
                        command.Parameters.AddWithValue("@R", reason.Trim());
                        command.Parameters.AddWithValue("@D", now);

                        command.ExecuteNonQuery();
                    }

                    // تحديث إعدادات التتبع بعد تسجيل التاريخ.
                    using (var command = new SQLiteCommand(@"
INSERT INTO InventoryProductTracking
(
    ProductId,
    TrackBatchNumber,
    TrackExpiryDate,
    CreatedBy,
    CreatedAt,
    UpdatedBy,
    UpdatedAt
)
VALUES
(
    @P,
    @B,
    @E,
    @U,
    @D,
    @U,
    @D
)
ON CONFLICT(ProductId) DO UPDATE SET
    TrackBatchNumber = excluded.TrackBatchNumber,
    TrackExpiryDate = excluded.TrackExpiryDate,
    UpdatedBy = excluded.UpdatedBy,
    UpdatedAt = excluded.UpdatedAt;",
                        connection,
                        transaction))
                    {
                        command.Parameters.AddWithValue("@P", productId);
                        command.Parameters.AddWithValue("@B", trackBatchNumber ? 1 : 0);
                        command.Parameters.AddWithValue("@E", trackExpiryDate ? 1 : 0);
                        command.Parameters.AddWithValue("@U", managerId);
                        command.Parameters.AddWithValue("@D", now);

                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }

        private static void SeedMissingSettings(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int? productId)
        {
            string filter = productId.HasValue
                ? "AND p.Id = @P"
                : string.Empty;

            string sql = @"
INSERT OR IGNORE INTO InventoryProductTracking
(
    ProductId,
    TrackBatchNumber,
    TrackExpiryDate,
    CreatedAt,
    UpdatedAt
)
SELECT
    p.Id,
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM Batches b
            WHERE b.ProductId = p.Id
              AND TRIM(COALESCE(b.BatchNumber, '')) <> ''
        )
        THEN 1 ELSE 0
    END,
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM Batches b
            WHERE b.ProductId = p.Id
              AND TRIM(COALESCE(b.ExpiryDate, '')) <> ''
        )
        THEN 1 ELSE 0
    END,
    strftime('%Y-%m-%d %H:%M:%S', 'now', 'localtime'),
    strftime('%Y-%m-%d %H:%M:%S', 'now', 'localtime')
FROM Products p
WHERE p.IsArchived = 0
" + filter + ";";

            using (var command = new SQLiteCommand(
                sql,
                connection,
                transaction))
            {
                if (productId.HasValue)
                    command.Parameters.AddWithValue("@P", productId.Value);

                command.ExecuteNonQuery();
            }
        }

        private static void EnsureNoActiveInventoryAudit(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int productId)
        {
            const string sql = @"
SELECT COUNT(*)
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s
    ON s.Id = i.SessionId
WHERE i.ProductId = @P
  AND s.Status IN ('Open', 'Submitted', 'PartiallyApproved')

UNION ALL

SELECT COUNT(*)
FROM InventoryCountPendingMovements m
INNER JOIN InventoryCountSessions s
    ON s.Id = m.SessionId
WHERE m.ProductId = @P
  AND m.ProcessingStatus IN ('Pending', 'Failed')
  AND s.Status IN ('Open', 'Submitted', 'PartiallyApproved');";

            using (var command = new SQLiteCommand(
                sql,
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@P", productId);

                using (var reader = command.ExecuteReader())
                {
                    int activeCount = 0;

                    while (reader.Read())
                        activeCount += Convert.ToInt32(reader[0]);

                    if (activeCount > 0)
                    {
                        throw new InvalidOperationException(
                            "لا يمكن تغيير نوع تتبع الصنف أثناء وجود جلسة جرد نشطة " +
                            "أو حركات جرد معلقة أو فاشلة مرتبطة به. " +
                            "يجب إنهاء الجرد ومعالجة الحركات أولًا.");
                    }
                }
            }
        }

        private static void EnsureManager(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId)
        {
            using (var command = new SQLiteCommand(
                "SELECT Role FROM Users WHERE Id=@I AND IsActive=1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@I", userId);

                object role = command.ExecuteScalar();

                if (role == null ||
                    !string.Equals(
                        Convert.ToString(role),
                        "Manager",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException(
                        "هذه العملية للمدير النشط فقط.");
                }
            }
        }
    }

    public sealed class InventoryProductTrackingSettings
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; }

        public bool TrackBatchNumber { get; set; }

        public bool TrackExpiryDate { get; set; }

        public double CurrentStock { get; set; }
    }
}