using System;
using System.Data.SQLite;

namespace Ocore.desktop.Data
{
    public static class InventoryAuditSchema
    {
        public static void Ensure(SQLiteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            const string sql = @"
CREATE TABLE IF NOT EXISTS InventoryCountSessions (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SessionNumber TEXT NOT NULL UNIQUE,
 ScopeType TEXT NOT NULL CHECK(ScopeType IN ('Full','Selected')),
 Reason TEXT NOT NULL,
 Status TEXT NOT NULL DEFAULT 'Open' CHECK(Status IN ('Open','Submitted','PartiallyApproved','Closed','Cancelled')),
 CreatedBy INTEGER NOT NULL, CreatedAt TEXT NOT NULL, SubmittedAt TEXT,
 ClosedBy INTEGER, ClosedAt TEXT, CancelledBy INTEGER, CancelledAt TEXT, CancelReason TEXT,
 FOREIGN KEY(CreatedBy) REFERENCES Users(Id),
 FOREIGN KEY(ClosedBy) REFERENCES Users(Id),
 FOREIGN KEY(CancelledBy) REFERENCES Users(Id)
);
CREATE INDEX IF NOT EXISTS IX_InventoryCountSessions_Status
ON InventoryCountSessions(Status,CreatedAt);

CREATE TABLE IF NOT EXISTS InventoryCountItems (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SessionId INTEGER NOT NULL,
 ProductId INTEGER NOT NULL,
 BatchId INTEGER,
 TargetType TEXT NOT NULL DEFAULT 'Batch',
 TargetKey TEXT NOT NULL,
 ProductNameSnapshot TEXT NOT NULL,
 BatchNumberSnapshot TEXT NOT NULL DEFAULT '',
 ExpiryDateSnapshot TEXT NOT NULL DEFAULT '',
 UnitSnapshot TEXT NOT NULL DEFAULT '',
 BookQuantity REAL NOT NULL,
 PurchasePriceSnapshot REAL NOT NULL DEFAULT 0,

 ActualQuantity REAL,
 ActualPQuantity INTEGER,
 ActualUQuantity INTEGER,
 UnitsPerPackageSnapshot INTEGER NOT NULL DEFAULT 1,

 AppliedQuantity REAL,
 NetVarianceQuantity REAL NOT NULL DEFAULT 0,

 ItemStatus TEXT NOT NULL DEFAULT 'Pending'
 CHECK(ItemStatus IN ('Pending','Counting','Counted','Review','RecountRequired','Approved','Cancelled')),

 AssignedUserId INTEGER,
 CountedBy INTEGER,
 CountedAt TEXT,

 LockUserId INTEGER,
 LockToken TEXT,
 LockUntil TEXT,

 ApprovedBy INTEGER,
 ApprovedAt TEXT,
 ManagerNote TEXT NOT NULL DEFAULT '',
 CreatedAt TEXT NOT NULL,

 FOREIGN KEY(SessionId) REFERENCES InventoryCountSessions(Id),
 FOREIGN KEY(ProductId) REFERENCES Products(Id),
 FOREIGN KEY(BatchId) REFERENCES Batches(Id),
 FOREIGN KEY(AssignedUserId) REFERENCES Users(Id),
 FOREIGN KEY(CountedBy) REFERENCES Users(Id),
 FOREIGN KEY(LockUserId) REFERENCES Users(Id),
 FOREIGN KEY(ApprovedBy) REFERENCES Users(Id),

 UNIQUE(SessionId,TargetKey)
);

CREATE INDEX IF NOT EXISTS IX_InventoryCountItems_SessionStatus
ON InventoryCountItems(SessionId,ItemStatus);

CREATE INDEX IF NOT EXISTS IX_InventoryCountItems_Batch
ON InventoryCountItems(BatchId,ItemStatus);

CREATE INDEX IF NOT EXISTS IX_InventoryCountItems_Assignee
ON InventoryCountItems(AssignedUserId,ItemStatus);

CREATE TABLE IF NOT EXISTS InventoryCountItemBatchSnapshots (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 CountItemId INTEGER NOT NULL,
 BatchId INTEGER NOT NULL,
 BookQuantity REAL NOT NULL,
 PurchasePriceSnapshot REAL NOT NULL DEFAULT 0,
 BatchNumberSnapshot TEXT NOT NULL DEFAULT '',
 ExpiryDateSnapshot TEXT NOT NULL DEFAULT '',
 UnitSnapshot TEXT NOT NULL DEFAULT '',
 ReceiptAtSnapshot TEXT NOT NULL DEFAULT '',
 ReceiptItemIdSnapshot INTEGER NOT NULL DEFAULT 0,
 CreatedAt TEXT NOT NULL,
 FOREIGN KEY(CountItemId) REFERENCES InventoryCountItems(Id),
 FOREIGN KEY(BatchId) REFERENCES Batches(Id),
 UNIQUE(CountItemId,BatchId)
);

CREATE INDEX IF NOT EXISTS IX_InventoryCountItemBatchSnapshots_Batch
ON InventoryCountItemBatchSnapshots(BatchId,CountItemId);

CREATE TRIGGER IF NOT EXISTS TR_InventoryCountItemBatchSnapshots_NoUpdate
BEFORE UPDATE ON InventoryCountItemBatchSnapshots
BEGIN
 SELECT RAISE(ABORT,'Inventory count batch snapshots are immutable.');
END;

CREATE TRIGGER IF NOT EXISTS TR_InventoryCountItemBatchSnapshots_NoDelete
BEFORE DELETE ON InventoryCountItemBatchSnapshots
BEGIN
 SELECT RAISE(ABORT,'Inventory count batch snapshots are immutable.');
END;

CREATE TABLE IF NOT EXISTS InventoryCountItemCostDecisions (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SessionId INTEGER NOT NULL,
 CountItemId INTEGER NOT NULL,
 DifferenceQuantity REAL NOT NULL,
 EvaluationMethod TEXT NOT NULL,
 UnitCost REAL NOT NULL,
 SourceBatchId INTEGER,
 Notes TEXT NOT NULL DEFAULT '',
 Reason TEXT NOT NULL,
 ManagerId INTEGER NOT NULL,
 CreatedAt TEXT NOT NULL,
 FOREIGN KEY(SessionId) REFERENCES InventoryCountSessions(Id),
 FOREIGN KEY(CountItemId) REFERENCES InventoryCountItems(Id),
 FOREIGN KEY(SourceBatchId) REFERENCES Batches(Id),
 FOREIGN KEY(ManagerId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_InventoryCountItemCostDecisions_Item
ON InventoryCountItemCostDecisions(CountItemId,Id);

CREATE TRIGGER IF NOT EXISTS TR_InventoryCountItemCostDecisions_NoUpdate
BEFORE UPDATE ON InventoryCountItemCostDecisions
BEGIN
 SELECT RAISE(ABORT,'Inventory count cost decisions are append-only.');
END;

CREATE TRIGGER IF NOT EXISTS TR_InventoryCountItemCostDecisions_NoDelete
BEFORE DELETE ON InventoryCountItemCostDecisions
BEGIN
 SELECT RAISE(ABORT,'Inventory count cost decisions are append-only.');
END;

CREATE TABLE IF NOT EXISTS InventoryCountRevisions (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 CountItemId INTEGER NOT NULL,

 PreviousActualQuantity REAL,
 NewActualQuantity REAL,

 PreviousActualPQuantity INTEGER,
 PreviousActualUQuantity INTEGER,
 NewActualPQuantity INTEGER,
 NewActualUQuantity INTEGER,

 PreviousStatus TEXT NOT NULL,
 NewStatus TEXT NOT NULL,
 ChangedBy INTEGER NOT NULL,
 Reason TEXT NOT NULL DEFAULT '',
 CreatedAt TEXT NOT NULL,

 FOREIGN KEY(CountItemId) REFERENCES InventoryCountItems(Id),
 FOREIGN KEY(ChangedBy) REFERENCES Users(Id)
);

CREATE TRIGGER IF NOT EXISTS TR_InventoryCountRevisions_NoUpdate
BEFORE UPDATE ON InventoryCountRevisions
BEGIN
 SELECT RAISE(ABORT,'Inventory count revisions are append-only.');
END;

CREATE TRIGGER IF NOT EXISTS TR_InventoryCountRevisions_NoDelete
BEFORE DELETE ON InventoryCountRevisions
BEGIN
 SELECT RAISE(ABORT,'Inventory count revisions are append-only.');
END;

CREATE TABLE IF NOT EXISTS InventoryCountEvents (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SessionId INTEGER NOT NULL,
 CountItemId INTEGER,
 EventType TEXT NOT NULL,
 ActorUserId INTEGER NOT NULL,
 Details TEXT NOT NULL DEFAULT '',
 CreatedAt TEXT NOT NULL,
 FOREIGN KEY(SessionId) REFERENCES InventoryCountSessions(Id),
 FOREIGN KEY(CountItemId) REFERENCES InventoryCountItems(Id),
 FOREIGN KEY(ActorUserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_InventoryCountEvents_Session
ON InventoryCountEvents(SessionId,Id);

CREATE TRIGGER IF NOT EXISTS TR_InventoryCountEvents_NoUpdate
BEFORE UPDATE ON InventoryCountEvents
BEGIN
 SELECT RAISE(ABORT,'Inventory count event history is append-only.');
END;

CREATE TRIGGER IF NOT EXISTS TR_InventoryCountEvents_NoDelete
BEFORE DELETE ON InventoryCountEvents
BEGIN
 SELECT RAISE(ABORT,'Inventory count event history is append-only.');
END;

CREATE TABLE IF NOT EXISTS InventoryCountMessages (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SessionId INTEGER NOT NULL,
 CountItemId INTEGER,
 SenderUserId INTEGER NOT NULL,
 RecipientUserId INTEGER NOT NULL,
 MessageText TEXT NOT NULL,
 SentAt TEXT NOT NULL,
 ReadAt TEXT,
 ReplyText TEXT,
 RepliedAt TEXT,
 FOREIGN KEY(SessionId) REFERENCES InventoryCountSessions(Id),
 FOREIGN KEY(CountItemId) REFERENCES InventoryCountItems(Id),
 FOREIGN KEY(SenderUserId) REFERENCES Users(Id),
 FOREIGN KEY(RecipientUserId) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS InventoryCountPendingMovements (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 SessionId INTEGER NOT NULL,
 ProductId INTEGER NOT NULL,
 BatchId INTEGER,
 CountItemId INTEGER,
 TargetKey TEXT NOT NULL DEFAULT '',
 MovementType TEXT NOT NULL,
 SourceTable TEXT NOT NULL,
 SourceRecordId INTEGER NOT NULL,
 Quantity REAL NOT NULL,
 UnitSnapshot TEXT NOT NULL DEFAULT '',
 EmployeeUserId INTEGER NOT NULL,
 OriginalCreatedAt TEXT NOT NULL,
 ProcessedAt TEXT,
 ProcessingStatus TEXT NOT NULL DEFAULT 'Pending'
 CHECK(ProcessingStatus IN ('Pending','Processed','Failed','Cancelled')),
 ProcessingMessage TEXT NOT NULL DEFAULT '',
 FOREIGN KEY(SessionId) REFERENCES InventoryCountSessions(Id),
 FOREIGN KEY(ProductId) REFERENCES Products(Id),
 FOREIGN KEY(BatchId) REFERENCES Batches(Id),
 FOREIGN KEY(EmployeeUserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_InventoryCountPendingMovements_Session
ON InventoryCountPendingMovements(SessionId,ProcessingStatus,OriginalCreatedAt);

CREATE TABLE IF NOT EXISTS InventoryCountSettings (
 Id INTEGER PRIMARY KEY CHECK(Id=1),
 RepeatedShortageThreshold INTEGER NOT NULL DEFAULT 2,
 UpdatedBy INTEGER,
 UpdatedAt TEXT,
 FOREIGN KEY(UpdatedBy) REFERENCES Users(Id)
);

INSERT OR IGNORE INTO InventoryCountSettings(Id) VALUES(1);

CREATE TABLE IF NOT EXISTS InventoryProductTracking (
 ProductId INTEGER PRIMARY KEY,
 TrackBatchNumber INTEGER NOT NULL DEFAULT 0 CHECK(TrackBatchNumber IN (0,1)),
 TrackExpiryDate INTEGER NOT NULL DEFAULT 0 CHECK(TrackExpiryDate IN (0,1)),
 CreatedBy INTEGER,
 CreatedAt TEXT NOT NULL,
 UpdatedBy INTEGER,
 UpdatedAt TEXT NOT NULL,
 FOREIGN KEY(ProductId) REFERENCES Products(Id),
 FOREIGN KEY(CreatedBy) REFERENCES Users(Id),
 FOREIGN KEY(UpdatedBy) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS InventoryProductTrackingHistory (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 ProductId INTEGER NOT NULL,
 OldTrackBatchNumber INTEGER NOT NULL,
 OldTrackExpiryDate INTEGER NOT NULL,
 NewTrackBatchNumber INTEGER NOT NULL,
 NewTrackExpiryDate INTEGER NOT NULL,
 CurrentStockAtChange REAL NOT NULL DEFAULT 0,
 ChangedBy INTEGER NOT NULL,
 Reason TEXT NOT NULL,
 CreatedAt TEXT NOT NULL,
 FOREIGN KEY(ProductId) REFERENCES Products(Id),
 FOREIGN KEY(ChangedBy) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_InventoryProductTrackingHistory_Product
ON InventoryProductTrackingHistory(ProductId,Id);

CREATE TRIGGER IF NOT EXISTS TR_InventoryProductTrackingHistory_NoUpdate
BEFORE UPDATE ON InventoryProductTrackingHistory
BEGIN
 SELECT RAISE(ABORT,'Tracking history is append-only.');
END;

CREATE TRIGGER IF NOT EXISTS TR_InventoryProductTrackingHistory_NoDelete
BEFORE DELETE ON InventoryProductTrackingHistory
BEGIN
 SELECT RAISE(ABORT,'Tracking history is append-only.');
END;

INSERT OR IGNORE INTO InventoryProductTracking
(ProductId,TrackBatchNumber,TrackExpiryDate,CreatedAt,UpdatedAt)
SELECT p.Id,
 CASE WHEN EXISTS(
   SELECT 1 FROM Batches b
   WHERE b.ProductId=p.Id AND TRIM(COALESCE(b.BatchNumber,''))<>''
 ) THEN 1 ELSE 0 END,
 CASE WHEN EXISTS(
   SELECT 1 FROM Batches b
   WHERE b.ProductId=p.Id AND TRIM(COALESCE(b.ExpiryDate,''))<>''
 ) THEN 1 ELSE 0 END,
 strftime('%Y-%m-%d %H:%M:%S','now','localtime'),
 strftime('%Y-%m-%d %H:%M:%S','now','localtime')
FROM Products p WHERE p.IsArchived=0;
";

            using (var command = new SQLiteCommand(sql, connection))
                command.ExecuteNonQuery();

            EnsureColumn(connection, "InventoryCountItems", "TargetType",
                "TEXT NOT NULL DEFAULT 'Batch'");

            EnsureColumn(connection, "InventoryCountItems", "AppliedQuantity", "REAL");

            EnsureColumn(connection, "InventoryCountItems", "NetVarianceQuantity",
                "REAL NOT NULL DEFAULT 0");

            EnsureColumn(connection, "InventoryCountItems", "ActualPQuantity", "INTEGER");

            EnsureColumn(connection, "InventoryCountItems", "ActualUQuantity", "INTEGER");

            EnsureColumn(connection, "InventoryCountItems", "UnitsPerPackageSnapshot",
                "INTEGER NOT NULL DEFAULT 1");

            EnsureColumn(connection, "InventoryCountPendingMovements", "CountItemId", "INTEGER");

            EnsureColumn(connection, "InventoryCountPendingMovements", "TargetKey",
                "TEXT NOT NULL DEFAULT ''");

            EnsureColumn(connection, "InventoryCountItemBatchSnapshots", "ReceiptAtSnapshot",
                "TEXT NOT NULL DEFAULT ''");

            EnsureColumn(connection, "InventoryCountItemBatchSnapshots", "ReceiptItemIdSnapshot",
                "INTEGER NOT NULL DEFAULT 0");

            EnsureColumn(connection, "InventoryCountRevisions", "PreviousActualPQuantity", "INTEGER");

            EnsureColumn(connection, "InventoryCountRevisions", "PreviousActualUQuantity", "INTEGER");

            EnsureColumn(connection, "InventoryCountRevisions", "NewActualPQuantity", "INTEGER");

            EnsureColumn(connection, "InventoryCountRevisions", "NewActualUQuantity", "INTEGER");

            using (var index = new SQLiteCommand(
                @"CREATE INDEX IF NOT EXISTS IX_InventoryCountPendingMovements_Target
                  ON InventoryCountPendingMovements(TargetKey,ProcessingStatus);",
                connection))
            {
                index.ExecuteNonQuery();
            }

            // Preserve legacy duplicate rows; do not silently delete history.
            int duplicateSourceKeys;

            using (var duplicates = new SQLiteCommand(@"
SELECT COUNT(*) FROM (
    SELECT SourceTable,SourceRecordId,MovementType
    FROM InventoryCountPendingMovements
    GROUP BY SourceTable,SourceRecordId,MovementType
    HAVING COUNT(*) > 1
);", connection))
            {
                duplicateSourceKeys = Convert.ToInt32(duplicates.ExecuteScalar());
            }

            if (duplicateSourceKeys == 0)
            {
                using (var uniqueIndex = new SQLiteCommand(@"
CREATE UNIQUE INDEX IF NOT EXISTS UX_InventoryCountPendingMovements_Source
ON InventoryCountPendingMovements(SourceTable,SourceRecordId,MovementType);",
                    connection))
                {
                    uniqueIndex.ExecuteNonQuery();
                }
            }
        }

        private static void EnsureColumn(
            SQLiteConnection connection,
            string tableName,
            string columnName,
            string definition)
        {
            bool exists = false;

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(" + tableName + ");", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(
                        Convert.ToString(reader["name"]),
                        columnName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (!exists)
            {
                using (var command = new SQLiteCommand(
                    "ALTER TABLE " + tableName + " ADD COLUMN " +
                    columnName + " " + definition + ";", connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}