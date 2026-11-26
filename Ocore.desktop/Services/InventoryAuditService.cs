
using DocumentFormat.OpenXml.ExtendedProperties;
using Ocore.desktop.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;

namespace Ocore.desktop.Services
{
    public sealed class InventoryAuditService
    {
        private readonly string connectionString;

        public InventoryAuditService()
        {
            var db = new DatabaseManager();
            db.InitializeDatabase();
            connectionString = "Data Source=" + db.GetDatabasePath() + ";Version=3;";
            EnsureReopenSnapshotStorage();
        }


        private void EnsureReopenSnapshotStorage()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var command = new SQLiteCommand(@"
CREATE TABLE IF NOT EXISTS InventoryCountReopenSnapshots
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CountItemId INTEGER NOT NULL,
    SessionId INTEGER NOT NULL,
    PreviousActualQuantity REAL NULL,
    PreviousActualPQuantity INTEGER NULL,
    PreviousActualUQuantity INTEGER NULL,
    PreviousUnitsPerPackageSnapshot INTEGER NULL,
    PreviousCountedBy INTEGER NULL,
    PreviousCountedAt TEXT NULL,
    PreviousManagerNote TEXT NULL,
    PreviousApprovedBy INTEGER NULL,
    PreviousApprovedAt TEXT NULL,
    PreviousAppliedQuantity REAL NULL,
    PreviousNetVarianceQuantity REAL NULL,
    PreviousClosedBy INTEGER NULL,
    PreviousClosedAt TEXT NULL,
    ReopenedBy INTEGER NOT NULL,
    ReopenedAt TEXT NOT NULL,
    RestoredBy INTEGER NULL,
    RestoredAt TEXT NULL,
    RestoreReason TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_InventoryCountReopenSnapshots_Active
ON InventoryCountReopenSnapshots(CountItemId,RestoredAt);", connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public int CreateSession(int managerId, string reason, IEnumerable<int> selectedBatchIds = null)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("سبب بدء الجرد مطلوب.");

            var selected = selectedBatchIds == null
                ? null
                : new HashSet<int>(selectedBatchIds.Where(x => x > 0));

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(connection, transaction, managerId);

                    string now = Now();
                    string number = "IC-" + DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

                    using (var command = new SQLiteCommand(@"
INSERT INTO InventoryCountSessions
(SessionNumber,ScopeType,Reason,Status,CreatedBy,CreatedAt)
VALUES(@N,@S,@R,'Open',@U,@D);", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@N", number);
                        command.Parameters.AddWithValue("@S", selected == null ? "Full" : "Selected");
                        command.Parameters.AddWithValue("@R", reason.Trim());
                        command.Parameters.AddWithValue("@U", managerId);
                        command.Parameters.AddWithValue("@D", now);
                        command.ExecuteNonQuery();
                    }

                    int sessionId = Convert.ToInt32(
                        new SQLiteCommand("SELECT last_insert_rowid();", connection, transaction).ExecuteScalar());

                    var targetsByKey = new Dictionary<string, Target>(StringComparer.Ordinal);

                    using (var command = new SQLiteCommand(@"
SELECT p.Id AS ProductId,p.Name AS ProductName,
       COALESCE(p.UnitName,'') AS UnitName,
       COALESCE(p.SmallUnitName,'') AS SmallUnitName,
       CASE WHEN COALESCE(p.UnitsPerPackage,1)>0
            THEN p.UnitsPerPackage ELSE 1 END AS UnitsPerPackage,
       COALESCE(t.TrackBatchNumber,0) AS TrackBatchNumber,
       COALESCE(t.TrackExpiryDate,0) AS TrackExpiryDate,
       b.Id AS BatchId,
       COALESCE(b.BatchNumber,'') AS BatchNumber,
       COALESCE(b.ExpiryDate,'') AS ExpiryDate,
       COALESCE(b.Quantity,0) AS Quantity,
       COALESCE(b.PurchasePrice,0) AS PurchasePrice,
       COALESCE(pi.CreatedAt,
           (SELECT MAX(al.CreatedAt)
            FROM AuditLogs al
            WHERE al.EntityType='Batch'
              AND al.EntityId=b.Id
              AND al.ActionType='Create'),'') AS ReceiptAt,
       COALESCE(pii.Id,b.Id,0) AS ReceiptItemId
FROM Products p
LEFT JOIN InventoryProductTracking t ON t.ProductId=p.Id
LEFT JOIN Batches b ON b.ProductId=p.Id
LEFT JOIN PurchaseInvoiceItems pii ON pii.BatchId=b.Id
LEFT JOIN PurchaseInvoices pi ON pi.Id=pii.InvoiceId
WHERE p.IsArchived=0
ORDER BY p.Id,
         COALESCE(pi.CreatedAt,
             (SELECT MAX(al.CreatedAt)
              FROM AuditLogs al
              WHERE al.EntityType='Batch'
                AND al.EntityId=b.Id
                AND al.ActionType='Create'),''),
         COALESCE(pii.Id,b.Id),b.Id;", connection, transaction))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int productId = Convert.ToInt32(reader["ProductId"]);
                            bool hasBatch = reader["BatchId"] != DBNull.Value;

                            if (!hasBatch)
                                continue;

                            string expiry = Convert.ToString(reader["ExpiryDate"]);
                            int batchId = Convert.ToInt32(reader["BatchId"]);
                            bool trackBatchNumber = Convert.ToInt32(reader["TrackBatchNumber"]) == 1;
                            bool trackExpiryDate = Convert.ToInt32(reader["TrackExpiryDate"]) == 1;

                            string targetType;
                            string targetKey;

                            if (trackBatchNumber)
                            {
                                targetType = "Batch";
                                targetKey = "B:" + batchId.ToString(CultureInfo.InvariantCulture);
                            }
                            else if (trackExpiryDate)
                            {
                                targetType = "ExpiryGroup";
                                string expiryKey = string.IsNullOrWhiteSpace(expiry)
                                    ? "~NONE~"
                                    : expiry.Trim();
                                targetKey = "E:" + productId.ToString(CultureInfo.InvariantCulture) + ":" + expiryKey;
                            }
                            else
                            {
                                targetType = "Product";
                                targetKey = "P:" + productId.ToString(CultureInfo.InvariantCulture);
                            }

                            Target target;

                            if (!targetsByKey.TryGetValue(targetKey, out target))
                            {
                                target = new Target
                                {
                                    ProductId = productId,
                                    Name = Convert.ToString(reader["ProductName"]),
                                    Unit = BuildUnitSnapshot(
                                        Convert.ToString(reader["UnitName"]),
                                        Convert.ToString(reader["SmallUnitName"]),
                                        Convert.ToInt32(reader["UnitsPerPackage"])),
                                    UnitsPerPackage = Convert.ToInt32(reader["UnitsPerPackage"]),
                                    TargetType = targetType,
                                    TargetKey = targetKey,
                                    Expiry = targetType == "Batch" || targetType == "ExpiryGroup" ? expiry : "",
                                    Batch = targetType == "Batch" ? Convert.ToString(reader["BatchNumber"]) : "",
                                    Batches = new List<TargetBatch>()
                                };
                                targetsByKey.Add(targetKey, target);
                            }

                            if (hasBatch)
                            {
                                var batch = new TargetBatch
                                {
                                    BatchId = Convert.ToInt32(reader["BatchId"]),
                                    BatchNumber = Convert.ToString(reader["BatchNumber"]),
                                    ExpiryDate = expiry,
                                    Quantity = Convert.ToDouble(reader["Quantity"]),
                                    PurchasePrice = Convert.ToDouble(reader["PurchasePrice"]),
                                    ReceiptAt = Convert.ToString(reader["ReceiptAt"]),
                                    ReceiptItemId = Convert.ToInt32(reader["ReceiptItemId"])
                                };

                                target.Batches.Add(batch);
                                target.Quantity += batch.Quantity;
                            }
                        }
                    }

                    var targets = targetsByKey.Values
                        .Where(x => selected == null || x.Batches.Any(b => selected.Contains(b.BatchId)))
                        .OrderBy(x => x.Name, StringComparer.CurrentCulture)
                        .ThenBy(x => x.TargetKey, StringComparer.Ordinal)
                        .ToList();

                    if (selected != null)
                    {
                        var includedBatchIds = new HashSet<int>(
                            targets.SelectMany(x => x.Batches).Select(x => x.BatchId));

                        var missingBatchIds = selected
                            .Where(x => !includedBatchIds.Contains(x))
                            .ToList();

                        if (missingBatchIds.Count > 0)
                        {
                            throw new InvalidOperationException(
                                "بعض التشغيلات المحددة غير موجودة أو لا تنتمي إلى أصناف قابلة للجرد؛ لم يتم إنشاء الجلسة.");
                        }
                    }
                    if (targets.Count == 0)
                        throw new InvalidOperationException("لا توجد أهداف مخزنية ضمن نطاق الجرد المحدد.");

                    foreach (var target in targets)
                    {
                        using (var check = new SQLiteCommand(@"
SELECT COUNT(*)
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE i.TargetKey=@K
  AND i.ItemStatus<>'Cancelled'
  AND s.Status IN ('Open','Submitted','PartiallyApproved');", connection, transaction))
                        {
                            check.Parameters.AddWithValue("@K", target.TargetKey);

                            if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                            {
                                throw new InvalidOperationException(
                                    "لا يمكن بدء الجرد: يوجد هدف مخزني ضمن جلسة أخرى مفتوحة أو قيد المراجعة.");
                            }
                        }
                    }

                    foreach (var target in targets)
                    {
                        TargetBatch latest = target.Batches
                            .OrderByDescending(x => x.ReceiptAt ?? "")
                            .ThenByDescending(x => x.ReceiptItemId)
                            .ThenByDescending(x => x.BatchId)
                            .FirstOrDefault();

                        using (var command = new SQLiteCommand(@"
INSERT INTO InventoryCountItems
(SessionId,ProductId,BatchId,TargetType,TargetKey,ProductNameSnapshot,
 BatchNumberSnapshot,ExpiryDateSnapshot,UnitSnapshot,BookQuantity,
 PurchasePriceSnapshot,UnitsPerPackageSnapshot,ItemStatus,CreatedAt)
VALUES(@S,@P,@B,@T,@K,@N,@BN,@E,@U,@Q,@C,@UP,'Pending',@D);", connection, transaction))
                        {
                            command.Parameters.AddWithValue("@S", sessionId);
                            command.Parameters.AddWithValue("@P", target.ProductId);
                            command.Parameters.AddWithValue("@B",
                                target.TargetType == "Batch" && target.Batches.Count == 1
                                    ? (object)target.Batches[0].BatchId
                                    : DBNull.Value);
                            command.Parameters.AddWithValue("@T", target.TargetType);
                            command.Parameters.AddWithValue("@K", target.TargetKey);
                            command.Parameters.AddWithValue("@N", target.Name ?? "");
                            command.Parameters.AddWithValue("@BN", target.Batch ?? "");
                            command.Parameters.AddWithValue("@E", target.Expiry ?? "");
                            command.Parameters.AddWithValue("@U", target.Unit ?? "");
                            command.Parameters.AddWithValue("@Q", target.Quantity);
                            command.Parameters.AddWithValue("@C", latest == null ? 0 : latest.PurchasePrice);
                            command.Parameters.AddWithValue("@UP", target.UnitsPerPackage > 0 ? target.UnitsPerPackage : 1);
                            command.Parameters.AddWithValue("@D", now);
                            command.ExecuteNonQuery();
                        }

                        int countItemId = Convert.ToInt32(
                            new SQLiteCommand("SELECT last_insert_rowid();", connection, transaction).ExecuteScalar());

                        foreach (var batch in target.Batches)
                        {
                            using (var snapshot = new SQLiteCommand(@"
INSERT INTO InventoryCountItemBatchSnapshots
(CountItemId,BatchId,BookQuantity,PurchasePriceSnapshot,
 BatchNumberSnapshot,ExpiryDateSnapshot,UnitSnapshot,
 ReceiptAtSnapshot,ReceiptItemIdSnapshot,CreatedAt)
VALUES(@I,@B,@Q,@C,@BN,@E,@U,@RA,@RI,@D);", connection, transaction))
                            {
                                snapshot.Parameters.AddWithValue("@I", countItemId);
                                snapshot.Parameters.AddWithValue("@B", batch.BatchId);
                                snapshot.Parameters.AddWithValue("@Q", batch.Quantity);
                                snapshot.Parameters.AddWithValue("@C", batch.PurchasePrice);
                                snapshot.Parameters.AddWithValue("@BN", batch.BatchNumber ?? "");
                                snapshot.Parameters.AddWithValue("@E", batch.ExpiryDate ?? "");
                                snapshot.Parameters.AddWithValue("@U", target.Unit ?? "");
                                snapshot.Parameters.AddWithValue("@RA", batch.ReceiptAt ?? "");
                                snapshot.Parameters.AddWithValue("@RI", batch.ReceiptItemId);
                                snapshot.Parameters.AddWithValue("@D", now);
                                snapshot.ExecuteNonQuery();
                            }
                        }
                    }

                    Event(connection, transaction, sessionId, null,
                        "SessionCreated", managerId,
                        "إنشاء " + number + "؛ السبب: " + reason.Trim() +
                        "؛ أهداف الجرد: " + targets.Count);

                    transaction.Commit();
                    return sessionId;
                }
            }
        }

        public void AssignItem(int managerId, int sessionId, int itemId, int employeeId)
        {
            throw new InvalidOperationException(
                "نظام الجرد الجديد ينفذه المدير مباشرة ولا يدعم إسناد البنود للموظفين.");
        }

        public string StartCounting(int userId, int sessionId, int itemId, int lockMinutes = 10)
        {
            if (lockMinutes < 1 || lockMinutes > 60)
                throw new ArgumentOutOfRangeException(nameof(lockMinutes));

            string token = Guid.NewGuid().ToString("N");

            using (var c = new SQLiteConnection(connectionString))
            {
                c.Open();

                using (var t = c.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureCounter(c, t, userId);
                    EnsureOpen(c, t, sessionId);

                    using (var cmd = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET LockUserId=@U,LockToken=@T,LockUntil=@Until,ItemStatus='Counting'
WHERE Id=@I AND SessionId=@S
  AND ItemStatus IN('Pending','Counting','RecountRequired')
  AND (AssignedUserId IS NULL OR AssignedUserId=@U)
  AND (LockUntil IS NULL OR LockUntil<@Now OR LockUserId=@U);", c, t))
                    {
                        cmd.Parameters.AddWithValue("@U", userId);
                        cmd.Parameters.AddWithValue("@T", token);
                        cmd.Parameters.AddWithValue("@Until",
                            DateTime.Now.AddMinutes(lockMinutes).ToString(
                                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                        cmd.Parameters.AddWithValue("@I", itemId);
                        cmd.Parameters.AddWithValue("@S", sessionId);
                        cmd.Parameters.AddWithValue("@Now", Now());

                        if (cmd.ExecuteNonQuery() != 1)
                        {
                            throw new InvalidOperationException(
                                "التشغيلة محجوزة لموظف آخر أو غير مسندة إليك.");
                        }
                    }

                    Event(c, t, sessionId, itemId,
                        "CountingStarted", userId, "بدء العد وحجز مؤقت.");

                    t.Commit();
                }
            }

            return token;
        }

        public void SaveCount(int userId, int sessionId, int itemId, string token, double quantity)
        {
            if (quantity < 0 || double.IsNaN(quantity) || double.IsInfinity(quantity))
                throw new ArgumentException("الكمية الفعلية غير صحيحة.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureCounter(connection, transaction, userId);
                    EnsureOpen(connection, transaction, sessionId);

                    double? oldQuantity = null;
                    string oldStatus;

                    using (var command = new SQLiteCommand(@"
SELECT i.ActualQuantity,i.ItemStatus,
       COALESCE(p.UnitsPerPackage,1) AS UnitsPerPackage,
       COALESCE(p.SmallUnitName,'') AS SmallUnitName
FROM InventoryCountItems i
INNER JOIN Products p ON p.Id=i.ProductId
WHERE i.Id=@I AND i.SessionId=@S
  AND i.LockUserId=@U AND i.LockToken=@T
  AND i.LockUntil>=@N;", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@I", itemId);
                        command.Parameters.AddWithValue("@S", sessionId);
                        command.Parameters.AddWithValue("@U", userId);
                        command.Parameters.AddWithValue("@T", token ?? "");
                        command.Parameters.AddWithValue("@N", Now());

                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                throw new InvalidOperationException(
                                    "انتهى حجز العد أو لم يعد صالحًا. ابدأ العد مجددًا.");
                            }

                            oldStatus = Convert.ToString(reader["ItemStatus"]);

                            if (oldStatus != "Counting" && oldStatus != "RecountRequired")
                            {
                                throw new InvalidOperationException(
                                    "حالة البند لا تسمح بحفظ العد. أعد تحميل البند وابدأ العد من جديد.");
                            }

                            if (reader["ActualQuantity"] != DBNull.Value)
                                oldQuantity = Convert.ToDouble(reader["ActualQuantity"]);

                            int unitsPerPackage = Convert.ToInt32(reader["UnitsPerPackage"]);

                            if (unitsPerPackage < 1)
                                unitsPerPackage = 1;

                            double smallUnitEquivalent = quantity * unitsPerPackage;

                            if (double.IsNaN(smallUnitEquivalent) ||
                                double.IsInfinity(smallUnitEquivalent) ||
                                Math.Abs(smallUnitEquivalent - Math.Round(smallUnitEquivalent)) > 0.000001)
                            {
                                string smallUnitName = Convert.ToString(reader["SmallUnitName"]);

                                string unitLabel = string.IsNullOrWhiteSpace(smallUnitName)
                                    ? "U"
                                    : smallUnitName + " (U)";

                                throw new ArgumentException(
                                    "الكمية يجب أن تمثل عددًا صحيحًا من " +
                                    unitLabel + "؛ معامل التحويل هو " +
                                    unitsPerPackage.ToString(CultureInfo.InvariantCulture) +
                                    " لكل وحدة P.");
                            }
                        }
                    }

                    using (var command = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET ActualQuantity=@Q,CountedBy=@U,CountedAt=@N,
    ItemStatus='Counted',LockUserId=NULL,LockToken=NULL,LockUntil=NULL
WHERE Id=@I AND SessionId=@S
  AND LockUserId=@U AND LockToken=@T AND LockUntil>=@N
  AND ItemStatus IN ('Counting','RecountRequired');", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Q", quantity);
                        command.Parameters.AddWithValue("@U", userId);
                        command.Parameters.AddWithValue("@N", Now());
                        command.Parameters.AddWithValue("@I", itemId);
                        command.Parameters.AddWithValue("@S", sessionId);
                        command.Parameters.AddWithValue("@T", token ?? "");

                        if (command.ExecuteNonQuery() != 1)
                        {
                            throw new InvalidOperationException(
                                "تعذر حفظ العد؛ ربما انتهى الحجز أو تغيرت حالة البند.");
                        }
                    }

                    Revision(connection, transaction, itemId, oldQuantity, quantity,
                        oldStatus, "Counted", userId, "حفظ نتيجة العد");

                    Event(connection, transaction, sessionId, itemId,
                        "CountSaved", userId,
                        "الكمية الفعلية: " +
                        quantity.ToString("0.########", CultureInfo.InvariantCulture));

                    transaction.Commit();
                }
            }
        }

        public void SaveCount(
            int managerId, int sessionId, int itemId, int actualPQuantity, int actualUQuantity)
        {
            if (actualPQuantity < 0 || actualUQuantity < 0)
                throw new ArgumentException("الكميات الفعلية لا يمكن أن تكون سالبة.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(connection, transaction, managerId);
                    EnsureOpen(connection, transaction, sessionId);
                    int unitsPerPackage;
                    double? oldQuantity = null;
                    int? oldP = null;
                    int? oldU = null;
                    string oldStatus;

                    using (var command = new SQLiteCommand(@"
SELECT i.ActualQuantity,i.ActualPQuantity,i.ActualUQuantity,i.ItemStatus,
       COALESCE(NULLIF(i.UnitsPerPackageSnapshot,0),p.UnitsPerPackage,1) AS UnitsPerPackage
FROM InventoryCountItems i
INNER JOIN Products p ON p.Id=i.ProductId
WHERE i.Id=@I AND i.SessionId=@S;", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@I", itemId);
                        command.Parameters.AddWithValue("@S", sessionId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                                throw new InvalidOperationException("بند الجرد غير موجود في الجلسة المحددة.");
                            oldStatus = Convert.ToString(reader["ItemStatus"]);
                            if (oldStatus != "Pending" && oldStatus != "Counting" &&
                                oldStatus != "RecountRequired" && oldStatus != "Counted")
                                throw new InvalidOperationException("حالة البند لا تسمح بتسجيل العد.");
                            unitsPerPackage = Convert.ToInt32(reader["UnitsPerPackage"]);
                            if (unitsPerPackage < 1)
                                throw new InvalidOperationException("معامل تحويل الوحدات للصنف غير صحيح.");
                            if (reader["ActualQuantity"] != DBNull.Value)
                                oldQuantity = Convert.ToDouble(reader["ActualQuantity"]);
                            if (reader["ActualPQuantity"] != DBNull.Value)
                                oldP = Convert.ToInt32(reader["ActualPQuantity"]);
                            if (reader["ActualUQuantity"] != DBNull.Value)
                                oldU = Convert.ToInt32(reader["ActualUQuantity"]);
                        }
                    }

                    if (actualUQuantity >= unitsPerPackage)
                        throw new ArgumentException(
                            "عدد وحدات U يجب أن يكون أقل من معامل التحويل (" +
                            unitsPerPackage.ToString(CultureInfo.InvariantCulture) + ").");
                    double actualQuantity = actualPQuantity + (actualUQuantity / (double)unitsPerPackage);
                    string now = Now();

                    using (var command = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET ActualQuantity=@Q,ActualPQuantity=@P,ActualUQuantity=@U,
    UnitsPerPackageSnapshot=@UP,CountedBy=@A,CountedAt=@N,
    ItemStatus='Counted',LockUserId=NULL,LockToken=NULL,LockUntil=NULL
WHERE Id=@I AND SessionId=@S
  AND ItemStatus IN ('Pending','Counting','RecountRequired','Counted');",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Q", actualQuantity);
                        command.Parameters.AddWithValue("@P", actualPQuantity);
                        command.Parameters.AddWithValue("@U", actualUQuantity);
                        command.Parameters.AddWithValue("@UP", unitsPerPackage);
                        command.Parameters.AddWithValue("@A", managerId);
                        command.Parameters.AddWithValue("@N", now);
                        command.Parameters.AddWithValue("@I", itemId);
                        command.Parameters.AddWithValue("@S", sessionId);
                        if (command.ExecuteNonQuery() != 1)
                            throw new InvalidOperationException("تعذر حفظ نتيجة العد.");
                    }

                    RevisionPAndU(connection, transaction, itemId,
                        oldQuantity, actualQuantity, oldP, oldU,
                        actualPQuantity, actualUQuantity,
                        oldStatus, "Counted", managerId, "حفظ العد الفعلي بوحدتي P وU");
                    Event(connection, transaction, sessionId, itemId, "CountSaved", managerId,
                        "الفعلي: " + actualPQuantity.ToString(CultureInfo.InvariantCulture) +
                        " P + " + actualUQuantity.ToString(CultureInfo.InvariantCulture) +
                        " U؛ معامل التحويل: " + unitsPerPackage.ToString(CultureInfo.InvariantCulture));
                    transaction.Commit();
                }
            }
        }

        public void SubmitSession(int userId, int sessionId)
        {
            using (var c = new SQLiteConnection(connectionString))
            {
                c.Open();

                using (var t = c.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureCounter(c, t, userId);
                    EnsureOpen(c, t, sessionId);

                    using (var cmd = new SQLiteCommand(@"
SELECT COUNT(*)
FROM InventoryCountItems
WHERE SessionId=@S
  AND ItemStatus NOT IN('Counted','Approved','Cancelled');", c, t))
                    {
                        cmd.Parameters.AddWithValue("@S", sessionId);

                        if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                            throw new InvalidOperationException(
                                "لا يمكن إرسال الجلسة قبل اكتمال العد أو إلغاء البنود المتبقية صراحةً.");
                    }

                    using (var cmd = new SQLiteCommand(@"
UPDATE InventoryCountSessions
SET Status='Submitted',SubmittedAt=@N
WHERE Id=@S
  AND Status IN('Open','PartiallyApproved','Submitted');", c, t))
                    {
                        cmd.Parameters.AddWithValue("@N", Now());
                        cmd.Parameters.AddWithValue("@S", sessionId);

                        if (cmd.ExecuteNonQuery() != 1)
                            throw new InvalidOperationException("تعذر إرسال الجلسة.");
                    }

                    Event(c, t, sessionId, null,
                        "SessionSubmitted", userId,
                        "إرسال الجلسة لمراجعة المدير.");

                    t.Commit();
                }
            }
        }

        public void CancelItems(
            int managerId,
            int sessionId,
            IEnumerable<int> itemIds,
            string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("سبب إلغاء البنود مطلوب.", "reason");

            var ids = (itemIds ?? Enumerable.Empty<int>()).ToList();

            if (ids.Count == 0)
                throw new ArgumentException("حدد بندًا واحدًا على الأقل للإلغاء.");

            if (ids.Any(x => x <= 0) || ids.Distinct().Count() != ids.Count)
                throw new ArgumentException("توجد أرقام بنود غير صالحة أو مكررة.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction =
                    connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(connection, transaction, managerId);
                    EnsureOpen(connection, transaction, sessionId);

                    var affectedProducts = new SortedSet<int>();

                    foreach (int itemId in ids)
                    {
                        int productId;
                        double? actualQuantity;
                        string oldStatus;

                        using (var command = new SQLiteCommand(@"
SELECT ProductId,ActualQuantity,ItemStatus
FROM InventoryCountItems
WHERE Id=@I AND SessionId=@S;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@I", itemId);
                            command.Parameters.AddWithValue("@S", sessionId);

                            using (var reader = command.ExecuteReader())
                            {
                                if (!reader.Read())
                                    throw new InvalidOperationException(
                                        "أحد البنود المحددة غير موجود أو لا يتبع الجلسة.");

                                productId = Convert.ToInt32(reader["ProductId"]);
                                oldStatus = Convert.ToString(reader["ItemStatus"]);

                                actualQuantity = reader["ActualQuantity"] == DBNull.Value
                                    ? (double?)null
                                    : Convert.ToDouble(reader["ActualQuantity"]);
                            }
                        }

                        if (oldStatus == "Approved" || oldStatus == "Cancelled")
                            throw new InvalidOperationException(
                                "لا يمكن إلغاء بند معتمد أو ملغى مسبقًا.");

                        if (oldStatus != "Pending" &&
                            oldStatus != "Counting" &&
                            oldStatus != "Counted" &&
                            oldStatus != "Review" &&
                            oldStatus != "RecountRequired")
                        {
                            throw new InvalidOperationException(
                                "حالة أحد البنود لا تسمح بالإلغاء.");
                        }

                        using (var command = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET ItemStatus='Cancelled',
    ManagerNote=@R,
    LockUserId=NULL,
    LockToken=NULL,
    LockUntil=NULL
WHERE Id=@I AND SessionId=@S
  AND ItemStatus IN
      ('Pending','Counting','Counted','Review','RecountRequired');",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@R", reason.Trim());
                            command.Parameters.AddWithValue("@I", itemId);
                            command.Parameters.AddWithValue("@S", sessionId);

                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException(
                                    "تغيرت حالة أحد البنود أثناء الإلغاء؛ لم يتم إلغاء أي بند.");
                        }

                        Revision(
                            connection, transaction, itemId,
                            actualQuantity, actualQuantity,
                            oldStatus, "Cancelled", managerId, reason.Trim());

                        Event(
                            connection, transaction, sessionId, itemId,
                            "ItemCancelled", managerId, reason.Trim());

                        affectedProducts.Add(productId);
                    }

                    // نعالج الحركات بعد إلغاء كل الأهداف المحددة،
                    // حتى لا تتحرك أرصدة أثناء تطبيق جزء من العملية.
                    foreach (int productId in affectedProducts)
                    {
                        ProcessPendingMovementsForProduct(
                            connection, transaction, productId, managerId);
                    }

                    transaction.Commit();
                }
            }
        }

        public void ApproveItem(int managerId, int sessionId, int itemId, string note = "")
        {
            ApproveItems(managerId, sessionId, new[] { itemId }, note);
        }

        public void ApproveItems(int managerId, int sessionId, IEnumerable<int> itemIds, string note = "")
        {
            var ids = (itemIds ?? Enumerable.Empty<int>()).ToList();

            if (ids.Count == 0)
                throw new ArgumentException("حدد بندًا واحدًا على الأقل للاعتماد.");

            if (ids.Any(x => x <= 0))
                throw new ArgumentException("توجد أرقام بنود غير صالحة.");

            if (ids.Distinct().Count() != ids.Count)
                throw new ArgumentException("لا يمكن اعتماد البند نفسه أكثر من مرة في العملية الواحدة.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(connection, transaction, managerId);
                    EnsureReview(connection, transaction, sessionId);
                    ValidateItemsForApproval(connection, transaction, sessionId, ids, note);

                    var affectedProductIds = new SortedSet<int>();

                    foreach (int itemId in ids)
                    {
                        affectedProductIds.Add(
                            ApproveItemWithinTransaction(
                                connection, transaction, managerId, sessionId, itemId, note));
                    }

                    foreach (int productId in affectedProductIds)
                    {
                        ProcessPendingMovementsForProduct(
                            connection, transaction, productId, managerId);
                    }

                    transaction.Commit();
                }
            }
        }


        private static void ValidateItemsForApproval(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int sessionId,
            IList<int> itemIds,
            string note)
        {
            const double epsilon = 0.00000001;

            foreach (int itemId in itemIds)
            {
                double bookQuantity;
                double actualQuantity;
                double? appliedQuantity;
                string itemStatus;

                using (var command = new SQLiteCommand(@"
SELECT BookQuantity,ActualQuantity,AppliedQuantity,ItemStatus,
       LockUserId,LockToken,LockUntil
FROM InventoryCountItems
WHERE Id=@I AND SessionId=@S;", connection, transaction))
                {
                    command.Parameters.AddWithValue("@I", itemId);
                    command.Parameters.AddWithValue("@S", sessionId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new InvalidOperationException(
                                "بند الجرد غير موجود أو لا يتبع الجلسة المحددة.");

                        if (reader["ActualQuantity"] == DBNull.Value)
                            throw new InvalidOperationException(
                                "لا توجد كمية فعلية في أحد البنود المحددة.");

                        itemStatus = Convert.ToString(reader["ItemStatus"]);

                        if (itemStatus != "Counted" && itemStatus != "Review")
                            throw new InvalidOperationException(
                                "أحد البنود المحددة لا يقبل الاعتماد؛ لم يتم اعتماد أي بند.");

                        if (reader["LockUserId"] != DBNull.Value ||
                            reader["LockToken"] != DBNull.Value ||
                            reader["LockUntil"] != DBNull.Value)
                        {
                            throw new InvalidOperationException(
                                "أحد البنود ما زال يحمل قفل عد؛ حرر القفل أو أعد العد قبل الاعتماد.");
                        }

                        bookQuantity = Convert.ToDouble(reader["BookQuantity"]);
                        actualQuantity = Convert.ToDouble(reader["ActualQuantity"]);

                        appliedQuantity = reader["AppliedQuantity"] == DBNull.Value
                            ? (double?)null
                            : Convert.ToDouble(reader["AppliedQuantity"]);
                    }
                }

                double difference = Math.Round(
                    actualQuantity - (appliedQuantity ?? bookQuantity),
                    8,
                    MidpointRounding.AwayFromZero);

                if (Math.Abs(difference) > epsilon &&
                    string.IsNullOrWhiteSpace(note))
                {
                    throw new InvalidOperationException(
                        "يوجد عجز أو زيادة. اكتب سبب الفرق في ملاحظة المدير قبل الاعتماد.");
                }
            }
        }


        private int ApproveItemWithinTransaction(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int managerId,
            int sessionId,
            int itemId,
            string note)
        {
            int productId;
            int? batchId;
            string targetType;
            double bookQuantity;
            double actualQuantity;
            double itemCostSnapshot;
            double? appliedQuantity;
            double netVarianceQuantity;
            string itemStatus;

            using (var command = new SQLiteCommand(@"
SELECT ProductId,BatchId,COALESCE(TargetType,'Batch') AS TargetType,
       BookQuantity,ActualQuantity,PurchasePriceSnapshot,
       AppliedQuantity,NetVarianceQuantity,ItemStatus
FROM InventoryCountItems
WHERE Id=@I AND SessionId=@S;", connection, transaction))
            {
                command.Parameters.AddWithValue("@I", itemId);
                command.Parameters.AddWithValue("@S", sessionId);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        throw new InvalidOperationException("بند الجرد غير موجود.");

                    if (reader["ActualQuantity"] == DBNull.Value)
                        throw new InvalidOperationException("لا توجد كمية فعلية.");

                    itemStatus = Convert.ToString(reader["ItemStatus"]);

                    if (itemStatus != "Counted" && itemStatus != "Review")
                        throw new InvalidOperationException("البند لا يقبل الاعتماد.");

                    productId = Convert.ToInt32(reader["ProductId"]);

                    batchId = reader["BatchId"] == DBNull.Value
                        ? (int?)null
                        : Convert.ToInt32(reader["BatchId"]);

                    targetType = Convert.ToString(reader["TargetType"]);
                    bookQuantity = Convert.ToDouble(reader["BookQuantity"]);
                    actualQuantity = Convert.ToDouble(reader["ActualQuantity"]);
                    itemCostSnapshot = Convert.ToDouble(reader["PurchasePriceSnapshot"]);

                    appliedQuantity = reader["AppliedQuantity"] == DBNull.Value
                        ? (double?)null
                        : Convert.ToDouble(reader["AppliedQuantity"]);

                    netVarianceQuantity = Convert.ToDouble(reader["NetVarianceQuantity"]);
                }
            }

            double difference = Math.Round(
                actualQuantity - (appliedQuantity ?? bookQuantity),
                8, MidpointRounding.AwayFromZero);

            double finalVariance = Math.Round(
                netVarianceQuantity + difference, 8, MidpointRounding.AwayFromZero);

            const double epsilon = 0.00000001;

            var layers = new List<CostLayer>();

            using (var command = new SQLiteCommand(@"
SELECT s.BatchId,s.BookQuantity,s.PurchasePriceSnapshot,
       s.ReceiptAtSnapshot AS ReceiptAt,
       s.ReceiptItemIdSnapshot AS ReceiptItemId
FROM InventoryCountItemBatchSnapshots s
WHERE s.CountItemId=@I
ORDER BY s.ReceiptAtSnapshot DESC,
         s.ReceiptItemIdSnapshot DESC,
         s.BatchId DESC;", connection, transaction))
            {
                command.Parameters.AddWithValue("@I", itemId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        layers.Add(new CostLayer
                        {
                            BatchId = Convert.ToInt32(reader["BatchId"]),
                            BookQuantity = Convert.ToDouble(reader["BookQuantity"]),
                            UnitCost = Convert.ToDouble(reader["PurchasePriceSnapshot"]),
                            ReceiptAt = Convert.ToString(reader["ReceiptAt"]),
                            ReceiptItemId = Convert.ToInt32(reader["ReceiptItemId"])
                        });
                    }
                }
            }

            if (layers.Count == 0 && batchId.HasValue)
            {
                layers.Add(new CostLayer
                {
                    BatchId = batchId.Value,
                    UnitCost = itemCostSnapshot,
                    ReceiptAt = "",
                    ReceiptItemId = 0
                });
            }

            if (Math.Abs(difference) > epsilon)
            {
                if (string.IsNullOrWhiteSpace(note))
                {
                    throw new InvalidOperationException(
                        "يوجد عجز أو زيادة. اكتب سبب الفرق في ملاحظة المدير قبل الاعتماد.");
                }

                CostLayer latestLayer;
                if (string.Equals(targetType, "Batch", StringComparison.OrdinalIgnoreCase))
                {
                    if (!batchId.HasValue)
                        throw new InvalidOperationException("بند التشغيلة لا يحتوي على رقم تشغيلة صالح.");
                    latestLayer = layers.FirstOrDefault(x => x.BatchId == batchId.Value);
                    if (latestLayer == null)
                    {
                        latestLayer = new CostLayer
                        {
                            BatchId = batchId.Value,
                            UnitCost = itemCostSnapshot,
                            ReceiptAt = "",
                            ReceiptItemId = 0
                        };
                    }
                }
                else
                {
                    // Keep the historical behavior for legacy grouped sessions.
                    latestLayer = layers
                        .OrderByDescending(x => x.ReceiptAt ?? "")
                        .ThenByDescending(x => x.ReceiptItemId)
                        .ThenByDescending(x => x.BatchId)
                        .FirstOrDefault();
                }

                if (latestLayer == null)
                {
                    throw new InvalidOperationException(
                        "لا يوجد استلام مسجل داخل هدف الجرد لتقييم الفرق. لا يمكن اعتماد الفرق دون مصدر تكلفة موثق.");
                }

                double currentLayerQuantity;

                using (var command = new SQLiteCommand(
                    "SELECT Quantity FROM Batches WHERE Id=@B;",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@B", latestLayer.BatchId);

                    object value = command.ExecuteScalar();

                    if (value == null || value == DBNull.Value)
                        throw new InvalidOperationException("آخر استلام داخل هدف الجرد غير موجود.");

                    currentLayerQuantity = Convert.ToDouble(value);
                }

                double adjustedQuantity = Math.Round(
                    currentLayerQuantity + difference,
                    8,
                    MidpointRounding.AwayFromZero);

                if (adjustedQuantity < -epsilon)
                {
                    throw new InvalidOperationException(
                        "كمية العجز أكبر من رصيد آخر استلام داخل هدف الجرد. لم يتم تغيير المخزون؛ يلزم مراجعة توزيع الفرق قبل الاعتماد.");
                }

                using (var command = new SQLiteCommand(
                    "UPDATE Batches SET Quantity=@Q WHERE Id=@B;",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@Q", Math.Max(0, adjustedQuantity));
                    command.Parameters.AddWithValue("@B", latestLayer.BatchId);

                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException(
                            "تعذر تحديث رصيد آخر استلام داخل هدف الجرد.");
                }

                using (var command = new SQLiteCommand(@"
INSERT INTO InventoryCountItemCostDecisions
(SessionId,CountItemId,DifferenceQuantity,EvaluationMethod,UnitCost,
 SourceBatchId,Notes,Reason,ManagerId,CreatedAt)
VALUES(@S,@I,@D,'LastReceiptInTarget',@C,@B,@N,@R,@U,@T);",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@S", sessionId);
                    command.Parameters.AddWithValue("@I", itemId);
                    command.Parameters.AddWithValue("@D", difference);
                    command.Parameters.AddWithValue("@C", latestLayer.UnitCost);
                    command.Parameters.AddWithValue("@B", latestLayer.BatchId);
                    command.Parameters.AddWithValue("@N", note.Trim());
                    command.Parameters.AddWithValue("@R", note.Trim());
                    command.Parameters.AddWithValue("@U", managerId);
                    command.Parameters.AddWithValue("@T", Now());
                    command.ExecuteNonQuery();
                }
            }

            using (var command = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET ItemStatus='Approved',ApprovedBy=@U,ApprovedAt=@N,
    AppliedQuantity=@A,
    NetVarianceQuantity=COALESCE(NetVarianceQuantity,0)+@D,
    ManagerNote=@M
WHERE Id=@I AND SessionId=@S;", connection, transaction))
            {
                command.Parameters.AddWithValue("@U", managerId);
                command.Parameters.AddWithValue("@N", Now());
                command.Parameters.AddWithValue("@A", actualQuantity);
                command.Parameters.AddWithValue("@D", difference);
                command.Parameters.AddWithValue("@M", note ?? "");
                command.Parameters.AddWithValue("@I", itemId);
                command.Parameters.AddWithValue("@S", sessionId);

                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("تعذر تسجيل اعتماد بند الجرد.");
            }

            Revision(connection, transaction, itemId, actualQuantity, actualQuantity,
                itemStatus, "Approved", managerId,
                "اعتماد المدير؛ الرصيد الدفتري قبل الجرد: " +
                bookQuantity.ToString("0.########", CultureInfo.InvariantCulture));

            Event(connection, transaction, sessionId, itemId,
                "ItemApproved", managerId,
                "الهدف: " + targetType +
                "؛ الرصيد قبل: " + bookQuantity +
                "؛ بعد: " + actualQuantity +
                "؛ صافي فرق الجرد بعد استبعاد الحركات: " + finalVariance +
                "؛ تعديل هذا الاعتماد: " + difference +
                "؛ ملاحظة: " + (note ?? ""));

            using (var command = new SQLiteCommand(
                "UPDATE InventoryCountSessions SET Status='PartiallyApproved' WHERE Id=@S;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@S", sessionId);
                command.ExecuteNonQuery();
            }

            return productId;
        }

        public void RequestRecount(int managerId, int sessionId, int itemId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("سبب إعادة العد مطلوب.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(connection, transaction, managerId);
                    EnsureReview(connection, transaction, sessionId);

                    double? oldQuantity = null;
                    string oldStatus;

                    using (var command = new SQLiteCommand(@"
SELECT ActualQuantity,ItemStatus
FROM InventoryCountItems
WHERE Id=@I AND SessionId=@S;", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@I", itemId);
                        command.Parameters.AddWithValue("@S", sessionId);

                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                                throw new InvalidOperationException("بند الجرد غير موجود.");

                            oldStatus = Convert.ToString(reader["ItemStatus"]);

                            if (oldStatus != "Counted" &&
                                oldStatus != "Review" &&
                                oldStatus != "RecountRequired")
                            {
                                throw new InvalidOperationException(
                                    "لا يمكن طلب إعادة العد لهذا البند. البنود المعتمدة لا يعاد فتحها بهذه العملية.");
                            }

                            if (reader["ActualQuantity"] != DBNull.Value)
                                oldQuantity = Convert.ToDouble(reader["ActualQuantity"]);
                        }
                    }

                    using (var command = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET ItemStatus='RecountRequired',ManagerNote=@R,
    LockUserId=NULL,LockToken=NULL,LockUntil=NULL
WHERE Id=@I AND SessionId=@S
  AND ItemStatus IN ('Counted','Review','RecountRequired');",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@R", reason.Trim());
                        command.Parameters.AddWithValue("@I", itemId);
                        command.Parameters.AddWithValue("@S", sessionId);

                        if (command.ExecuteNonQuery() != 1)
                        {
                            throw new InvalidOperationException(
                                "تغيرت حالة البند أثناء طلب إعادة العد. أعد تحميل البيانات.");
                        }
                    }

                    Revision(connection, transaction, itemId, oldQuantity, oldQuantity,
                        oldStatus, "RecountRequired", managerId, reason.Trim());

                    Event(connection, transaction, sessionId, itemId,
                        "RecountRequested", managerId, reason.Trim());

                    transaction.Commit();
                }
            }
        }

        public void CloseSession(int managerId, int sessionId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(connection, transaction, managerId);

                    using (var command = new SQLiteCommand(@"
SELECT COUNT(*)
FROM InventoryCountItems
WHERE SessionId=@S
  AND ItemStatus NOT IN ('Approved','Cancelled');",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);

                        if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException(
                                "لا يمكن إغلاق الجلسة قبل اعتماد البنود المتبقية أو إلغائها صراحةً.");
                        }
                    }

                    using (var command = new SQLiteCommand(@"
SELECT COUNT(*)
FROM InventoryCountPendingMovements
WHERE SessionId=@S
  AND ProcessingStatus IN ('Pending','Failed');",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);

                        if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException(
                                "توجد حركات معلقة أو فاشلة. يجب معالجتها قبل إغلاق الجلسة.");
                        }
                    }

                    using (var command = new SQLiteCommand(@"
UPDATE InventoryCountSessions
SET Status='Closed',ClosedBy=@U,ClosedAt=@N
WHERE Id=@S AND Status IN('Open','Submitted','PartiallyApproved');",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@U", managerId);
                        command.Parameters.AddWithValue("@N", Now());
                        command.Parameters.AddWithValue("@S", sessionId);

                        if (command.ExecuteNonQuery() != 1)
                        {
                            throw new InvalidOperationException(
                                "حالة الجلسة لا تسمح بالإغلاق، أو أن الجلسة غير موجودة.");
                        }
                    }

                    Event(connection, transaction, sessionId, null,
                        "SessionClosed", managerId,
                        "إغلاق الجلسة بعد اعتماد البنود المتبقية أو إلغائها.");

                    transaction.Commit();
                }
            }
        }

        public void CancelSession(int managerId, int sessionId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("سبب الإلغاء مطلوب.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(connection, transaction, managerId);

                    using (var command = new SQLiteCommand(@"
SELECT COUNT(*)
FROM InventoryCountItems
WHERE SessionId=@S
  AND (
      ItemStatus='Approved'
      OR AppliedQuantity IS NOT NULL
      OR ABS(COALESCE(NetVarianceQuantity,0)) > 0.00000001
  );", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);

                        if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException(
                                "لا يمكن إلغاء جلسة سبق اعتماد أحد بنودها أو تطبيق تسوية مخزون بها.");
                        }
                    }

                    using (var command = new SQLiteCommand(@"
SELECT COUNT(*)
FROM InventoryCountPendingMovements
WHERE SessionId=@S
  AND ProcessingStatus IN ('Pending','Failed');",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);

                        if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                        {
                            throw new InvalidOperationException(
                                "لا يمكن إلغاء الجلسة لوجود حركات معلقة أو فاشلة. عالج الحركات أولاً.");
                        }
                    }

                    using (var command = new SQLiteCommand(@"
UPDATE InventoryCountSessions
SET Status='Cancelled',CancelledBy=@U,CancelledAt=@N,CancelReason=@R
WHERE Id=@S AND Status='Open';", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@U", managerId);
                        command.Parameters.AddWithValue("@N", Now());
                        command.Parameters.AddWithValue("@R", reason.Trim());
                        command.Parameters.AddWithValue("@S", sessionId);

                        if (command.ExecuteNonQuery() != 1)
                            throw new InvalidOperationException("يمكن إلغاء الجلسة المفتوحة فقط.");
                    }

                    using (var command = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET ItemStatus='Cancelled',
    LockUserId=NULL,LockToken=NULL,LockUntil=NULL
WHERE SessionId=@S AND ItemStatus<>'Approved';",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);
                        command.ExecuteNonQuery();
                    }

                    Event(connection, transaction, sessionId, null,
                        "SessionCancelled", managerId, reason.Trim());

                    transaction.Commit();
                }
            }
        }

        public void ReopenSelectedItems(
            int managerId, int sessionId, IEnumerable<int> itemIds, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("سبب إعادة المراجعة مطلوب.");

            var ids = new HashSet<int>(
                (itemIds ?? Enumerable.Empty<int>()).Where(x => x > 0));

            if (ids.Count == 0)
                throw new ArgumentException("حدد بندًا واحدًا على الأقل.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(connection, transaction, managerId);

                    using (var command = new SQLiteCommand(
                        "SELECT Status FROM InventoryCountSessions WHERE Id=@S;",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);

                        if (Convert.ToString(command.ExecuteScalar()) != "Closed")
                            throw new InvalidOperationException("إعادة المراجعة الجزئية للجلسة المغلقة فقط.");
                    }

                    foreach (int id in ids)
                    {
                        string targetKey;
                        string targetType;
                        string expirySnapshot;
                        int productId;
                        int? batchId;

                        using (var command = new SQLiteCommand(@"
SELECT TargetKey,COALESCE(TargetType,'Batch') AS TargetType,
       ProductId,BatchId,ExpiryDateSnapshot
FROM InventoryCountItems
WHERE Id=@I AND SessionId=@S AND ItemStatus='Approved';",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@I", id);
                            command.Parameters.AddWithValue("@S", sessionId);

                            using (var reader = command.ExecuteReader())
                            {
                                if (!reader.Read())
                                {
                                    throw new InvalidOperationException(
                                        "أحد البنود ليس معتمدًا أو لا يتبع الجلسة.");
                                }

                                targetKey = Convert.ToString(reader["TargetKey"]);
                                targetType = Convert.ToString(reader["TargetType"]);
                                productId = Convert.ToInt32(reader["ProductId"]);

                                batchId = reader["BatchId"] == DBNull.Value
                                    ? (int?)null
                                    : Convert.ToInt32(reader["BatchId"]);

                                expirySnapshot = Convert.ToString(reader["ExpiryDateSnapshot"]);
                            }
                        }

                        using (var command = new SQLiteCommand(@"
SELECT COUNT(*)
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE i.TargetKey=@K
  AND i.SessionId<>@S
  AND i.ItemStatus NOT IN ('Approved','Cancelled')
  AND s.Status IN ('Open','Submitted','PartiallyApproved');",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@K", targetKey);
                            command.Parameters.AddWithValue("@S", sessionId);

                            if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                            {
                                throw new InvalidOperationException(
                                    "لا يمكن إعادة فتح الهدف؛ لأنه مستخدم حاليًا في جلسة جرد أخرى.");
                            }
                        }

                        int snapshotCount;

                        using (var command = new SQLiteCommand(
                            "SELECT COUNT(*) FROM InventoryCountItemBatchSnapshots WHERE CountItemId=@I;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@I", id);
                            snapshotCount = Convert.ToInt32(command.ExecuteScalar());
                        }

                        if (snapshotCount == 0)
                        {
                            if (!batchId.HasValue)
                            {
                                throw new InvalidOperationException(
                                    "لا توجد لقطات استلام محفوظة لهذا الهدف؛ لا يمكن إعادة فتحه بأمان.");
                            }

                            using (var command = new SQLiteCommand(@"
SELECT CASE
    WHEN ABS(COALESCE(b.Quantity,0)-COALESCE(i.AppliedQuantity,i.ActualQuantity,0))<0.00000001
    THEN 0 ELSE 1 END
FROM InventoryCountItems i
LEFT JOIN Batches b ON b.Id=i.BatchId
WHERE i.Id=@I;", connection, transaction))
                            {
                                command.Parameters.AddWithValue("@I", id);

                                if (Convert.ToInt32(command.ExecuteScalar()) != 0)
                                {
                                    throw new InvalidOperationException(
                                        "تغير رصيد التشغيلة بعد إغلاق الجلسة. لا يمكن إعادة فتحها قبل إنشاء لقطة أساس محدثة.");
                                }
                            }
                        }
                        else
                        {
                            using (var command = new SQLiteCommand(@"
SELECT COUNT(*)
FROM InventoryCountItemBatchSnapshots snap
LEFT JOIN Batches b ON b.Id=snap.BatchId
WHERE snap.CountItemId=@I
  AND ABS(COALESCE(b.Quantity,0) -
      (snap.BookQuantity
       + COALESCE((SELECT SUM(d.DifferenceQuantity)
                   FROM InventoryCountItemCostDecisions d
                   WHERE d.CountItemId=@I AND d.SourceBatchId=snap.BatchId),0)
       + COALESCE((SELECT SUM(CASE WHEN m.MovementType='Sale' THEN -m.Quantity ELSE m.Quantity END)
                   FROM InventoryCountPendingMovements m
                   WHERE m.CountItemId=@I AND m.BatchId=snap.BatchId
                     AND m.ProcessingStatus='Processed'),0)
      )) >= 0.00000001;", connection, transaction))
                            {
                                command.Parameters.AddWithValue("@I", id);

                                if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                                {
                                    throw new InvalidOperationException(
                                        "تغيرت كميات أحد الاستلامات بعد إغلاق الجلسة. لا يمكن إعادة فتح الهدف قبل تحديث لقطة الأساس.");
                                }
                            }
                        }

                        if (!string.Equals(targetType, "Batch", StringComparison.OrdinalIgnoreCase))
                        {
                            string newLayerSql = targetType == "ExpiryGroup"
                                ? @"
SELECT COUNT(*) FROM Batches b
WHERE b.ProductId=@P
  AND COALESCE(b.ExpiryDate,'')=COALESCE(@E,'')
  AND NOT EXISTS (
      SELECT 1 FROM InventoryCountItemBatchSnapshots snap
      WHERE snap.CountItemId=@I AND snap.BatchId=b.Id);"
                                : @"
SELECT COUNT(*) FROM Batches b
WHERE b.ProductId=@P
  AND NOT EXISTS (
      SELECT 1 FROM InventoryCountItemBatchSnapshots snap
      WHERE snap.CountItemId=@I AND snap.BatchId=b.Id);";

                            using (var command = new SQLiteCommand(newLayerSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@P", productId);
                                command.Parameters.AddWithValue("@E", expirySnapshot ?? "");
                                command.Parameters.AddWithValue("@I", id);

                                if (Convert.ToInt32(command.ExecuteScalar()) > 0)
                                {
                                    throw new InvalidOperationException(
                                        "وصل استلام جديد بعد إغلاق الجلسة. يلزم تحديث لقطة الاستلامات قبل إعادة فتح الهدف.");
                                }
                            }
                        }

                        using (var command = new SQLiteCommand(@"
INSERT INTO InventoryCountReopenSnapshots
(CountItemId,SessionId,PreviousActualQuantity,PreviousActualPQuantity,
 PreviousActualUQuantity,PreviousUnitsPerPackageSnapshot,PreviousCountedBy,
 PreviousCountedAt,PreviousManagerNote,PreviousApprovedBy,PreviousApprovedAt,
 PreviousAppliedQuantity,PreviousNetVarianceQuantity,PreviousClosedBy,
 PreviousClosedAt,ReopenedBy,ReopenedAt)
SELECT i.Id,i.SessionId,i.ActualQuantity,i.ActualPQuantity,
       i.ActualUQuantity,i.UnitsPerPackageSnapshot,i.CountedBy,
       i.CountedAt,i.ManagerNote,i.ApprovedBy,i.ApprovedAt,
       i.AppliedQuantity,i.NetVarianceQuantity,s.ClosedBy,s.ClosedAt,@U,@N
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE i.Id=@I AND i.SessionId=@S AND i.ItemStatus='Approved'
  AND NOT EXISTS
  (
      SELECT 1 FROM InventoryCountReopenSnapshots old
      WHERE old.CountItemId=i.Id AND old.RestoredAt IS NULL
  );", connection, transaction))
                        {
                            command.Parameters.AddWithValue("@U", managerId);
                            command.Parameters.AddWithValue("@N", Now());
                            command.Parameters.AddWithValue("@I", id);
                            command.Parameters.AddWithValue("@S", sessionId);
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException(
                                    "تعذر حفظ نسخة الاعتماد السابق؛ لم تتم إعادة فتح البند.");
                        }

                        using (var command = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET ItemStatus='RecountRequired',ManagerNote=@R,
    LockUserId=NULL,LockToken=NULL,LockUntil=NULL
WHERE Id=@I AND SessionId=@S AND ItemStatus='Approved';",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@R", reason.Trim());
                            command.Parameters.AddWithValue("@I", id);
                            command.Parameters.AddWithValue("@S", sessionId);

                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException("تعذر إعادة فتح هدف الجرد المحدد.");
                        }

                        Event(connection, transaction, sessionId, id,
                            "ItemReopened", managerId, reason.Trim());
                    }

                    using (var command = new SQLiteCommand(@"
UPDATE InventoryCountSessions
SET Status='PartiallyApproved',ClosedBy=NULL,ClosedAt=NULL
WHERE Id=@S;", connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);
                        command.ExecuteNonQuery();
                    }

                    Event(connection, transaction, sessionId, null,
                        "SessionPartiallyReopened", managerId,
                        "إعادة فتح جزئية؛ العدد: " + ids.Count + "؛ السبب: " + reason.Trim());

                    transaction.Commit();
                }
            }
        }


        public void CancelReopenedItems(
            int managerId, int sessionId, IEnumerable<int> itemIds, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("سبب إلغاء المراجعة مطلوب.");

            var ids = (itemIds ?? Enumerable.Empty<int>()).Where(x => x > 0).ToList();
            if (ids.Count == 0)
                throw new ArgumentException("حدد بندًا واحدًا على الأقل.");
            if (ids.Distinct().Count() != ids.Count)
                throw new ArgumentException("لا يمكن تحديد البند نفسه أكثر من مرة.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(connection, transaction, managerId);
                    using (var command = new SQLiteCommand(
                        "SELECT Status FROM InventoryCountSessions WHERE Id=@S;",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);
                        string status = Convert.ToString(command.ExecuteScalar());
                        if (status != "PartiallyApproved" && status != "Submitted" && status != "Open")
                            throw new InvalidOperationException("لا توجد مراجعة مفتوحة يمكن إلغاؤها في هذه الجلسة.");
                    }

                    var affectedProducts = new SortedSet<int>();
                    int? originalClosedBy = null;
                    string originalClosedAt = null;

                    foreach (int id in ids)
                    {
                        int productId, snapshotId;
                        string itemStatus;
                        double? currentQuantity, previousQuantity;
                        int? currentP, currentU, previousP, previousU;
                        int? previousClosedBy;
                        string previousClosedAt;

                        using (var command = new SQLiteCommand(@"
SELECT i.ProductId,i.ItemStatus,i.ActualQuantity,i.ActualPQuantity,i.ActualUQuantity,
       snap.Id AS SnapshotId,snap.PreviousActualQuantity,
       snap.PreviousActualPQuantity,snap.PreviousActualUQuantity,
       snap.PreviousClosedBy,snap.PreviousClosedAt
FROM InventoryCountItems i
INNER JOIN InventoryCountReopenSnapshots snap
        ON snap.CountItemId=i.Id AND snap.SessionId=i.SessionId
       AND snap.RestoredAt IS NULL
WHERE i.Id=@I AND i.SessionId=@S
ORDER BY snap.Id DESC LIMIT 1;", connection, transaction))
                        {
                            command.Parameters.AddWithValue("@I", id);
                            command.Parameters.AddWithValue("@S", sessionId);
                            using (var reader = command.ExecuteReader())
                            {
                                if (!reader.Read())
                                    throw new InvalidOperationException("لا توجد نسخة محفوظة للاعتماد السابق لهذا البند.");

                                itemStatus = Convert.ToString(reader["ItemStatus"]);
                                if (itemStatus != "RecountRequired" && itemStatus != "Counting" &&
                                    itemStatus != "Counted" && itemStatus != "Review")
                                    throw new InvalidOperationException(
                                        "يمكن إلغاء المراجعة للبند المفتوح فقط؛ لا يمكن التراجع عن اعتماد جديد.");

                                productId = Convert.ToInt32(reader["ProductId"]);
                                snapshotId = Convert.ToInt32(reader["SnapshotId"]);
                                currentQuantity = reader["ActualQuantity"] == DBNull.Value
                                    ? (double?)null : Convert.ToDouble(reader["ActualQuantity"]);
                                currentP = reader["ActualPQuantity"] == DBNull.Value
                                    ? (int?)null : Convert.ToInt32(reader["ActualPQuantity"]);
                                currentU = reader["ActualUQuantity"] == DBNull.Value
                                    ? (int?)null : Convert.ToInt32(reader["ActualUQuantity"]);
                                previousQuantity = reader["PreviousActualQuantity"] == DBNull.Value
                                    ? (double?)null : Convert.ToDouble(reader["PreviousActualQuantity"]);
                                previousP = reader["PreviousActualPQuantity"] == DBNull.Value
                                    ? (int?)null : Convert.ToInt32(reader["PreviousActualPQuantity"]);
                                previousU = reader["PreviousActualUQuantity"] == DBNull.Value
                                    ? (int?)null : Convert.ToInt32(reader["PreviousActualUQuantity"]);
                                previousClosedBy = reader["PreviousClosedBy"] == DBNull.Value
                                    ? (int?)null : Convert.ToInt32(reader["PreviousClosedBy"]);
                                previousClosedAt = reader["PreviousClosedAt"] == DBNull.Value
                                    ? null : Convert.ToString(reader["PreviousClosedAt"]);
                            }
                        }

                        if (!originalClosedBy.HasValue && previousClosedBy.HasValue)
                            originalClosedBy = previousClosedBy;
                        if (string.IsNullOrWhiteSpace(originalClosedAt) &&
                            !string.IsNullOrWhiteSpace(previousClosedAt))
                            originalClosedAt = previousClosedAt;

                        using (var command = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET ActualQuantity=(SELECT PreviousActualQuantity FROM InventoryCountReopenSnapshots WHERE Id=@R),
    ActualPQuantity=(SELECT PreviousActualPQuantity FROM InventoryCountReopenSnapshots WHERE Id=@R),
    ActualUQuantity=(SELECT PreviousActualUQuantity FROM InventoryCountReopenSnapshots WHERE Id=@R),
    UnitsPerPackageSnapshot=(SELECT PreviousUnitsPerPackageSnapshot FROM InventoryCountReopenSnapshots WHERE Id=@R),
    CountedBy=(SELECT PreviousCountedBy FROM InventoryCountReopenSnapshots WHERE Id=@R),
    CountedAt=(SELECT PreviousCountedAt FROM InventoryCountReopenSnapshots WHERE Id=@R),
    ManagerNote=(SELECT PreviousManagerNote FROM InventoryCountReopenSnapshots WHERE Id=@R),
    ApprovedBy=(SELECT PreviousApprovedBy FROM InventoryCountReopenSnapshots WHERE Id=@R),
    ApprovedAt=(SELECT PreviousApprovedAt FROM InventoryCountReopenSnapshots WHERE Id=@R),
    AppliedQuantity=(SELECT PreviousAppliedQuantity FROM InventoryCountReopenSnapshots WHERE Id=@R),
    NetVarianceQuantity=(SELECT PreviousNetVarianceQuantity FROM InventoryCountReopenSnapshots WHERE Id=@R),
    ItemStatus='Approved',LockUserId=NULL,LockToken=NULL,LockUntil=NULL
WHERE Id=@I AND SessionId=@S
  AND ItemStatus IN ('RecountRequired','Counting','Counted','Review');",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@R", snapshotId);
                            command.Parameters.AddWithValue("@I", id);
                            command.Parameters.AddWithValue("@S", sessionId);
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException(
                                    "تغيرت حالة البند أثناء إلغاء المراجعة؛ لم يتم استرجاع الاعتماد السابق.");
                        }

                        RevisionPAndU(connection, transaction, id,
                            currentQuantity, previousQuantity,
                            currentP, currentU, previousP, previousU,
                            itemStatus, "Approved", managerId,
                            "إلغاء المراجعة والعودة للاعتماد السابق: " + reason.Trim());

                        using (var command = new SQLiteCommand(@"
UPDATE InventoryCountReopenSnapshots
SET RestoredBy=@U,RestoredAt=@N,RestoreReason=@R
WHERE Id=@I AND RestoredAt IS NULL;", connection, transaction))
                        {
                            command.Parameters.AddWithValue("@U", managerId);
                            command.Parameters.AddWithValue("@N", Now());
                            command.Parameters.AddWithValue("@R", reason.Trim());
                            command.Parameters.AddWithValue("@I", snapshotId);
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException("تعذر إغلاق سجل المراجعة بعد استرجاع الاعتماد السابق.");
                        }

                        Event(connection, transaction, sessionId, id,
                            "ItemReopenCancelled", managerId,
                            "استرجاع الاعتماد السابق دون عكس تسوية المخزون: " + reason.Trim());
                        affectedProducts.Add(productId);
                    }

                    foreach (int productId in affectedProducts)
                        ProcessPendingMovementsForProduct(connection, transaction, productId, managerId);

                    int unfinished, pending;
                    using (var command = new SQLiteCommand(@"
SELECT COUNT(*) FROM InventoryCountItems
WHERE SessionId=@S AND ItemStatus NOT IN ('Approved','Cancelled');",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);
                        unfinished = Convert.ToInt32(command.ExecuteScalar());
                    }
                    using (var command = new SQLiteCommand(@"
SELECT COUNT(*) FROM InventoryCountPendingMovements
WHERE SessionId=@S AND ProcessingStatus IN ('Pending','Failed');",
                        connection, transaction))
                    {
                        command.Parameters.AddWithValue("@S", sessionId);
                        pending = Convert.ToInt32(command.ExecuteScalar());
                    }

                    if (unfinished == 0 && pending == 0)
                    {
                        using (var command = new SQLiteCommand(@"
UPDATE InventoryCountSessions
SET Status='Closed',ClosedBy=@U,ClosedAt=@N
WHERE Id=@S;", connection, transaction))
                        {
                            command.Parameters.AddWithValue("@U",
                                originalClosedBy.HasValue ? (object)originalClosedBy.Value : managerId);
                            command.Parameters.AddWithValue("@N",
                                string.IsNullOrWhiteSpace(originalClosedAt) ? Now() : originalClosedAt);
                            command.Parameters.AddWithValue("@S", sessionId);
                            command.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var command = new SQLiteCommand(
                            "UPDATE InventoryCountSessions SET Status='PartiallyApproved' WHERE Id=@S;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@S", sessionId);
                            command.ExecuteNonQuery();
                        }
                    }

                    Event(connection, transaction, sessionId, null,
                        "SessionReopenCancelled", managerId,
                        "إلغاء المراجعة وإرجاع البنود للاعتماد السابق؛ العدد: " +
                        ids.Count + "؛ السبب: " + reason.Trim());

                    transaction.Commit();
                }
            }
        }

        public static int? FindActiveSessionForProduct(
            SQLiteConnection c, SQLiteTransaction t, int productId)
        {
            using (var cmd = new SQLiteCommand(@"
SELECT s.Id
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE i.ProductId=@P
  AND i.ItemStatus NOT IN ('Approved','Cancelled')
  AND s.Status IN ('Open','Submitted','PartiallyApproved')
ORDER BY s.CreatedAt,s.Id
LIMIT 1;", c, t))
            {
                cmd.Parameters.AddWithValue("@P", productId);

                object value = cmd.ExecuteScalar();

                return value == null || value == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(value);
            }
        }

        public static int? FindActiveSessionForIncomingBatch(
            SQLiteConnection c, SQLiteTransaction t, int productId, string expiryDate)
        {
            using (var command = new SQLiteCommand(@"
SELECT s.Id
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE i.ProductId=@P
  AND i.ItemStatus NOT IN ('Approved','Cancelled')
  AND s.Status IN ('Open','Submitted','PartiallyApproved')
  AND
  (
      (
          i.TargetType='ExpiryGroup'
          AND i.TargetKey='E:' || @P || ':' ||
              CASE
                  WHEN TRIM(COALESCE(@E,''))=''
                      THEN '~NONE~'
                  ELSE TRIM(@E)
              END
      )
      OR
      (
          i.TargetType='Product'
          AND i.TargetKey='P:' || @P
      )
  )
ORDER BY s.CreatedAt,s.Id
LIMIT 1;", c, t))
            {
                command.Parameters.AddWithValue("@P", productId);
                command.Parameters.AddWithValue("@E", expiryDate ?? "");
                object value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(value);
            }
        }

        public static void EnsureIncomingBatchNotFrozen(
            SQLiteConnection c, SQLiteTransaction t, int productId,
            string expiryDate, string operation)
        {
            int? sessionId = FindActiveSessionForIncomingBatch(
                c, t, productId, expiryDate);

            if (!sessionId.HasValue)
                return;

            using (var command = new SQLiteCommand(@"
SELECT SessionNumber,CreatedAt
FROM InventoryCountSessions
WHERE Id=@S;", c, t))
            {
                command.Parameters.AddWithValue("@S", sessionId.Value);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        throw new InvalidOperationException(
                            "لا يمكن تنفيذ " + (operation ?? "الحركة") +
                            " لأن هدف الصنف/الصلاحية مجمد ضمن جلسة الجرد رقم " +
                            Convert.ToString(reader["SessionNumber"]) +
                            "، وتاريخ بدء الجرد: " + Convert.ToString(reader["CreatedAt"]) + ".");
                    }
                }
            }
        }

        public static int? FindActiveSessionForBatch(
            SQLiteConnection c, SQLiteTransaction t, int batchId)
        {
            using (var command = new SQLiteCommand(@"
SELECT s.Id
FROM Batches b
INNER JOIN InventoryCountItems i
    ON i.ProductId=b.ProductId
   AND (
       i.BatchId=b.Id
       OR i.TargetType='Product'
       OR (
           i.TargetType='ExpiryGroup'
           AND i.TargetKey='E:' || b.ProductId || ':' ||
               CASE WHEN TRIM(COALESCE(b.ExpiryDate,''))=''
                    THEN '~NONE~' ELSE TRIM(b.ExpiryDate) END
       )
   )
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE b.Id=@B
  AND i.ItemStatus NOT IN ('Approved','Cancelled')
  AND s.Status IN ('Open','Submitted','PartiallyApproved')
ORDER BY s.CreatedAt,s.Id
LIMIT 1;", c, t))
            {
                command.Parameters.AddWithValue("@B", batchId);

                object value = command.ExecuteScalar();

                return value == null || value == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(value);
            }
        }

        public static void EnsureBatchNotFrozen(
            SQLiteConnection c, SQLiteTransaction t, int batchId, string operation)
        {
            using (var command = new SQLiteCommand(@"
SELECT s.SessionNumber,s.CreatedAt
FROM Batches b
INNER JOIN InventoryCountItems i
    ON i.ProductId=b.ProductId
   AND (
       i.BatchId=b.Id
       OR i.TargetType='Product'
       OR (
           i.TargetType='ExpiryGroup'
           AND i.TargetKey='E:' || b.ProductId || ':' ||
               CASE WHEN TRIM(COALESCE(b.ExpiryDate,''))=''
                    THEN '~NONE~' ELSE TRIM(b.ExpiryDate) END
       )
   )
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE b.Id=@B
  AND i.ItemStatus NOT IN ('Approved','Cancelled')
  AND s.Status IN ('Open','Submitted','PartiallyApproved')
ORDER BY s.CreatedAt,s.Id
LIMIT 1;", c, t))
            {
                command.Parameters.AddWithValue("@B", batchId);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        throw new InvalidOperationException(
                            "لا يمكن تنفيذ " + (operation ?? "الحركة") +
                            " لأن هدف المخزون مجمد ضمن جلسة الجرد رقم " +
                            Convert.ToString(reader["SessionNumber"]) +
                            "، وتاريخ بدء الجرد: " + Convert.ToString(reader["CreatedAt"]) + ".");
                    }
                }
            }
        }

        public static void EnsureProductNotFrozen(
            SQLiteConnection c, SQLiteTransaction t, int productId, string operation)
        {
            using (var cmd = new SQLiteCommand(@"
SELECT s.SessionNumber,s.CreatedAt
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE i.ProductId=@P
  AND i.ItemStatus NOT IN ('Approved','Cancelled')
  AND s.Status IN ('Open','Submitted','PartiallyApproved')
ORDER BY s.CreatedAt,s.Id
LIMIT 1;", c, t))
            {
                cmd.Parameters.AddWithValue("@P", productId);

                using (var rd = cmd.ExecuteReader())
                {
                    if (rd.Read())
                    {
                        throw new InvalidOperationException(
                            "لا يمكن تنفيذ " + (operation ?? "الحركة") +
                            " لأن الصنف مجمد ضمن جلسة الجرد رقم " +
                            Convert.ToString(rd["SessionNumber"]) +
                            "، وتاريخ بدء الجرد: " + Convert.ToString(rd["CreatedAt"]) + ".");
                    }
                }
            }
        }

        public static void QueuePendingMovement(
            SQLiteConnection c, SQLiteTransaction t, int sessionId, int productId,
            int batchId, string movementType, string sourceTable, int sourceRecordId,
            double quantity, string unitSnapshot, int employeeUserId, string originalCreatedAt)
        {
            int? countItemId = null;
            string targetKey = "";

            using (var target = new SQLiteCommand(@"
SELECT i.Id,i.TargetKey
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
LEFT JOIN Batches b ON b.Id=@B
WHERE i.SessionId=@S
  AND i.ProductId=@P
  AND i.ItemStatus NOT IN ('Approved','Cancelled')
  AND s.Status IN ('Open','Submitted','PartiallyApproved')
  AND (
      (i.TargetType='Batch' AND i.BatchId=@B)
      OR (
          i.TargetType='ExpiryGroup'
          AND i.TargetKey='E:' || @P || ':' ||
              CASE WHEN TRIM(COALESCE(b.ExpiryDate,''))=''
                   THEN '~NONE~' ELSE TRIM(b.ExpiryDate) END
      )
      OR (
          i.TargetType='Product'
          AND i.TargetKey='P:' || @P
      )
  )
ORDER BY
    CASE
        WHEN i.TargetType='Batch' AND i.BatchId=@B THEN 0
        WHEN i.TargetType='ExpiryGroup' THEN 1
        WHEN i.TargetType='Product' THEN 2
        ELSE 3
    END,
    i.Id
LIMIT 1;", c, t))
            {
                target.Parameters.AddWithValue("@B", batchId);
                target.Parameters.AddWithValue("@S", sessionId);
                target.Parameters.AddWithValue("@P", productId);

                using (var reader = target.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        countItemId = Convert.ToInt32(reader["Id"]);
                        targetKey = Convert.ToString(reader["TargetKey"]);
                    }
                }
            }

            using (var existing = new SQLiteCommand(@"
SELECT Id,SessionId,ProductId,BatchId,CountItemId,
       COALESCE(TargetKey,'') AS TargetKey,
       Quantity,COALESCE(UnitSnapshot,'') AS UnitSnapshot
FROM InventoryCountPendingMovements
WHERE SourceTable=@ST AND SourceRecordId=@SR AND MovementType=@T
LIMIT 1;", c, t))
            {
                existing.Parameters.AddWithValue("@ST", sourceTable);
                existing.Parameters.AddWithValue("@SR", sourceRecordId);
                existing.Parameters.AddWithValue("@T", movementType);

                using (var reader = existing.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        bool same =
                            Convert.ToInt32(reader["SessionId"]) == sessionId &&
                            Convert.ToInt32(reader["ProductId"]) == productId &&
                            Convert.ToInt32(reader["BatchId"]) == batchId &&
                            (reader["CountItemId"] == DBNull.Value
                                ? (int?)null
                                : Convert.ToInt32(reader["CountItemId"])) == countItemId &&
                            string.Equals(
                                Convert.ToString(reader["TargetKey"]),
                                targetKey ?? string.Empty,
                                StringComparison.Ordinal) &&
                            string.Equals(
                                Convert.ToString(reader["UnitSnapshot"]),
                                unitSnapshot ?? string.Empty,
                                StringComparison.Ordinal) &&
                            Math.Abs(Convert.ToDouble(reader["Quantity"]) - quantity) < 0.00000001;

                        if (!same)
                        {
                            throw new InvalidOperationException(
                                "مصدر الحركة مسجل مسبقًا ببيانات مختلفة؛ تم إيقاف العملية لمنع ازدواج المخزون.");
                        }

                        return;
                    }
                }
            }

            using (var command = new SQLiteCommand(@"
INSERT INTO InventoryCountPendingMovements
(SessionId,ProductId,BatchId,CountItemId,TargetKey,
 MovementType,SourceTable,SourceRecordId,Quantity,UnitSnapshot,
 EmployeeUserId,OriginalCreatedAt,ProcessingStatus)
VALUES(@S,@P,@B,@CI,@K,@T,@ST,@SR,@Q,@U,@E,@D,'Pending');", c, t))
            {
                command.Parameters.AddWithValue("@S", sessionId);
                command.Parameters.AddWithValue("@P", productId);
                command.Parameters.AddWithValue("@B", batchId);
                command.Parameters.AddWithValue("@CI",
                    countItemId.HasValue ? (object)countItemId.Value : DBNull.Value);
                command.Parameters.AddWithValue("@K", targetKey ?? "");
                command.Parameters.AddWithValue("@T", movementType);
                command.Parameters.AddWithValue("@ST", sourceTable);
                command.Parameters.AddWithValue("@SR", sourceRecordId);
                command.Parameters.AddWithValue("@Q", quantity);
                command.Parameters.AddWithValue("@U", unitSnapshot ?? "");
                command.Parameters.AddWithValue("@E", employeeUserId);
                command.Parameters.AddWithValue("@D", originalCreatedAt);
                command.ExecuteNonQuery();
            }

            Event(c, t, sessionId, countItemId,
                "PendingMovementCreated", employeeUserId,
                "حركة معلقة: " + movementType +
                "؛ الصنف: " + productId +
                "؛ التشغيلة: " + batchId +
                "؛ الهدف: " + targetKey +
                "؛ الكمية: " +
                quantity.ToString("0.########", CultureInfo.InvariantCulture));
        }

        private static void ProcessPendingMovementsForProduct(
            SQLiteConnection c, SQLiteTransaction t, int productId, int actorUserId)
        {
            var movements = new List<PendingMovement>();

            using (var command = new SQLiteCommand(@"
SELECT Id,SessionId,ProductId,BatchId,CountItemId,MovementType,
       SourceTable,SourceRecordId,Quantity,
       COALESCE(TargetKey,'') AS TargetKey,
       COALESCE(UnitSnapshot,'') AS UnitSnapshot
FROM InventoryCountPendingMovements
WHERE ProductId=@P
  AND ProcessingStatus IN ('Pending','Failed')
ORDER BY OriginalCreatedAt,Id;", c, t))
            {
                command.Parameters.AddWithValue("@P", productId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        movements.Add(new PendingMovement
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            SessionId = Convert.ToInt32(reader["SessionId"]),
                            ProductId = Convert.ToInt32(reader["ProductId"]),
                            BatchId = Convert.ToInt32(reader["BatchId"]),
                            CountItemId = reader["CountItemId"] == DBNull.Value
                                ? (int?)null
                                : Convert.ToInt32(reader["CountItemId"]),
                            MovementType = Convert.ToString(reader["MovementType"]),
                            SourceTable = Convert.ToString(reader["SourceTable"]),
                            SourceRecordId = Convert.ToInt32(reader["SourceRecordId"]),
                            Quantity = Convert.ToDouble(reader["Quantity"]),
                            TargetKey = Convert.ToString(reader["TargetKey"]),
                            UnitSnapshot = Convert.ToString(reader["UnitSnapshot"])
                        });
                    }
                }
            }

            foreach (var movement in movements)
            {
                int activeCount;

                string activeSql = string.IsNullOrWhiteSpace(movement.TargetKey)
                    ? @"
SELECT COUNT(*)
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE i.ProductId=@P
  AND i.ItemStatus NOT IN ('Approved','Cancelled')
  AND s.Status IN ('Open','Submitted','PartiallyApproved');"
                    : @"
SELECT COUNT(*)
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
WHERE i.TargetKey=@K
  AND i.ItemStatus NOT IN ('Approved','Cancelled')
  AND s.Status IN ('Open','Submitted','PartiallyApproved');";

                using (var active = new SQLiteCommand(activeSql, c, t))
                {
                    if (string.IsNullOrWhiteSpace(movement.TargetKey))
                        active.Parameters.AddWithValue("@P", productId);
                    else
                        active.Parameters.AddWithValue("@K", movement.TargetKey);

                    activeCount = Convert.ToInt32(active.ExecuteScalar());
                }

                if (activeCount > 0)
                    continue;

                using (var savepoint = new SQLiteCommand("SAVEPOINT pending_movement;", c, t))
                    savepoint.ExecuteNonQuery();

                try
                {
                    bool expiredReturn = false;

                    if (movement.MovementType == "Purchase" &&
                        movement.SourceTable == "PurchaseInvoiceItems")
                    {
                        int sourceBatchId;
                        double sourceQuantity;
                        using (var sourceCommand = new SQLiteCommand(@"
SELECT BatchId,
       COALESCE(PackageQuantity,0)+COALESCE(FreePackageQuantity,0) AS TotalPackageQuantity
FROM PurchaseInvoiceItems
WHERE Id=@I;", c, t))
                        {
                            sourceCommand.Parameters.AddWithValue("@I", movement.SourceRecordId);
                            using (var sourceReader = sourceCommand.ExecuteReader())
                            {
                                if (!sourceReader.Read())
                                    throw new InvalidOperationException("سجل استلام الشراء غير موجود.");
                                sourceBatchId = Convert.ToInt32(sourceReader["BatchId"]);
                                sourceQuantity = Convert.ToDouble(sourceReader["TotalPackageQuantity"]);
                            }
                        }
                        if (sourceBatchId != movement.BatchId ||
                            Math.Abs(sourceQuantity - movement.Quantity) >= 0.00000001)
                            throw new InvalidOperationException(
                                "بيانات استلام الشراء لا تطابق الحركة المعلقة؛ تم إيقاف الإضافة.");

                        using (var command = new SQLiteCommand(
                            "UPDATE Batches SET Quantity=Quantity+@Q WHERE Id=@B AND ProductId=@P;", c, t))
                        {
                            command.Parameters.AddWithValue("@Q", movement.Quantity);
                            command.Parameters.AddWithValue("@B", movement.BatchId);
                            command.Parameters.AddWithValue("@P", movement.ProductId);
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException("تشغيلة الشراء غير موجودة أو لا تتبع الصنف.");
                        }
                    }
                    else if (movement.MovementType == "ManualAdd" &&
                             movement.SourceTable == "Batches")
                    {
                        int sourceProductId;
                        using (var sourceCommand = new SQLiteCommand(
                            "SELECT ProductId FROM Batches WHERE Id=@B;", c, t))
                        {
                            sourceCommand.Parameters.AddWithValue("@B", movement.SourceRecordId);
                            object sourceValue = sourceCommand.ExecuteScalar();
                            if (sourceValue == null || sourceValue == DBNull.Value)
                                throw new InvalidOperationException("التشغيلة المضافة يدويًا غير موجودة.");
                            sourceProductId = Convert.ToInt32(sourceValue);
                        }
                        if (sourceProductId != movement.ProductId ||
                            movement.SourceRecordId != movement.BatchId)
                            throw new InvalidOperationException("بيانات إضافة التشغيلة لا تطابق الحركة المعلقة.");
                        using (var command = new SQLiteCommand(
                            "UPDATE Batches SET Quantity=Quantity+@Q WHERE Id=@B AND ProductId=@P;",
                            c, t))
                        {
                            command.Parameters.AddWithValue("@Q", movement.Quantity);
                            command.Parameters.AddWithValue("@B", movement.BatchId);
                            command.Parameters.AddWithValue("@P", movement.ProductId);
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException("تعذر إضافة الكمية إلى التشغيلة اليدوية.");
                        }
                    }
                    else if (movement.MovementType == "Sale" &&
                             movement.SourceTable == "SalesInvoiceBatchAllocations")
                    {
                        double sourceQuantity;
                        int sourceBatchId;
                        int sourceProductId;
                        using (var sourceCommand = new SQLiteCommand(@"
SELECT a.BatchId,a.Quantity,si.ProductId
FROM SalesInvoiceBatchAllocations a
INNER JOIN SalesInvoiceItems si ON si.Id=a.InvoiceItemId
WHERE a.Id=@I;", c, t))
                        {
                            sourceCommand.Parameters.AddWithValue("@I", movement.SourceRecordId);
                            using (var sourceReader = sourceCommand.ExecuteReader())
                            {
                                if (!sourceReader.Read())
                                    throw new InvalidOperationException("سجل تخصيص البيع غير موجود.");
                                sourceBatchId = Convert.ToInt32(sourceReader["BatchId"]);
                                sourceQuantity = Convert.ToDouble(sourceReader["Quantity"]);
                                sourceProductId = Convert.ToInt32(sourceReader["ProductId"]);
                            }
                        }
                        if (sourceBatchId != movement.BatchId ||
                            sourceProductId != movement.ProductId ||
                            Math.Abs(sourceQuantity - movement.Quantity) >= 0.00000001)
                            throw new InvalidOperationException(
                                "بيانات تخصيص البيع لا تطابق الحركة المعلقة؛ تم إيقاف الخصم لمنع ازدواج المخزون.");
                        using (var command = new SQLiteCommand(@"
UPDATE Batches SET Quantity=Quantity-@Q
WHERE Id=@B AND Quantity>=@Q;", c, t))
                        {
                            command.Parameters.AddWithValue("@Q", movement.Quantity);
                            command.Parameters.AddWithValue("@B", movement.BatchId);
                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException(
                                    "رصيد التشغيلة غير كافٍ لتنفيذ حركة البيع المعلقة.");
                        }
                    }
                    else if (movement.MovementType == "SalesReturn" &&
                             movement.SourceTable == "SalesReturnBatchAllocations")
                    {
                        int sourceBatchId;
                        int sourceProductId;
                        double sourceQuantity;
                        using (var sourceCommand = new SQLiteCommand(@"
SELECT rba.BatchId,rba.Quantity,sri.ProductId
FROM SalesReturnBatchAllocations rba
INNER JOIN SalesReturnItems sri ON sri.Id=rba.ReturnItemId
WHERE rba.Id=@I;", c, t))
                        {
                            sourceCommand.Parameters.AddWithValue("@I", movement.SourceRecordId);
                            using (var sourceReader = sourceCommand.ExecuteReader())
                            {
                                if (!sourceReader.Read())
                                    throw new InvalidOperationException("سجل تخصيص المرتجع غير موجود.");
                                sourceBatchId = Convert.ToInt32(sourceReader["BatchId"]);
                                sourceProductId = Convert.ToInt32(sourceReader["ProductId"]);
                                sourceQuantity = Convert.ToDouble(sourceReader["Quantity"]);
                            }
                        }
                        if (sourceBatchId != movement.BatchId ||
                            sourceProductId != movement.ProductId ||
                            Math.Abs(sourceQuantity - movement.Quantity) >= 0.00000001)
                            throw new InvalidOperationException(
                                "بيانات تخصيص المرتجع لا تطابق الحركة المعلقة؛ تم إيقاف الإضافة.");

                        string expiry;

                        using (var command = new SQLiteCommand(
                            "SELECT COALESCE(ExpiryDate,'') FROM Batches WHERE Id=@B AND ProductId=@P;", c, t))
                        {
                            command.Parameters.AddWithValue("@B", movement.BatchId);
                            command.Parameters.AddWithValue("@P", movement.ProductId);

                            object value = command.ExecuteScalar();

                            if (value == null || value == DBNull.Value)
                                throw new InvalidOperationException("تشغيلة المرتجع غير موجودة.");

                            expiry = Convert.ToString(value).Trim();
                        }

                        DateTime expiryDate;

                        expiredReturn = !string.IsNullOrWhiteSpace(expiry) &&
                            (!DateTime.TryParseExact(
                                expiry, "yyyy-MM-dd",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out expiryDate) ||
                             expiryDate.Date < DateTime.Today);

                        if (!expiredReturn)
                        {
                            using (var command = new SQLiteCommand(
                                "UPDATE Batches SET Quantity=Quantity+@Q WHERE Id=@B;", c, t))
                            {
                                command.Parameters.AddWithValue("@Q", movement.Quantity);
                                command.Parameters.AddWithValue("@B", movement.BatchId);

                                if (command.ExecuteNonQuery() != 1)
                                    throw new InvalidOperationException("تعذر تحديث رصيد تشغيلة المرتجع.");
                            }
                        }

                        using (var command = new SQLiteCommand(
                            "UPDATE SalesReturnBatchAllocations SET Restocked=@R WHERE Id=@I;", c, t))
                        {
                            command.Parameters.AddWithValue("@R", expiredReturn ? 0 : 1);
                            command.Parameters.AddWithValue("@I", movement.SourceRecordId);

                            if (command.ExecuteNonQuery() != 1)
                                throw new InvalidOperationException("سجل تخصيص المرتجع غير موجود.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("نوع الحركة المعلقة أو مصدرها غير معروف.");
                    }

                    if (movement.CountItemId.HasValue && !expiredReturn)
                    {
                        string itemStatus;

                        using (var statusCommand = new SQLiteCommand(
                            "SELECT ItemStatus FROM InventoryCountItems WHERE Id=@I;", c, t))
                        {
                            statusCommand.Parameters.AddWithValue("@I", movement.CountItemId.Value);
                            itemStatus = Convert.ToString(statusCommand.ExecuteScalar());
                        }

                        if (itemStatus == "Approved")
                        {
                            if (movement.MovementType == "Purchase")
                            {
                                double purchaseCost;
                                string batchNumberSnapshot;
                                string expirySnapshot;
                                string receiptAtSnapshot;
                                int receiptItemIdSnapshot;

                                using (var batchCommand = new SQLiteCommand(@"
SELECT b.PurchasePrice,
       COALESCE(b.BatchNumber,'') AS BatchNumber,
       COALESCE(b.ExpiryDate,'') AS ExpiryDate,
       COALESCE(pi.CreatedAt,'') AS ReceiptAt,
       COALESCE(pii.Id,b.Id) AS ReceiptItemId
FROM Batches b
LEFT JOIN PurchaseInvoiceItems pii ON pii.BatchId=b.Id
LEFT JOIN PurchaseInvoices pi ON pi.Id=pii.InvoiceId
WHERE b.Id=@B
ORDER BY COALESCE(pi.CreatedAt,'') DESC,
         COALESCE(pii.Id,b.Id) DESC
LIMIT 1;", c, t))
                                {
                                    batchCommand.Parameters.AddWithValue("@B", movement.BatchId);

                                    using (var batchReader = batchCommand.ExecuteReader())
                                    {
                                        if (!batchReader.Read())
                                            throw new InvalidOperationException("تعذر قراءة بيانات استلام الشراء المعلق.");

                                        purchaseCost = Convert.ToDouble(batchReader["PurchasePrice"]);
                                        batchNumberSnapshot = Convert.ToString(batchReader["BatchNumber"]);
                                        expirySnapshot = Convert.ToString(batchReader["ExpiryDate"]);
                                        receiptAtSnapshot = Convert.ToString(batchReader["ReceiptAt"]);
                                        receiptItemIdSnapshot = Convert.ToInt32(batchReader["ReceiptItemId"]);
                                    }
                                }

                                using (var snapshot = new SQLiteCommand(@"
INSERT OR IGNORE INTO InventoryCountItemBatchSnapshots
(CountItemId,BatchId,BookQuantity,PurchasePriceSnapshot,
 BatchNumberSnapshot,ExpiryDateSnapshot,UnitSnapshot,
 ReceiptAtSnapshot,ReceiptItemIdSnapshot,CreatedAt)
VALUES(@I,@B,0,@C,@BN,@E,@U,@RA,@RI,@D);", c, t))
                                {
                                    snapshot.Parameters.AddWithValue("@I", movement.CountItemId.Value);
                                    snapshot.Parameters.AddWithValue("@B", movement.BatchId);
                                    snapshot.Parameters.AddWithValue("@C", purchaseCost);
                                    snapshot.Parameters.AddWithValue("@BN", batchNumberSnapshot);
                                    snapshot.Parameters.AddWithValue("@E", expirySnapshot);
                                    snapshot.Parameters.AddWithValue("@U", movement.UnitSnapshot ?? "");
                                    snapshot.Parameters.AddWithValue("@RA", receiptAtSnapshot);
                                    snapshot.Parameters.AddWithValue("@RI", receiptItemIdSnapshot);
                                    snapshot.Parameters.AddWithValue("@D", Now());
                                    snapshot.ExecuteNonQuery();
                                }
                            }

                            double appliedDelta = movement.MovementType == "Sale"
                                ? -movement.Quantity : movement.Quantity;
                            using (var appliedCommand = new SQLiteCommand(@"
UPDATE InventoryCountItems
SET AppliedQuantity=COALESCE(AppliedQuantity,BookQuantity)+@Q
WHERE Id=@I AND ItemStatus='Approved';", c, t))
                            {
                                appliedCommand.Parameters.AddWithValue("@Q", appliedDelta);
                                appliedCommand.Parameters.AddWithValue("@I", movement.CountItemId.Value);
                                appliedCommand.ExecuteNonQuery();
                            }
                        }
                    }

                    string result = expiredReturn
                        ? "تمت معالجة المرتجع دون إضافته للمخزون لأن التشغيلة انتهت صلاحيتها."
                        : movement.MovementType == "Sale"
                            ? "تم خصم كمية البيع من رصيد التشغيلة."
                            : "تمت إضافة الكمية إلى رصيد التشغيلة.";

                    using (var command = new SQLiteCommand(@"
UPDATE InventoryCountPendingMovements
SET ProcessingStatus=@S,ProcessedAt=@N,ProcessingMessage=@M
WHERE Id=@I;", c, t))
                    {
                        command.Parameters.AddWithValue("@S", expiredReturn ? "Cancelled" : "Processed");
                        command.Parameters.AddWithValue("@N", Now());
                        command.Parameters.AddWithValue("@M", result);
                        command.Parameters.AddWithValue("@I", movement.Id);
                        command.ExecuteNonQuery();
                    }

                    Event(c, t, movement.SessionId, null,
                        "PendingMovementProcessed", actorUserId,
                        "الحركة المعلقة رقم " + movement.Id + "؛ " + result);

                    using (var release = new SQLiteCommand("RELEASE SAVEPOINT pending_movement;", c, t))
                        release.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    using (var rollback = new SQLiteCommand("ROLLBACK TO SAVEPOINT pending_movement;", c, t))
                        rollback.ExecuteNonQuery();

                    using (var release = new SQLiteCommand("RELEASE SAVEPOINT pending_movement;", c, t))
                        release.ExecuteNonQuery();

                    using (var command = new SQLiteCommand(@"
UPDATE InventoryCountPendingMovements
SET ProcessingStatus='Failed',ProcessedAt=@N,ProcessingMessage=@M
WHERE Id=@I;", c, t))
                    {
                        command.Parameters.AddWithValue("@N", Now());
                        command.Parameters.AddWithValue("@M", ex.Message);
                        command.Parameters.AddWithValue("@I", movement.Id);
                        command.ExecuteNonQuery();
                    }

                    Event(c, t, movement.SessionId, null,
                        "PendingMovementFailed", actorUserId,
                        "فشل معالجة الحركة المعلقة رقم " + movement.Id + "؛ " + ex.Message);
                }
            }
        }

        private static void ProcessPendingMovementsForSession(
            SQLiteConnection c, SQLiteTransaction t, int sessionId, int actorUserId)
        {
            var products = new List<int>();

            using (var cmd = new SQLiteCommand(@"
SELECT ProductId
FROM InventoryCountItems
WHERE SessionId=@S
UNION
SELECT ProductId
FROM InventoryCountPendingMovements
WHERE SessionId=@S
  AND ProcessingStatus IN ('Pending','Failed');", c, t))
            {
                cmd.Parameters.AddWithValue("@S", sessionId);

                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                        products.Add(Convert.ToInt32(rd[0]));
                }
            }

            foreach (int productId in products)
                ProcessPendingMovementsForProduct(c, t, productId, actorUserId);
        }

        public void RetryFailedPendingMovements(int managerId, int sessionId)
        {
            using (var c = new SQLiteConnection(connectionString))
            {
                c.Open();

                using (var t = c.BeginTransaction(IsolationLevel.Serializable))
                {
                    EnsureManager(c, t, managerId);

                    string sessionStatus;

                    using (var cmd = new SQLiteCommand(
                        "SELECT Status FROM InventoryCountSessions WHERE Id=@S;", c, t))
                    {
                        cmd.Parameters.AddWithValue("@S", sessionId);

                        object value = cmd.ExecuteScalar();

                        if (value == null || value == DBNull.Value)
                            throw new InvalidOperationException("جلسة الجرد غير موجودة.");

                        sessionStatus = Convert.ToString(value);
                    }

                    if (sessionStatus != "Open" &&
                        sessionStatus != "Submitted" &&
                        sessionStatus != "PartiallyApproved")
                    {
                        throw new InvalidOperationException(
                            "لا يمكن إعادة معالجة الحركات بعد إغلاق الجلسة أو إلغائها.");
                    }

                    ProcessPendingMovementsForSession(c, t, sessionId, managerId);

                    Event(c, t, sessionId, null,
                        "PendingMovementsRetryRequested", managerId,
                        "إعادة محاولة معالجة الحركات المعلقة.");

                    t.Commit();
                }
            }
        }

        private sealed class PendingMovement
        {
            public int Id { get; set; }
            public int SessionId { get; set; }
            public int ProductId { get; set; }
            public int BatchId { get; set; }
            public int? CountItemId { get; set; }
            public string MovementType { get; set; }
            public string SourceTable { get; set; }
            public int SourceRecordId { get; set; }
            public double Quantity { get; set; }
            public string TargetKey { get; set; }
            public string UnitSnapshot { get; set; }
        }

        private static void EnsureManager(SQLiteConnection c, SQLiteTransaction t, int id)
        {
            using (var q = new SQLiteCommand(
                "SELECT Role FROM Users WHERE Id=@I AND IsActive=1;", c, t))
            {
                q.Parameters.AddWithValue("@I", id);
                object r = q.ExecuteScalar();

                if (r == null ||
                    !string.Equals(Convert.ToString(r), "Manager", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("هذه العملية للمدير فقط.");
                }
            }
        }

        private static void EnsureCounter(SQLiteConnection c, SQLiteTransaction t, int id)
        {
            EnsureManager(c, t, id);
        }

        private static void EnsureActive(SQLiteConnection c, SQLiteTransaction t, int id)
        {
            using (var q = new SQLiteCommand(
                "SELECT COUNT(*) FROM Users WHERE Id=@I AND IsActive=1;", c, t))
            {
                q.Parameters.AddWithValue("@I", id);

                if (Convert.ToInt32(q.ExecuteScalar()) != 1)
                    throw new UnauthorizedAccessException("المستخدم غير نشط.");
            }
        }

        private static void EnsureOpen(SQLiteConnection c, SQLiteTransaction t, int id)
        {
            using (var q = new SQLiteCommand(
                "SELECT Status FROM InventoryCountSessions WHERE Id=@I;", c, t))
            {
                q.Parameters.AddWithValue("@I", id);
                string s = Convert.ToString(q.ExecuteScalar());

                if (s != "Open" && s != "PartiallyApproved" && s != "Submitted")
                    throw new InvalidOperationException("الجلسة لا تقبل العد حاليًا.");
            }
        }

        private static void EnsureReview(SQLiteConnection c, SQLiteTransaction t, int id)
        {
            using (var q = new SQLiteCommand(
                "SELECT Status FROM InventoryCountSessions WHERE Id=@I;", c, t))
            {
                q.Parameters.AddWithValue("@I", id);
                string s = Convert.ToString(q.ExecuteScalar());

                if (s != "Submitted" && s != "PartiallyApproved")
                    throw new InvalidOperationException("الجلسة ليست قيد مراجعة المدير.");
            }
        }

        private static void Event(
            SQLiteConnection c, SQLiteTransaction t,
            int sid, int? item, string type, int actor, string details)
        {
            using (var q = new SQLiteCommand(@"
INSERT INTO InventoryCountEvents
(SessionId,CountItemId,EventType,ActorUserId,Details,CreatedAt)
VALUES(@S,@I,@T,@A,@D,@N);", c, t))
            {
                q.Parameters.AddWithValue("@S", sid);
                q.Parameters.AddWithValue("@I",
                    item.HasValue ? (object)item.Value : DBNull.Value);
                q.Parameters.AddWithValue("@T", type);
                q.Parameters.AddWithValue("@A", actor);
                q.Parameters.AddWithValue("@D", details ?? "");
                q.Parameters.AddWithValue("@N", Now());
                q.ExecuteNonQuery();
            }
        }

        private static void Revision(
            SQLiteConnection c, SQLiteTransaction t,
            int id, double? old, double? next,
            string os, string ns, int actor, string reason)
        {
            using (var q = new SQLiteCommand(@"
INSERT INTO InventoryCountRevisions
(CountItemId,PreviousActualQuantity,NewActualQuantity,
 PreviousStatus,NewStatus,ChangedBy,Reason,CreatedAt)
VALUES(@I,@O,@N,@OS,@NS,@A,@R,@D);", c, t))
            {
                q.Parameters.AddWithValue("@I", id);
                q.Parameters.AddWithValue("@O", old.HasValue ? (object)old.Value : DBNull.Value);
                q.Parameters.AddWithValue("@N", next.HasValue ? (object)next.Value : DBNull.Value);
                q.Parameters.AddWithValue("@OS", os);
                q.Parameters.AddWithValue("@NS", ns);
                q.Parameters.AddWithValue("@A", actor);
                q.Parameters.AddWithValue("@R", reason ?? "");
                q.Parameters.AddWithValue("@D", Now());
                q.ExecuteNonQuery();
            }
        }

        private static void RevisionPAndU(
            SQLiteConnection c, SQLiteTransaction t,
            int id, double? oldQuantity, double? newQuantity,
            int? oldP, int? oldU, int? newP, int? newU,
            string oldStatus, string newStatus, int actor, string reason)
        {
            using (var command = new SQLiteCommand(@"
INSERT INTO InventoryCountRevisions
(CountItemId,PreviousActualQuantity,NewActualQuantity,
 PreviousActualPQuantity,PreviousActualUQuantity,
 NewActualPQuantity,NewActualUQuantity,
 PreviousStatus,NewStatus,ChangedBy,Reason,CreatedAt)
VALUES(@I,@OQ,@NQ,@OP,@OU,@NP,@NU,@OS,@NS,@A,@R,@D);", c, t))
            {
                command.Parameters.AddWithValue("@I", id);
                command.Parameters.AddWithValue("@OQ", oldQuantity.HasValue ? (object)oldQuantity.Value : DBNull.Value);
                command.Parameters.AddWithValue("@NQ", newQuantity.HasValue ? (object)newQuantity.Value : DBNull.Value);
                command.Parameters.AddWithValue("@OP", oldP.HasValue ? (object)oldP.Value : DBNull.Value);
                command.Parameters.AddWithValue("@OU", oldU.HasValue ? (object)oldU.Value : DBNull.Value);
                command.Parameters.AddWithValue("@NP", newP.HasValue ? (object)newP.Value : DBNull.Value);
                command.Parameters.AddWithValue("@NU", newU.HasValue ? (object)newU.Value : DBNull.Value);
                command.Parameters.AddWithValue("@OS", oldStatus);
                command.Parameters.AddWithValue("@NS", newStatus);
                command.Parameters.AddWithValue("@A", actor);
                command.Parameters.AddWithValue("@R", reason ?? "");
                command.Parameters.AddWithValue("@D", Now());
                command.ExecuteNonQuery();
            }
        }

        private static string BuildUnitSnapshot(
            string packageUnitName, string smallUnitName, int unitsPerPackage)
        {
            string packageUnit = (packageUnitName ?? string.Empty).Trim();
            string smallUnit = (smallUnitName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(packageUnit))
                packageUnit = "P";

            if (unitsPerPackage <= 1 || string.IsNullOrWhiteSpace(smallUnit))
                return packageUnit + " (P)";

            return packageUnit + " (P) = " +
                unitsPerPackage.ToString(CultureInfo.InvariantCulture) +
                " " + smallUnit + " (U)";
        }

        private static string Now()
        {
            return DateTime.Now.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private sealed class Target
        {
            public int ProductId { get; set; }
            public string Name { get; set; }
            public string Batch { get; set; }
            public string Expiry { get; set; }
            public string Unit { get; set; }
            public int UnitsPerPackage { get; set; }
            public string TargetType { get; set; }
            public string TargetKey { get; set; }
            public double Quantity { get; set; }
            public List<TargetBatch> Batches { get; set; }
        }

        private sealed class TargetBatch
        {
            public int BatchId { get; set; }
            public string BatchNumber { get; set; }
            public string ExpiryDate { get; set; }
            public double Quantity { get; set; }
            public double PurchasePrice { get; set; }
            public string ReceiptAt { get; set; }
            public int ReceiptItemId { get; set; }
        }

        private sealed class CostLayer
        {
            public int BatchId { get; set; }
            public double BookQuantity { get; set; }
            public double UnitCost { get; set; }
            public string ReceiptAt { get; set; }
            public int ReceiptItemId { get; set; }
        }
    }
}
