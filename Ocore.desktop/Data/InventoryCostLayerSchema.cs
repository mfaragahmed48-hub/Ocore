using System;
using System.Data.SQLite;

namespace Ocore.desktop.Data
{
    /// <summary>
    /// Creates the append-oriented cost-layer schema used only for products
    /// that do not track batch numbers and do not track expiry dates.
    /// This schema is additive and does not rewrite historical stock records.
    /// </summary>
    internal static class InventoryCostLayerSchema
    {
        public static void Ensure(SQLiteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException("connection");

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Execute(connection, transaction, @"
CREATE TABLE IF NOT EXISTS InventoryCostLayers
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    BatchId INTEGER NOT NULL,
    SourceType TEXT NOT NULL
        CHECK (SourceType IN ('Purchase','Opening','Adjustment','Legacy','CountVariance')),
    SourceRecordId INTEGER,
    SourceItemId INTEGER,
    ReceivedAt TEXT NOT NULL,
    UnitCost REAL NOT NULL CHECK (UnitCost >= 0),
    OriginalQuantity REAL NOT NULL CHECK (OriginalQuantity >= 0),
    RemainingQuantity REAL NOT NULL CHECK (RemainingQuantity >= 0),
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (ProductId) REFERENCES Products(Id),
    FOREIGN KEY (BatchId) REFERENCES Batches(Id)
);");
                    Execute(connection, transaction, @"
CREATE INDEX IF NOT EXISTS IX_InventoryCostLayers_ProductFifo
ON InventoryCostLayers(ProductId, ReceivedAt, Id);");
                    Execute(connection, transaction, @"
CREATE INDEX IF NOT EXISTS IX_InventoryCostLayers_Batch
ON InventoryCostLayers(BatchId, RemainingQuantity);");
                    Execute(connection, transaction, @"
CREATE UNIQUE INDEX IF NOT EXISTS UX_InventoryCostLayers_PurchaseItem
ON InventoryCostLayers(SourceItemId)
WHERE SourceType='Purchase' AND SourceItemId IS NOT NULL;");

                    Execute(connection, transaction, @"
CREATE TABLE IF NOT EXISTS SalesInvoiceCostAllocations
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceBatchAllocationId INTEGER NOT NULL,
    CostLayerId INTEGER NOT NULL,
    Quantity REAL NOT NULL CHECK (Quantity > 0),
    UnitCost REAL NOT NULL CHECK (UnitCost >= 0),
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (InvoiceBatchAllocationId)
        REFERENCES SalesInvoiceBatchAllocations(Id),
    FOREIGN KEY (CostLayerId)
        REFERENCES InventoryCostLayers(Id)
);");
                    Execute(connection, transaction, @"
CREATE INDEX IF NOT EXISTS IX_SalesInvoiceCostAllocations_BatchAllocation
ON SalesInvoiceCostAllocations(InvoiceBatchAllocationId);");
                    Execute(connection, transaction, @"
CREATE INDEX IF NOT EXISTS IX_SalesInvoiceCostAllocations_CostLayer
ON SalesInvoiceCostAllocations(CostLayerId);");

                    Execute(connection, transaction, @"
CREATE TABLE IF NOT EXISTS SalesReturnCostAllocations
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnBatchAllocationId INTEGER NOT NULL,
    SourceSalesCostAllocationId INTEGER NOT NULL,
    CostLayerId INTEGER NOT NULL,
    Quantity REAL NOT NULL CHECK (Quantity > 0),
    UnitCost REAL NOT NULL CHECK (UnitCost >= 0),
    Restocked INTEGER NOT NULL DEFAULT 0 CHECK (Restocked IN (0,1)),
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (ReturnBatchAllocationId)
        REFERENCES SalesReturnBatchAllocations(Id),
    FOREIGN KEY (SourceSalesCostAllocationId)
        REFERENCES SalesInvoiceCostAllocations(Id),
    FOREIGN KEY (CostLayerId)
        REFERENCES InventoryCostLayers(Id)
);");
                    Execute(connection, transaction, @"
CREATE INDEX IF NOT EXISTS IX_SalesReturnCostAllocations_ReturnAllocation
ON SalesReturnCostAllocations(ReturnBatchAllocationId);");
                    Execute(connection, transaction, @"
CREATE INDEX IF NOT EXISTS IX_SalesReturnCostAllocations_SourceSaleCost
ON SalesReturnCostAllocations(SourceSalesCostAllocationId);");

                    Execute(connection, transaction, @"
CREATE TABLE IF NOT EXISTS InventoryCostLayerMovements
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CostLayerId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    MovementType TEXT NOT NULL
        CHECK (MovementType IN
            ('Receipt','Sale','SaleReturn','AdjustmentIncrease',
             'AdjustmentDecrease','CountVariance','Opening')),
    QuantityChange REAL NOT NULL CHECK (QuantityChange <> 0),
    UnitCost REAL NOT NULL CHECK (UnitCost >= 0),
    ReferenceTable TEXT NOT NULL DEFAULT '',
    ReferenceId INTEGER,
    ReferenceItemId INTEGER,
    Notes TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (CostLayerId) REFERENCES InventoryCostLayers(Id),
    FOREIGN KEY (ProductId) REFERENCES Products(Id)
);");
                    Execute(connection, transaction, @"
CREATE INDEX IF NOT EXISTS IX_InventoryCostLayerMovements_Layer
ON InventoryCostLayerMovements(CostLayerId, CreatedAt, Id);");
                    Execute(connection, transaction, @"
CREATE INDEX IF NOT EXISTS IX_InventoryCostLayerMovements_Product
ON InventoryCostLayerMovements(ProductId, CreatedAt, Id);");

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static void Execute(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string sql)
        {
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }
    }
}
