using System;
using System.Data.SQLite;

namespace Ocore.desktop.Services
{
    /// <summary>
    /// Handles cost layers for products that do not track batch numbers
    /// and do not track expiry dates.
    /// All methods participate in the caller's SQLite transaction.
    /// </summary>
    internal static class InventoryCostLayerService
    {
        public static bool IsUntrackedProduct(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int productId)
        {
            using (var command = new SQLiteCommand(@"
SELECT TrackBatchNumber, TrackExpiryDate
FROM InventoryProductTracking
WHERE ProductId = @ProductId;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@ProductId", productId);

                using (var reader = command.ExecuteReader())
                {
                    // Fail closed if tracking settings are unexpectedly missing.
                    if (!reader.Read())
                        return false;

                    return Convert.ToInt32(reader["TrackBatchNumber"]) == 0 &&
                           Convert.ToInt32(reader["TrackExpiryDate"]) == 0;
                }
            }
        }

        public static int GetOrCreateUntrackedBatch(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int productId,
            int supplierId,
            double unitCost,
            double receivedQuantity,
            int? activeAuditSessionId)
        {
            int batchId;
            bool existingBatch = false;

            using (var command = new SQLiteCommand(@"
SELECT Id
FROM Batches
WHERE ProductId = @ProductId
  AND TRIM(COALESCE(BatchNumber, '')) = ''
  AND TRIM(COALESCE(ExpiryDate, '')) = ''
ORDER BY Id
LIMIT 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@ProductId", productId);
                object existing = command.ExecuteScalar();

                if (existing != null && existing != DBNull.Value)
                {
                    batchId = Convert.ToInt32(existing);
                    existingBatch = true;
                }
                else
                {
                    using (var insert = new SQLiteCommand(@"
INSERT INTO Batches
(
    ProductId,
    SupplierId,
    BatchNumber,
    ExpiryDate,
    PurchasePrice,
    Quantity
)
VALUES
(
    @ProductId,
    @SupplierId,
    NULL,
    '',
    @UnitCost,
    0
);
SELECT last_insert_rowid();",
                        connection,
                        transaction))
                    {
                        insert.Parameters.AddWithValue("@ProductId", productId);
                        insert.Parameters.AddWithValue("@SupplierId", supplierId);
                        insert.Parameters.AddWithValue("@UnitCost", unitCost);
                        batchId = Convert.ToInt32(insert.ExecuteScalar());
                    }
                }
            }

            if (existingBatch)
            {
                EnsureLegacyLayerCoverage(
                    connection, transaction, productId, batchId,
                    activeAuditSessionId);
            }

            using (var update = new SQLiteCommand(@"
UPDATE Batches
SET
    PurchasePrice = @UnitCost,
    Quantity = CASE
        WHEN @DeferQuantity = 1 THEN Quantity
        ELSE Quantity + @ReceivedQuantity
    END
WHERE Id = @BatchId
  AND ProductId = @ProductId;",
                connection,
                transaction))
            {
                update.Parameters.AddWithValue("@UnitCost", unitCost);
                update.Parameters.AddWithValue(
                    "@DeferQuantity",
                    activeAuditSessionId.HasValue ? 1 : 0);
                update.Parameters.AddWithValue("@ReceivedQuantity", receivedQuantity);
                update.Parameters.AddWithValue("@BatchId", batchId);
                update.Parameters.AddWithValue("@ProductId", productId);

                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "تعذر تحديث رصيد التشغيلة الموحدة للصنف.");
            }

            return batchId;
        }


        private static void EnsureLegacyLayerCoverage(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int productId,
            int batchId,
            int? activeAuditSessionId)
        {
            double batchQuantity;
            double previousUnitCost;
            string receivedAt;

            using (var command = new SQLiteCommand(@"
SELECT
    COALESCE(b.Quantity,0) AS Quantity,
    COALESCE(b.PurchasePrice,0) AS PurchasePrice,
    COALESCE((
        SELECT MIN(pi.CreatedAt)
        FROM PurchaseInvoiceItems pii
        INNER JOIN PurchaseInvoices pi ON pi.Id=pii.InvoiceId
        WHERE pii.BatchId=b.Id
    ),'') AS ReceivedAt
FROM Batches b
WHERE b.Id=@BatchId AND b.ProductId=@ProductId;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@BatchId", batchId);
                command.Parameters.AddWithValue("@ProductId", productId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        throw new InvalidOperationException(
                            "تعذر قراءة رصيد التشغيلة القديمة قبل تحديث تكلفتها.");
                    batchQuantity = Convert.ToDouble(reader["Quantity"]);
                    previousUnitCost = Convert.ToDouble(reader["PurchasePrice"]);
                    receivedAt = Convert.ToString(reader["ReceivedAt"]);
                }
            }

            double pendingSales = 0;
            if (activeAuditSessionId.HasValue)
            {
                using (var command = new SQLiteCommand(@"
SELECT COALESCE(SUM(Quantity),0)
FROM InventoryCountPendingMovements
WHERE SessionId=@SessionId
  AND BatchId=@BatchId
  AND MovementType='Sale'
  AND ProcessingStatus IN ('Pending','Failed');",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@SessionId", activeAuditSessionId.Value);
                    command.Parameters.AddWithValue("@BatchId", batchId);
                    pendingSales = Convert.ToDouble(command.ExecuteScalar());
                }
            }

            double existingLayerQuantity;
            using (var command = new SQLiteCommand(@"
SELECT COALESCE(SUM(RemainingQuantity),0)
FROM InventoryCostLayers
WHERE ProductId=@ProductId AND BatchId=@BatchId
  AND RemainingQuantity>0;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@ProductId", productId);
                command.Parameters.AddWithValue("@BatchId", batchId);
                existingLayerQuantity = Convert.ToDouble(command.ExecuteScalar());
            }

            double uncoveredQuantity = Math.Round(
                batchQuantity - pendingSales - existingLayerQuantity, 8);
            if (uncoveredQuantity <= 0.00000001)
                return;

            if (string.IsNullOrWhiteSpace(receivedAt))
                receivedAt = "0001-01-01 00:00:00";

            int legacyLayerId;
            using (var command = new SQLiteCommand(@"
INSERT INTO InventoryCostLayers
(
    ProductId,BatchId,SourceType,SourceRecordId,SourceItemId,
    ReceivedAt,UnitCost,OriginalQuantity,RemainingQuantity,CreatedAt
)
VALUES
(
    @ProductId,@BatchId,'Legacy',NULL,NULL,
    @ReceivedAt,@UnitCost,@Quantity,@Quantity,@CreatedAt
);
SELECT last_insert_rowid();",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@ProductId", productId);
                command.Parameters.AddWithValue("@BatchId", batchId);
                command.Parameters.AddWithValue("@ReceivedAt", receivedAt);
                command.Parameters.AddWithValue("@UnitCost", previousUnitCost);
                command.Parameters.AddWithValue("@Quantity", uncoveredQuantity);
                command.Parameters.AddWithValue("@CreatedAt", receivedAt);
                legacyLayerId = Convert.ToInt32(command.ExecuteScalar());
            }

            using (var command = new SQLiteCommand(@"
INSERT INTO InventoryCostLayerMovements
(
    CostLayerId,ProductId,MovementType,QuantityChange,UnitCost,
    ReferenceTable,ReferenceId,ReferenceItemId,Notes,CreatedAt
)
VALUES
(
    @LayerId,@ProductId,'Opening',@Quantity,@UnitCost,
    'Batches',@BatchId,NULL,
    'تثبيت تكلفة الرصيد السابق قبل تسجيل تكلفة شراء جديدة',@CreatedAt
);",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@LayerId", legacyLayerId);
                command.Parameters.AddWithValue("@ProductId", productId);
                command.Parameters.AddWithValue("@Quantity", uncoveredQuantity);
                command.Parameters.AddWithValue("@UnitCost", previousUnitCost);
                command.Parameters.AddWithValue("@BatchId", batchId);
                command.Parameters.AddWithValue("@CreatedAt", receivedAt);
                command.ExecuteNonQuery();
            }
        }

        public static void RecordPurchaseReceipt(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int productId,
            int batchId,
            int invoiceId,
            int purchaseItemId,
            string receivedAt,
            double unitCost,
            double quantity,
            bool deferUntilAuditMovementProcessed)
        {
            if (productId <= 0 || batchId <= 0 ||
                invoiceId <= 0 || purchaseItemId <= 0)
                throw new ArgumentException("بيانات طبقة تكلفة الشراء غير صحيحة.");

            if (double.IsNaN(unitCost) || double.IsInfinity(unitCost) ||
                unitCost < 0)
                throw new ArgumentOutOfRangeException("unitCost");

            if (double.IsNaN(quantity) || double.IsInfinity(quantity) ||
                quantity <= 0)
                throw new ArgumentOutOfRangeException("quantity");

            int costLayerId;

            using (var command = new SQLiteCommand(@"
INSERT INTO InventoryCostLayers
(
    ProductId,
    BatchId,
    SourceType,
    SourceRecordId,
    SourceItemId,
    ReceivedAt,
    UnitCost,
    OriginalQuantity,
    RemainingQuantity,
    CreatedAt
)
VALUES
(
    @ProductId,
    @BatchId,
    'Purchase',
    @InvoiceId,
    @PurchaseItemId,
    @ReceivedAt,
    @UnitCost,
    @Quantity,
    CASE WHEN @Defer = 1 THEN 0 ELSE @Quantity END,
    @CreatedAt
);
SELECT last_insert_rowid();",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@ProductId", productId);
                command.Parameters.AddWithValue("@BatchId", batchId);
                command.Parameters.AddWithValue("@InvoiceId", invoiceId);
                command.Parameters.AddWithValue("@PurchaseItemId", purchaseItemId);
                command.Parameters.AddWithValue("@ReceivedAt", receivedAt);
                command.Parameters.AddWithValue("@UnitCost", unitCost);
                command.Parameters.AddWithValue("@Quantity", quantity);
                command.Parameters.AddWithValue("@Defer", deferUntilAuditMovementProcessed ? 1 : 0);
                command.Parameters.AddWithValue("@CreatedAt", receivedAt);

                costLayerId = Convert.ToInt32(command.ExecuteScalar());
            }

            if (!deferUntilAuditMovementProcessed)
            {
                using (var command = new SQLiteCommand(@"
    INSERT INTO InventoryCostLayerMovements
    (
        CostLayerId,
        ProductId,
        MovementType,
        QuantityChange,
        UnitCost,
        ReferenceTable,
        ReferenceId,
        ReferenceItemId,
        Notes,
        CreatedAt
    )
    VALUES
    (
        @CostLayerId,
        @ProductId,
        'Receipt',
        @Quantity,
        @UnitCost,
        'PurchaseInvoiceItems',
        @InvoiceId,
        @PurchaseItemId,
        'استلام فاتورة شراء',
        @CreatedAt
    );",
                    connection,
                    transaction))
                {
                    command.Parameters.AddWithValue("@CostLayerId", costLayerId);
                    command.Parameters.AddWithValue("@ProductId", productId);
                    command.Parameters.AddWithValue("@Quantity", quantity);
                    command.Parameters.AddWithValue("@UnitCost", unitCost);
                    command.Parameters.AddWithValue("@InvoiceId", invoiceId);
                    command.Parameters.AddWithValue("@PurchaseItemId", purchaseItemId);
                    command.Parameters.AddWithValue("@CreatedAt", receivedAt);
                    command.ExecuteNonQuery();
                }

            }
        }
        public static void ActivateDeferredPurchaseReceipt(
            SQLiteConnection c, SQLiteTransaction t,
            int purchaseItemId, double quantity, string createdAt)
        {
            int layerId, productId;
            double unitCost;
            using (var cmd = new SQLiteCommand(@"
SELECT Id,ProductId,UnitCost FROM InventoryCostLayers
WHERE SourceType='Purchase' AND SourceItemId=@I LIMIT 1;", c, t))
            {
                cmd.Parameters.AddWithValue("@I", purchaseItemId);
                using (var rd = cmd.ExecuteReader())
                {
                    if (!rd.Read()) return;
                    layerId = Convert.ToInt32(rd["Id"]);
                    productId = Convert.ToInt32(rd["ProductId"]);
                    unitCost = Convert.ToDouble(rd["UnitCost"]);
                }
            }
            using (var cmd = new SQLiteCommand(@"
UPDATE InventoryCostLayers
SET RemainingQuantity=RemainingQuantity+@Q
WHERE Id=@I AND OriginalQuantity+0.00000001>=RemainingQuantity+@Q;", c, t))
            {
                cmd.Parameters.AddWithValue("@Q", quantity);
                cmd.Parameters.AddWithValue("@I", layerId);
                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("تعذر تفعيل طبقة تكلفة الشراء بعد انتهاء الجرد.");
            }
            InsertCostMovement(c, t, layerId, productId, "Receipt", quantity, unitCost,
                "PurchaseInvoiceItems", purchaseItemId, purchaseItemId,
                "ترحيل استلام شراء بعد الجرد", createdAt);
        }

        public static void AllocateSaleCostLayers(
            SQLiteConnection c, SQLiteTransaction t,
            int productId, int batchId, int allocationId, long invoiceItemId,
            double quantity, double availableBeforeSale, string createdAt)
        {
            double layered;
            using (var cmd = new SQLiteCommand(@"
SELECT COALESCE(SUM(RemainingQuantity),0)
FROM InventoryCostLayers
WHERE ProductId=@P AND BatchId=@B AND RemainingQuantity>0;", c, t))
            {
                cmd.Parameters.AddWithValue("@P", productId);
                cmd.Parameters.AddWithValue("@B", batchId);
                layered = Convert.ToDouble(cmd.ExecuteScalar());
            }

            double gap = Math.Round(availableBeforeSale - layered, 8);
            if (gap > 0.00000001)
            {
                double legacyCost;
                using (var cmd = new SQLiteCommand(
                    "SELECT PurchasePrice FROM Batches WHERE Id=@B AND ProductId=@P;", c, t))
                {
                    cmd.Parameters.AddWithValue("@B", batchId);
                    cmd.Parameters.AddWithValue("@P", productId);
                    object v = cmd.ExecuteScalar();
                    if (v == null || v == DBNull.Value)
                        throw new InvalidOperationException("تعذر تحديد تكلفة الرصيد القديم للصنف.");
                    legacyCost = Convert.ToDouble(v);
                }
                int legacyId;
                using (var cmd = new SQLiteCommand(@"
INSERT INTO InventoryCostLayers
(ProductId,BatchId,SourceType,SourceRecordId,SourceItemId,ReceivedAt,
 UnitCost,OriginalQuantity,RemainingQuantity,CreatedAt)
VALUES(@P,@B,'Legacy',NULL,NULL,@D,@C,@Q,@Q,@D);
SELECT last_insert_rowid();", c, t))
                {
                    cmd.Parameters.AddWithValue("@P", productId);
                    cmd.Parameters.AddWithValue("@B", batchId);
                    cmd.Parameters.AddWithValue("@D", createdAt);
                    cmd.Parameters.AddWithValue("@C", legacyCost);
                    cmd.Parameters.AddWithValue("@Q", gap);
                    legacyId = Convert.ToInt32(cmd.ExecuteScalar());
                }
                InsertCostMovement(c, t, legacyId, productId, "Opening", gap, legacyCost,
                    "Batches", batchId, null, "رصيد سابق قبل تفعيل طبقات التكلفة", createdAt);
            }

            var layers = new System.Collections.Generic.List<CostLayerCandidate>();
            using (var cmd = new SQLiteCommand(@"
SELECT Id,UnitCost,RemainingQuantity FROM InventoryCostLayers
WHERE ProductId=@P AND BatchId=@B AND RemainingQuantity>0
ORDER BY ReceivedAt,Id;", c, t))
            {
                cmd.Parameters.AddWithValue("@P", productId);
                cmd.Parameters.AddWithValue("@B", batchId);
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) layers.Add(new CostLayerCandidate
                    {
                        Id = Convert.ToInt32(rd["Id"]),
                        UnitCost = Convert.ToDouble(rd["UnitCost"]),
                        RemainingQuantity = Convert.ToDouble(rd["RemainingQuantity"])
                    });
            }

            double remaining = quantity;
            foreach (var layer in layers)
            {
                if (remaining <= 0.00000001) break;
                double take = Math.Min(remaining, layer.RemainingQuantity);
                if (take <= 0.00000001) continue;
                using (var cmd = new SQLiteCommand(@"
UPDATE InventoryCostLayers
SET RemainingQuantity=CASE WHEN RemainingQuantity-@Q<=0.00000001 THEN 0
 ELSE RemainingQuantity-@Q END
WHERE Id=@I AND RemainingQuantity+0.00000001>=@Q;", c, t))
                {
                    cmd.Parameters.AddWithValue("@Q", take);
                    cmd.Parameters.AddWithValue("@I", layer.Id);
                    if (cmd.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("تغير رصيد طبقة التكلفة أثناء تسجيل البيع.");
                }
                using (var cmd = new SQLiteCommand(@"
INSERT INTO SalesInvoiceCostAllocations
(InvoiceBatchAllocationId,CostLayerId,Quantity,UnitCost,CreatedAt)
VALUES(@A,@L,@Q,@C,@D);", c, t))
                {
                    cmd.Parameters.AddWithValue("@A", allocationId);
                    cmd.Parameters.AddWithValue("@L", layer.Id);
                    cmd.Parameters.AddWithValue("@Q", take);
                    cmd.Parameters.AddWithValue("@C", layer.UnitCost);
                    cmd.Parameters.AddWithValue("@D", createdAt);
                    cmd.ExecuteNonQuery();
                }
                InsertCostMovement(c, t, layer.Id, productId, "Sale", -take, layer.UnitCost,
                    "SalesInvoiceBatchAllocations", allocationId, invoiceItemId,
                    "صرف تكلفة بيع FIFO", createdAt);
                remaining = Math.Round(remaining - take, 8);
            }
            if (remaining > 0.00000001)
                throw new InvalidOperationException(
                    "طبقات تكلفة الصنف لا تغطي الكمية المباعة؛ تم إلغاء الفاتورة لحماية دقة التكلفة.");
        }

        private static void InsertCostMovement(
            SQLiteConnection c, SQLiteTransaction t, int layerId, int productId,
            string type, double quantity, double cost, string refTable,
            int? refId, long? refItemId, string notes, string createdAt)
        {
            using (var cmd = new SQLiteCommand(@"
INSERT INTO InventoryCostLayerMovements
(CostLayerId,ProductId,MovementType,QuantityChange,UnitCost,
 ReferenceTable,ReferenceId,ReferenceItemId,Notes,CreatedAt)
VALUES(@L,@P,@T,@Q,@C,@RT,@RI,@RII,@N,@D);", c, t))
            {
                cmd.Parameters.AddWithValue("@L", layerId);
                cmd.Parameters.AddWithValue("@P", productId);
                cmd.Parameters.AddWithValue("@T", type);
                cmd.Parameters.AddWithValue("@Q", quantity);
                cmd.Parameters.AddWithValue("@C", cost);
                cmd.Parameters.AddWithValue("@RT", refTable ?? "");
                cmd.Parameters.AddWithValue("@RI", refId.HasValue ? (object)refId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@RII", refItemId.HasValue ? (object)refItemId.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@N", notes ?? "");
                cmd.Parameters.AddWithValue("@D", createdAt);
                cmd.ExecuteNonQuery();
            }
        }

        private sealed class CostLayerCandidate
        {
            public int Id { get; set; }
            public double UnitCost { get; set; }
            public double RemainingQuantity { get; set; }
        }

    }
}
