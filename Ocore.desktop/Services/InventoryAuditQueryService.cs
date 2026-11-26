
using System;
using System.Globalization;
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
            EnsureActiveUser(userId);

            bool manager = IsManager(userId);

            const string sql = @"
SELECT s.Id,s.SessionNumber,s.ScopeType,s.Reason,s.Status,s.CreatedAt,
       COALESCE(u.FullName,'') AS CreatedByName,
       (SELECT COUNT(*) FROM InventoryCountItems i
        WHERE i.SessionId=s.Id) AS TotalItems,
       (SELECT COUNT(*) FROM InventoryCountItems i
        WHERE i.SessionId=s.Id
          AND i.ItemStatus IN ('Counted','Review','Approved')) AS CountedItems,
       (SELECT COUNT(*) FROM InventoryCountItems i
        WHERE i.SessionId=s.Id
          AND i.ItemStatus='Approved') AS ApprovedItems,
       (SELECT COUNT(*) FROM InventoryCountPendingMovements m
        WHERE m.SessionId=s.Id
          AND m.ProcessingStatus='Pending') AS PendingMovements,
       (SELECT COUNT(*) FROM InventoryCountPendingMovements m
        WHERE m.SessionId=s.Id
          AND m.ProcessingStatus='Failed') AS FailedMovements
