
using System;
using System.Data;
using System.Data.SQLite;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public sealed class InventoryAuditQueryService
    {
        private readonly string connectionString;

        public InventoryAuditQueryService()
        {
            var db = new DatabaseManager();
            db.InitializeDatabase();
            connectionString = "Data Source=" + db.GetDatabasePath() + ";Version=3;";
        }

        private bool IsManager(int userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(
                "SELECT Role FROM Users WHERE Id=@U AND IsActive=1;",
                connection))
            {
                command.Parameters.AddWithValue("@U", userId);
                connection.Open();

                object role = command.ExecuteScalar();

                return role != null &&
                    role != DBNull.Value &&
                    string.Equals(
                        Convert.ToString(role),
                        "Manager",
                        StringComparison.OrdinalIgnoreCase);
            }
        }

        private void EnsureActiveUser(int userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(
                "SELECT COUNT(*) FROM Users WHERE Id=@U AND IsActive=1;",
                connection))
            {
                command.Parameters.AddWithValue("@U", userId);
                connection.Open();

                if (Convert.ToInt32(command.ExecuteScalar()) != 1)
                    throw new UnauthorizedAccessException(
                        "المستخدم غير نشط أو غير موجود.");
            }
        }

        private void EnsureManager(int userId)
        {
            if (!IsManager(userId))
                throw new UnauthorizedAccessException(
                    "عرض هذه البيانات متاح للمدير فقط.");
        }

        public DataTable GetSessions(int userId, bool includeClosed)
        {
            EnsureManager(userId);
            const string sql = @"
SELECT s.Id,s.SessionNumber,s.ScopeType,s.Reason,s.Status,s.CreatedAt,
       COALESCE(u.FullName,'') AS CreatedByName,
       (SELECT COUNT(*) FROM InventoryCountItems i WHERE i.SessionId=s.Id) AS TotalItems,
       (SELECT COUNT(*) FROM InventoryCountItems i WHERE i.SessionId=s.Id
          AND i.ItemStatus IN ('Counted','Review','Approved')) AS CountedItems,
       (SELECT COUNT(*) FROM InventoryCountItems i WHERE i.SessionId=s.Id
          AND i.ItemStatus='Approved') AS ApprovedItems,
       (SELECT COUNT(*) FROM InventoryCountPendingMovements m WHERE m.SessionId=s.Id
          AND m.ProcessingStatus='Pending') AS PendingMovements,
       (SELECT COUNT(*) FROM InventoryCountPendingMovements m WHERE m.SessionId=s.Id
          AND m.ProcessingStatus='Failed') AS FailedMovements
FROM InventoryCountSessions s
LEFT JOIN Users u ON u.Id=s.CreatedBy
WHERE (@A=1 OR s.Status IN ('Open','Submitted','PartiallyApproved'))
ORDER BY s.Id DESC;";
            return ReadTable(sql, command =>
                command.Parameters.AddWithValue("@A", includeClosed ? 1 : 0));
        }

        public DataTable GetItems(int sessionId, int userId)
        {
            EnsureManager(userId);
            const string sql = @"
SELECT i.Id,
       i.ProductNameSnapshot AS ProductName,
       p.ProductCode,
       COALESCE((SELECT pb.Barcode FROM ProductBarcodes pb
                 WHERE pb.ProductId=i.ProductId ORDER BY pb.Id LIMIT 1),'') AS Barcode,
       'تشغيلة' AS TargetKind,
       i.BatchNumberSnapshot AS BatchNumber,
       i.ExpiryDateSnapshot AS ExpiryDate,
       i.UnitSnapshot AS Unit,
       COALESCE(NULLIF(i.UnitsPerPackageSnapshot,0),1) AS UnitsPerPackageSnapshot,
       COALESCE(p.UnitName,'') AS PUnitName,
       COALESCE(p.SmallUnitName,'') AS UUnitName,
       i.BookQuantity,
       i.ActualQuantity,
       i.ActualPQuantity,
       i.ActualUQuantity,
       i.NetVarianceQuantity AS VarianceQuantity,
       i.ItemStatus,
       i.CountedBy,
       COALESCE(c.FullName,'') AS CountedByName,
       i.ManagerNote,
       i.PurchasePriceSnapshot,
       i.BatchId,
       i.ProductId,
       i.TargetType,
       i.TargetKey
FROM InventoryCountItems i
INNER JOIN Products p ON p.Id=i.ProductId
LEFT JOIN Users c ON c.Id=i.CountedBy
WHERE i.SessionId=@S
ORDER BY i.ProductNameSnapshot,i.BatchNumberSnapshot,i.ExpiryDateSnapshot,i.Id;";
            return ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
        }

        public DataTable GetEmployees(int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT Id,FullName,Role
FROM Users
WHERE IsActive=1
  AND lower(Role)<>'manager'
ORDER BY FullName;";

            return ReadTable(sql, null);
        }

        public DataTable GetAvailableBatches(int managerId)
        {
            EnsureManager(managerId);
            const string sql = @"
SELECT b.Id AS BatchId,b.ProductId,p.Name AS ProductName,
       COALESCE(p.ProductCode,'') AS ProductCode,
       COALESCE(b.BatchNumber,'') AS BatchNumber,
       COALESCE(b.ExpiryDate,'') AS ExpiryDate,
       COALESCE(p.UnitName,'') AS Unit,
       COALESCE(p.SmallUnitName,'') AS SmallUnitName,
       CASE WHEN COALESCE(p.UnitsPerPackage,1)>0
            THEN p.UnitsPerPackage ELSE 1 END AS UnitsPerPackage,
       b.Quantity,b.PurchasePrice,
       'تشغيلة' AS TargetKind,
       'B:' || b.Id AS TargetKey
FROM Batches b
INNER JOIN Products p ON p.Id=b.ProductId
WHERE p.IsArchived=0
  AND NOT EXISTS
  (
      SELECT 1
      FROM InventoryCountItems i
      INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
      WHERE i.ProductId=b.ProductId
        AND i.ItemStatus NOT IN ('Approved','Cancelled')
        AND s.Status IN ('Open','Submitted','PartiallyApproved')
  )
ORDER BY p.Name,b.ExpiryDate,b.BatchNumber,b.Id;";
            return ReadTable(sql, null);
        }

        public DataTable GetPendingMovements(int sessionId, int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT m.Id,m.MovementType,m.SourceTable,m.SourceRecordId,
       p.Name AS ProductName,
       COALESCE(b.BatchNumber,'') AS BatchNumber,
       COALESCE(b.ExpiryDate,'') AS ExpiryDate,
       m.Quantity,m.UnitSnapshot,
       COALESCE(u.FullName,'') AS EmployeeName,
       m.OriginalCreatedAt,m.ProcessedAt,
       m.ProcessingStatus,m.ProcessingMessage
FROM InventoryCountPendingMovements m
INNER JOIN Products p ON p.Id=m.ProductId
LEFT JOIN Batches b ON b.Id=m.BatchId
LEFT JOIN Users u ON u.Id=m.EmployeeUserId
WHERE m.SessionId=@S
ORDER BY m.OriginalCreatedAt,m.Id;";

            return ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
        }

        public DataTable GetSessionEvents(int sessionId, int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT e.Id,e.EventType,e.Details,
       COALESCE(u.FullName,'') AS ActorName,
       e.CreatedAt
FROM InventoryCountEvents e
LEFT JOIN Users u ON u.Id=e.ActorUserId
WHERE e.SessionId=@S
ORDER BY e.Id DESC;";

            return ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
        }

        public DataTable GetVarianceReport(int sessionId, int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT i.Id AS ItemId,
       i.ProductNameSnapshot AS ProductName,
       CASE i.TargetType
           WHEN 'Batch' THEN 'تشغيلة'
           WHEN 'ExpiryGroup' THEN 'مجموعة صلاحية'
           ELSE 'رصيد الصنف'
       END AS TargetKind,
       i.BatchNumberSnapshot AS BatchNumber,
       i.ExpiryDateSnapshot AS ExpiryDate,
       i.UnitSnapshot AS Unit,
       i.BookQuantity,
       i.ActualQuantity,
       i.ActualPQuantity,
       i.ActualUQuantity,
       COALESCE(NULLIF(i.UnitsPerPackageSnapshot,0),1) AS UnitsPerPackageSnapshot,
       i.NetVarianceQuantity AS NetVariance,
       COALESCE(d.ShortageQuantity,0) AS ShortageQuantity,
       COALESCE(d.ExcessQuantity,0) AS ExcessQuantity,
       COALESCE(d.ShortageValue,0) AS ShortageValue,
       COALESCE(d.ExcessValue,0) AS ExcessValue,
       i.ItemStatus,
       COALESCE(u.FullName,'') AS ApprovedBy
FROM InventoryCountItems i
LEFT JOIN Users u ON u.Id=i.ApprovedBy
LEFT JOIN
(
    SELECT CountItemId,
           SUM(CASE WHEN DifferenceQuantity<0
                    THEN -DifferenceQuantity ELSE 0 END) AS ShortageQuantity,
           SUM(CASE WHEN DifferenceQuantity>0
                    THEN DifferenceQuantity ELSE 0 END) AS ExcessQuantity,
           SUM(CASE WHEN DifferenceQuantity<0
                    THEN -DifferenceQuantity*UnitCost ELSE 0 END) AS ShortageValue,
           SUM(CASE WHEN DifferenceQuantity>0
                    THEN DifferenceQuantity*UnitCost ELSE 0 END) AS ExcessValue
    FROM InventoryCountItemCostDecisions
    GROUP BY CountItemId
) d ON d.CountItemId=i.Id
WHERE i.SessionId=@S
  AND i.ItemStatus='Approved'
ORDER BY i.ProductNameSnapshot,i.TargetKey;";

            return ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
        }

        public DataTable GetVarianceSummary(int sessionId, int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT COUNT(*) AS ApprovedTargets,
       SUM(CASE WHEN i.NetVarianceQuantity<0
                THEN -i.NetVarianceQuantity ELSE 0 END) AS NetShortageQuantity,
       SUM(CASE WHEN i.NetVarianceQuantity>0
                THEN i.NetVarianceQuantity ELSE 0 END) AS NetExcessQuantity,
       SUM(COALESCE(d.ShortageQuantity,0)) AS ShortageQuantity,
       SUM(COALESCE(d.ExcessQuantity,0)) AS ExcessQuantity,
       SUM(COALESCE(d.ShortageValue,0)) AS ShortageValue,
       SUM(COALESCE(d.ExcessValue,0)) AS ExcessValue
FROM InventoryCountItems i
LEFT JOIN
(
    SELECT CountItemId,
           SUM(CASE WHEN DifferenceQuantity<0
                    THEN -DifferenceQuantity ELSE 0 END) AS ShortageQuantity,
           SUM(CASE WHEN DifferenceQuantity>0
                    THEN DifferenceQuantity ELSE 0 END) AS ExcessQuantity,
           SUM(CASE WHEN DifferenceQuantity<0
                    THEN -DifferenceQuantity*UnitCost ELSE 0 END) AS ShortageValue,
           SUM(CASE WHEN DifferenceQuantity>0
                    THEN DifferenceQuantity*UnitCost ELSE 0 END) AS ExcessValue
    FROM InventoryCountItemCostDecisions
    GROUP BY CountItemId
) d ON d.CountItemId=i.Id
WHERE i.SessionId=@S
  AND i.ItemStatus='Approved';";

            return ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
        }

        public DataTable GetCostDecisionDetails(int sessionId, int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT i.ProductNameSnapshot AS ProductName,
       CASE i.TargetType
           WHEN 'Batch' THEN 'تشغيلة'
           WHEN 'ExpiryGroup' THEN 'مجموعة صلاحية'
           ELSE 'رصيد الصنف'
       END AS TargetKind,
       i.BatchNumberSnapshot AS BatchNumber,
       i.ExpiryDateSnapshot AS ExpiryDate,
       d.DifferenceQuantity,
       d.UnitCost,
       ABS(d.DifferenceQuantity)*d.UnitCost AS DifferenceValue,
       CASE
           WHEN d.DifferenceQuantity<0 THEN 'عجز'
           WHEN d.DifferenceQuantity>0 THEN 'زيادة'
           ELSE 'بدون فرق'
       END AS DifferenceType,
       COALESCE(snap.BatchNumberSnapshot,b.BatchNumber,'') AS CostSourceBatch,
       COALESCE(snap.ReceiptAtSnapshot,'') AS CostSourceReceiptAt,
       d.EvaluationMethod,d.Reason,
       COALESCE(u.FullName,'') AS ManagerName,
       d.CreatedAt
FROM InventoryCountItemCostDecisions d
INNER JOIN InventoryCountItems i ON i.Id=d.CountItemId
LEFT JOIN InventoryCountItemBatchSnapshots snap
       ON snap.CountItemId=d.CountItemId
      AND snap.BatchId=d.SourceBatchId
LEFT JOIN Batches b ON b.Id=d.SourceBatchId
LEFT JOIN Users u ON u.Id=d.ManagerId
WHERE d.SessionId=@S
ORDER BY d.Id;";

            return ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
        }

        public DataTable GetPreviousCountComparison(int sessionId, int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT i.ProductNameSnapshot AS ProductName,
       CASE i.TargetType
           WHEN 'Batch' THEN 'تشغيلة'
           WHEN 'ExpiryGroup' THEN 'مجموعة صلاحية'
           ELSE 'رصيد الصنف'
       END AS TargetKind,
       i.BatchNumberSnapshot AS BatchNumber,
       i.ExpiryDateSnapshot AS ExpiryDate,
       i.NetVarianceQuantity AS CurrentVariance,
       COALESCE
       (
           (
               SELECT p.NetVarianceQuantity
               FROM InventoryCountItems p
               INNER JOIN InventoryCountSessions ps ON ps.Id=p.SessionId
               WHERE p.TargetKey=i.TargetKey
                 AND p.SessionId<>i.SessionId
                 AND p.ItemStatus='Approved'
                 AND ps.Status='Closed'
                 AND ps.CreatedAt<cs.CreatedAt
               ORDER BY ps.ClosedAt DESC,ps.Id DESC
               LIMIT 1
           ),0
       ) AS PreviousVariance,
       COALESCE
       (
           (
               SELECT ps.SessionNumber
               FROM InventoryCountItems p
               INNER JOIN InventoryCountSessions ps ON ps.Id=p.SessionId
               WHERE p.TargetKey=i.TargetKey
                 AND p.SessionId<>i.SessionId
                 AND p.ItemStatus='Approved'
                 AND ps.Status='Closed'
                 AND ps.CreatedAt<cs.CreatedAt
               ORDER BY ps.ClosedAt DESC,ps.Id DESC
               LIMIT 1
           ),'لا يوجد جرد سابق'
       ) AS PreviousSession,
       i.UnitSnapshot AS Unit
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions cs ON cs.Id=i.SessionId
WHERE i.SessionId=@S
  AND i.ItemStatus='Approved'
ORDER BY i.ProductNameSnapshot,i.TargetKey;";

            return ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
        }

        public DataTable GetRepeatedShortages(int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT i.ProductNameSnapshot AS ProductName,
       CASE i.TargetType
           WHEN 'Batch' THEN 'تشغيلة'
           WHEN 'ExpiryGroup' THEN 'مجموعة صلاحية'
           ELSE 'رصيد الصنف'
       END AS TargetKind,
       i.BatchNumberSnapshot AS BatchNumber,
       i.ExpiryDateSnapshot AS ExpiryDate,
       COUNT(DISTINCT i.SessionId) AS ShortageSessions,
       MAX(-i.NetVarianceQuantity) AS LargestShortageQuantity,
       MAX(s.ClosedAt) AS LatestShortageAt
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
CROSS JOIN InventoryCountSettings cfg
WHERE i.ItemStatus='Approved'
  AND i.NetVarianceQuantity<0
  AND s.Status='Closed'
GROUP BY i.TargetKey,i.ProductNameSnapshot,i.TargetType,
         i.BatchNumberSnapshot,i.ExpiryDateSnapshot
HAVING COUNT(DISTINCT i.SessionId)>=cfg.RepeatedShortageThreshold
ORDER BY ShortageSessions DESC,LatestShortageAt DESC;";

            return ReadTable(sql, null);
        }

        private DataTable ReadTable(
            string sql,
            Action<SQLiteCommand> configure)
        {
            var table = new DataTable();

            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(sql, connection))
            {
                if (configure != null)
                    configure(command);

                connection.Open();

                using (var reader = command.ExecuteReader())
                    table.Load(reader);
            }

            return table;
        }
    }
}
