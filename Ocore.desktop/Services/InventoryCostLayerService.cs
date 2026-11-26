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
            bool deferQuantityForInventoryAudit)
        {
            int batchId;

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
                    deferQuantityForInventoryAudit ? 1 : 0);
                update.Parameters.AddWithValue("@ReceivedQuantity", receivedQuantity);
                update.Parameters.AddWithValue("@BatchId", batchId);
                update.Parameters.AddWithValue("@ProductId", productId);

                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "تعذر تحديث رصيد التشغيلة الموحدة للصنف.");
            }

            return batchId;
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
            double quantity)
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
    @Quantity,
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
                command.Parameters.AddWithValue("@CreatedAt", receivedAt);

                costLayerId = Convert.ToInt32(command.ExecuteScalar());
            }

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
}