FROM InventoryCountSessions s
LEFT JOIN Users u ON u.Id=s.CreatedBy
WHERE
(
    (@M=1 AND (@A=1 OR s.Status IN ('Open','Submitted','PartiallyApproved')))
    OR
    (
        @M=0
        AND s.Status IN ('Open','Submitted','PartiallyApproved')
        AND EXISTS
        (
            SELECT 1
            FROM InventoryCountItems i
            WHERE i.SessionId=s.Id
              AND i.AssignedUserId=@U
              AND i.ItemStatus NOT IN ('Approved','Cancelled')
        )
    )
)
ORDER BY s.Id DESC;";

            return ReadTable(sql, command =>
            {
                command.Parameters.AddWithValue("@M", manager ? 1 : 0);
                command.Parameters.AddWithValue(
                    "@A", includeClosed && manager ? 1 : 0);
                command.Parameters.AddWithValue("@U", userId);
            });
        }

        public DataTable GetItems(int sessionId, int userId)
        {
            EnsureActiveUser(userId);

            bool manager = IsManager(userId);

            const string sql = @"
SELECT i.Id,
       i.ProductNameSnapshot AS ProductName,
       p.ProductCode,
       COALESCE(
           (
               SELECT pb.Barcode
               FROM ProductBarcodes pb
               WHERE pb.ProductId=i.ProductId
               ORDER BY pb.Id
               LIMIT 1
           ),'') AS Barcode,
       CASE i.TargetType
           WHEN 'Batch' THEN 'تشغيلة'
           WHEN 'ExpiryGroup' THEN 'مجموعة صلاحية'
           ELSE 'رصيد الصنف'
       END AS TargetKind,
       i.BatchNumberSnapshot AS BatchNumber,
       i.ExpiryDateSnapshot AS ExpiryDate,
       i.UnitSnapshot AS Unit,
       CASE WHEN @M=1
            THEN i.BookQuantity
            ELSE NULL
       END AS BookQuantity,
       i.ActualQuantity,
       CASE WHEN @M=1
            THEN i.NetVarianceQuantity
            ELSE NULL
       END AS VarianceQuantity,
       i.ItemStatus,
       i.AssignedUserId,
       COALESCE(a.FullName,'غير مسند') AS AssignedEmployee,
       i.CountedBy,
       COALESCE(c.FullName,'') AS CountedByName,
       CASE WHEN @M=1
            THEN i.ManagerNote
            ELSE ''
       END AS ManagerNote,
       CASE WHEN @M=1
            THEN i.PurchasePriceSnapshot
            ELSE NULL
       END AS PurchasePriceSnapshot,
       i.BatchId,
       i.ProductId,
       i.TargetType,
       i.TargetKey
FROM InventoryCountItems i
INNER JOIN Products p ON p.Id=i.ProductId
LEFT JOIN Users a ON a.Id=i.AssignedUserId
LEFT JOIN Users c ON c.Id=i.CountedBy
WHERE i.SessionId=@S
  AND
  (
      @M=1
      OR i.AssignedUserId=@U
  )
ORDER BY i.Id;";

            return ReadTable(sql, command =>
            {
                command.Parameters.AddWithValue("@S", sessionId);
                command.Parameters.AddWithValue("@U", userId);
                command.Parameters.AddWithValue("@M", manager ? 1 : 0);
            });
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
       COALESCE((SELECT pb.Barcode FROM ProductBarcodes pb
                 WHERE pb.ProductId=p.Id ORDER BY pb.Id LIMIT 1),'') AS Barcode,
       COALESCE(b.BatchNumber,'') AS BatchNumber,
       COALESCE(b.ExpiryDate,'') AS ExpiryDate,
       COALESCE(p.UnitName,'') AS Unit,
       b.Quantity,b.PurchasePrice,
       CASE
           WHEN COALESCE(t.TrackBatchNumber,0)=1 THEN 'تشغيلة'
           WHEN COALESCE(t.TrackExpiryDate,0)=1 THEN 'مجموعة صلاحية'
           ELSE 'رصيد الصنف'
       END AS TargetKind,
       CASE
           WHEN COALESCE(t.TrackBatchNumber,0)=1
               THEN 'B:' || b.Id
           WHEN COALESCE(t.TrackExpiryDate,0)=1
               THEN 'E:' || p.Id || ':' ||
                    CASE
                        WHEN TRIM(COALESCE(b.ExpiryDate,''))=''
                            THEN '~NONE~'
                        ELSE TRIM(b.ExpiryDate)
                    END
           ELSE 'P:' || p.Id
       END AS TargetKey
FROM Batches b
INNER JOIN Products p ON p.Id=b.ProductId
LEFT JOIN InventoryProductTracking t ON t.ProductId=p.Id
WHERE COALESCE(p.IsArchived,0)=0
  AND NOT EXISTS
  (
      SELECT 1
      FROM InventoryCountItems i
      INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
      WHERE i.TargetKey =
          CASE
              WHEN COALESCE(t.TrackBatchNumber,0)=1
                  THEN 'B:' || b.Id
              WHEN COALESCE(t.TrackExpiryDate,0)=1
                  THEN 'E:' || p.Id || ':' ||
                       CASE
                           WHEN TRIM(COALESCE(b.ExpiryDate,''))=''
                               THEN '~NONE~'
                           ELSE TRIM(b.ExpiryDate)
                       END
              ELSE 'P:' || p.Id
          END
        AND i.ItemStatus NOT IN ('Approved','Cancelled')
        AND s.Status IN ('Open','Submitted','PartiallyApproved')
  )
ORDER BY p.Name,b.ExpiryDate,b.Id;";

            return ReadTable(sql, null);
        }


        public DataTable GetAvailableAuditTargets(int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT p.Id AS ProductId,p.Name AS ProductName,
       COALESCE(p.ProductCode,'') AS ProductCode,
       COALESCE((SELECT GROUP_CONCAT(pb.Barcode,'|') FROM ProductBarcodes pb
                 WHERE pb.ProductId=p.Id),'') AS Barcode,
       b.Id AS BatchId,
       COALESCE(b.BatchNumber,'') AS BatchNumber,
       COALESCE(b.ExpiryDate,'') AS ExpiryDate,
       COALESCE(p.UnitName,'') AS Unit,
       COALESCE(b.Quantity,0) AS Quantity,
       COALESCE(b.PurchasePrice,0) AS PurchasePrice,
       CASE WHEN b.Id IS NULL THEN 'كارتة الصنف' ELSE 'تشغيلة' END AS TargetKind,
       CASE WHEN b.Id IS NULL THEN 'P:' || p.Id ELSE 'B:' || b.Id END AS TargetKey
FROM Products p
LEFT JOIN Batches b ON b.ProductId=p.Id
WHERE COALESCE(p.IsArchived,0)=0
  AND
  (
      (b.Id IS NOT NULL AND NOT EXISTS
       (
           SELECT 1
           FROM InventoryCountItems i
           INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
           WHERE
             (
                 i.TargetKey='B:' || b.Id
                 OR i.TargetKey='P:' || p.Id
                 OR i.TargetKey='E:' || p.Id || ':' ||
                    CASE WHEN TRIM(COALESCE(b.ExpiryDate,''))='' THEN '~NONE~' ELSE TRIM(b.ExpiryDate) END
             )
             AND i.ItemStatus NOT IN ('Approved','Cancelled')
             AND s.Status IN ('Open','Submitted','PartiallyApproved')
       ))
      OR
      (b.Id IS NULL AND NOT EXISTS
       (
           SELECT 1
           FROM InventoryCountItems i
           INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
           WHERE i.TargetKey='P:' || p.Id
             AND i.ItemStatus NOT IN ('Approved','Cancelled')
             AND s.Status IN ('Open','Submitted','PartiallyApproved')
       ))
  )
ORDER BY p.Name,b.ExpiryDate,b.BatchNumber,b.Id;";

            return ReadTableWithoutConstraints(sql, null);
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
       i.NetVarianceQuantity AS NetVariance,
       COALESCE(NULLIF(i.UnitsPerPackageSnapshot,0),1) AS UnitsPerPackage,
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

            DataTable table = ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
            AddUnitBreakdown(table, "BookQuantity", "UnitsPerPackage", "Book");
            AddUnitBreakdown(table, "ActualQuantity", "UnitsPerPackage", "Actual");
            AddUnitBreakdown(table, "NetVariance", "UnitsPerPackage", "NetVariance");
            AddUnitBreakdown(table, "ShortageQuantity", "UnitsPerPackage", "Shortage");
            AddUnitBreakdown(table, "ExcessQuantity", "UnitsPerPackage", "Excess");
            return table;
        }

        public DataTable GetVarianceSummary(int sessionId, int managerId)
        {
            EnsureManager(managerId);

            const string sql = @"
SELECT COUNT(*) AS ApprovedTargets,
       SUM(CASE WHEN COALESCE(d.ShortageQuantity,0)>0 THEN 1 ELSE 0 END) AS ShortageTargets,
       SUM(CASE WHEN COALESCE(d.ExcessQuantity,0)>0 THEN 1 ELSE 0 END) AS ExcessTargets,
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
       COALESCE(NULLIF(i.UnitsPerPackageSnapshot,0),1) AS UnitsPerPackage,
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

            DataTable table = ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
            AddUnitBreakdown(table, "DifferenceQuantity", "UnitsPerPackage", "Difference");
            return table;
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
       COALESCE(NULLIF(i.UnitsPerPackageSnapshot,0),1) AS UnitsPerPackage,
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
       COALESCE(
           (
               SELECT COALESCE(NULLIF(p.UnitsPerPackageSnapshot,0),1)
               FROM InventoryCountItems p
               INNER JOIN InventoryCountSessions ps ON ps.Id=p.SessionId
               WHERE p.TargetKey=i.TargetKey AND p.SessionId<>i.SessionId
                 AND p.ItemStatus='Approved' AND ps.Status='Closed'
                 AND ps.CreatedAt<cs.CreatedAt
               ORDER BY ps.ClosedAt DESC,ps.Id DESC LIMIT 1
           ),COALESCE(NULLIF(i.UnitsPerPackageSnapshot,0),1)
       ) AS PreviousUnitsPerPackage,
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

            DataTable table = ReadTable(sql, command =>
                command.Parameters.AddWithValue("@S", sessionId));
            AddUnitBreakdown(table, "CurrentVariance", "UnitsPerPackage", "CurrentVariance");
            AddUnitBreakdown(table, "PreviousVariance", "PreviousUnitsPerPackage", "PreviousVariance");
            return table;
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
       COALESCE(NULLIF(i.UnitsPerPackageSnapshot,0),1) AS UnitsPerPackage,
       MAX(s.ClosedAt) AS LatestShortageAt
FROM InventoryCountItems i
INNER JOIN InventoryCountSessions s ON s.Id=i.SessionId
CROSS JOIN InventoryCountSettings cfg
WHERE i.ItemStatus='Approved'
  AND i.NetVarianceQuantity<0
  AND s.Status='Closed'
GROUP BY i.TargetKey,i.ProductNameSnapshot,i.TargetType,
         i.BatchNumberSnapshot,i.ExpiryDateSnapshot,
         COALESCE(NULLIF(i.UnitsPerPackageSnapshot,0),1)
HAVING COUNT(DISTINCT i.SessionId)>=cfg.RepeatedShortageThreshold
ORDER BY ShortageSessions DESC,LatestShortageAt DESC;";

            DataTable table = ReadTable(sql, null);
            AddUnitBreakdown(table, "LargestShortageQuantity", "UnitsPerPackage", "LargestShortage");
            return table;
        }

        private static void AddUnitBreakdown(DataTable table, string quantityColumn, string factorColumn, string prefix)
        {
            if (table == null || !table.Columns.Contains(quantityColumn) || !table.Columns.Contains(factorColumn)) return;
            string pColumn = prefix + "PQuantity", uColumn = prefix + "UQuantity";
            if (!table.Columns.Contains(pColumn)) table.Columns.Add(pColumn, typeof(long));
            if (!table.Columns.Contains(uColumn)) table.Columns.Add(uColumn, typeof(long));
            foreach (DataRow row in table.Rows)
            {
                double quantity = row[quantityColumn] == DBNull.Value ? 0d : Convert.ToDouble(row[quantityColumn], CultureInfo.InvariantCulture);
                int factor = row[factorColumn] == DBNull.Value ? 1 : Convert.ToInt32(row[factorColumn], CultureInfo.InvariantCulture);
                if (factor <= 0) throw new InvalidOperationException("معامل تحويل الوحدات غير صحيح في تقرير الجرد.");
                long totalSmallUnits = checked((long)Math.Round(Math.Abs(quantity) * factor, 0, MidpointRounding.AwayFromZero));
                long sign = quantity < 0 ? -1L : 1L;
                row[pColumn] = (totalSmallUnits / factor) * sign;
                row[uColumn] = (totalSmallUnits % factor) * sign;
            }
        }

        private DataTable ReadTableWithoutConstraints(
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
                {
                    // Build a plain schema ourselves. DataTable.Load(reader) may import
                    // SQLite key metadata as a unique constraint, but this query
                    // intentionally returns multiple batch rows for the same ProductId.
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        Type fieldType = reader.GetFieldType(i) ?? typeof(object);
                        var column = new DataColumn(reader.GetName(i), fieldType)
                        {
                            AllowDBNull = true
                        };
                        table.Columns.Add(column);
                    }

                    var values = new object[reader.FieldCount];
                    while (reader.Read())
                    {
                        reader.GetValues(values);
                        var row = table.NewRow();
                        for (int i = 0; i < values.Length; i++)
                            row[i] = values[i] ?? DBNull.Value;
                        table.Rows.Add(row);
                    }
                }
            }

            return table;
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
