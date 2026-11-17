
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class SalesReturnsHistoryService
    {
        private readonly string connectionString;

        public SalesReturnsHistoryService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        // ==========================================
        // أدوات قراءة آمنة
        // ==========================================

        private double ReadDouble(SQLiteDataReader reader, string column)
        {
            int index = reader.GetOrdinal(column);

            if (reader.IsDBNull(index))
                return 0;

            return Convert.ToDouble(reader.GetValue(index));
        }

        private int ReadInt(SQLiteDataReader reader, string column)
        {
            int index = reader.GetOrdinal(column);

            if (reader.IsDBNull(index))
                return 0;

            return Convert.ToInt32(reader.GetValue(index));
        }

        private string ReadString(SQLiteDataReader reader, string column)
        {
            int index = reader.GetOrdinal(column);

            return reader.IsDBNull(index)
                ? ""
                : Convert.ToString(reader.GetValue(index));
        }

        private bool ReadBool(SQLiteDataReader reader, string column)
        {
            return ReadInt(reader, column) != 0;
        }

        // ==========================================
        // جلب سجل مرتجعات البيع
        // ==========================================

        public List<SalesReturnHistoryItem> GetReturns(
            int userId,
            string searchText,
            DateTime? fromDate,
            DateTime? toDate)
        {
            if (userId <= 0)
                throw new Exception("رقم المستخدم غير صحيح.");

            var items = new List<SalesReturnHistoryItem>();
            string search = (searchText ?? "").Trim();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string sql = @"
                    SELECT
                        sr.Id,
                        sr.ReturnNumber,
                        si.InvoiceNumber,
                        u.FullName AS EmployeeName,
                        sr.Total,
                        sr.Reason,
                        sr.Status,
                        sr.CreatedAt
                    FROM SalesReturns sr
                    INNER JOIN SalesInvoices si
                        ON si.Id = sr.InvoiceId
                    INNER JOIN Users u
                        ON u.Id = sr.UserId
                    WHERE
                        (
                            @Search = ''
                            OR sr.ReturnNumber LIKE @Pattern
                            OR si.InvoiceNumber LIKE @Pattern
                            OR u.FullName LIKE @Pattern
                            OR sr.Reason LIKE @Pattern
                        )
                        AND (
                            @FromDate IS NULL
                            OR date(sr.CreatedAt) >= date(@FromDate)
                        )
                        AND (
                            @ToDate IS NULL
                            OR date(sr.CreatedAt) <= date(@ToDate)
                        )
                    ORDER BY sr.Id DESC;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Search", search);
                    command.Parameters.AddWithValue(
                        "@Pattern", "%" + search + "%");

                    command.Parameters.AddWithValue(
                        "@FromDate",
                        fromDate.HasValue
                            ? fromDate.Value.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture)
                            : (object)DBNull.Value);

                    command.Parameters.AddWithValue(
                        "@ToDate",
                        toDate.HasValue
                            ? toDate.Value.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture)
                            : (object)DBNull.Value);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime createdAt;

                            DateTime.TryParse(
                                Convert.ToString(reader["CreatedAt"]),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out createdAt);

                            items.Add(new SalesReturnHistoryItem
                            {
                                Id = ReadInt(reader, "Id"),
                                ReturnNumber = ReadString(reader, "ReturnNumber"),
                                InvoiceNumber = ReadString(reader, "InvoiceNumber"),
                                EmployeeName = ReadString(reader, "EmployeeName"),
                                Total = ReadDouble(reader, "Total"),
                                Reason = ReadString(reader, "Reason"),
                                Status = ReadString(reader, "Status"),
                                CreatedAt = createdAt.ToString(
                                    "yyyy-MM-dd HH:mm",
                                    CultureInfo.InvariantCulture)
                            });
                        }
                    }
                }
            }

            return items;
        }

        // ==========================================
        // تفاصيل مرتجع محدد
        // ==========================================

        public SalesReturnDetails GetReturnDetails(
            int userId,
            int returnId)
        {
            if (userId <= 0)
                throw new Exception("رقم المستخدم غير صحيح.");

            if (returnId <= 0)
                throw new Exception("رقم المرتجع غير صحيح.");

            var details = new SalesReturnDetails();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string sql = @"
                    SELECT
                        sr.Id,
                        sr.ReturnNumber,
                        sr.InvoiceId,
                        si.InvoiceNumber,
                        sr.UserId,
                        u.FullName AS EmployeeName,
                        sr.Total,
                        COALESCE(sr.DiscountAmount, 0) AS DiscountAmount,
                        COALESCE(sr.TaxableAmount, 0) AS TaxableAmount,
                        COALESCE(sr.VatAmount, 0) AS VatAmount,
                        COALESCE(sr.ScheduleTaxAmount, 0) AS ScheduleTaxAmount,
                        COALESCE(sr.TaxTotal, 0) AS TaxTotal,
COALESCE(sr.TaxStatus, 'Legacy') AS TaxStatus,
COALESCE(sr.RefundAmount, 0) AS RefundAmount,
COALESCE(sr.RefundPaymentMethod, '') AS RefundPaymentMethod,
                        sr.Reason,
                        sr.Status,
                        sr.CreatedAt
                    FROM SalesReturns sr
                    INNER JOIN SalesInvoices si
                        ON si.Id = sr.InvoiceId
                    INNER JOIN Users u
                        ON u.Id = sr.UserId
                    WHERE sr.Id = @ReturnId
                    LIMIT 1;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ReturnId", returnId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new Exception("المرتجع غير موجود.");

                        DateTime createdAt;

                        DateTime.TryParse(
                            Convert.ToString(reader["CreatedAt"]),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out createdAt);

                        details.Id = ReadInt(reader, "Id");
                        details.ReturnNumber = ReadString(reader, "ReturnNumber");
                        details.InvoiceId = ReadInt(reader, "InvoiceId");
                        details.InvoiceNumber = ReadString(reader, "InvoiceNumber");
                        details.UserId = ReadInt(reader, "UserId");
                        details.EmployeeName = ReadString(reader, "EmployeeName");

                        details.Total = ReadDouble(reader, "Total");
                        details.DiscountAmount = ReadDouble(reader, "DiscountAmount");
                        details.TaxableAmount = ReadDouble(reader, "TaxableAmount");
                        details.VatAmount = ReadDouble(reader, "VatAmount");
                        details.ScheduleTaxAmount = ReadDouble(reader, "ScheduleTaxAmount");
                        details.TaxTotal = ReadDouble(reader, "TaxTotal");
                        details.TaxStatus = ReadString(reader, "TaxStatus");
                        details.RefundAmount = ReadDouble(reader, "RefundAmount");
                        details.RefundPaymentMethod = ReadString(reader, "RefundPaymentMethod");
                        details.Reason = ReadString(reader, "Reason");
                        details.Status = ReadString(reader, "Status");
                        details.CreatedAt = createdAt;
                    }
                }

                // ==========================================
                // قراءة بنود المرتجع
                // ==========================================

                string itemsSql = @"
                    SELECT
                        sri.Id,
                        sri.ReturnId,
                        sri.InvoiceItemId,
                        sri.ProductId,
                        sri.ItemName,
                        sri.UnitName,
                        sri.Quantity,
                        sri.UnitPrice,
                        sri.Total,
                        sri.PackageQuantity,
                        sri.ReturnUnitName,
                        sri.ReturnUnitQuantity,
                        sri.ReturnUnitPrice,
                        COALESCE(sri.DiscountAmount, 0) AS DiscountAmount,
                        COALESCE(sri.TaxableAmount, 0) AS TaxableAmount,
                        COALESCE(sri.VatAmount, 0) AS VatAmount,
                        COALESCE(sri.ScheduleTaxAmount, 0) AS ScheduleTaxAmount,
                        COALESCE(sri.TaxStatus, 'Legacy') AS TaxStatus
                    FROM SalesReturnItems sri
                    WHERE sri.ReturnId = @ReturnId
                    ORDER BY sri.Id;";

                using (var command = new SQLiteCommand(itemsSql, connection))
                {
                    command.Parameters.AddWithValue("@ReturnId", returnId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string originalUnitName =
                                ReadString(reader, "UnitName");

                            double originalQuantity =
                                ReadDouble(reader, "Quantity");

                            double originalUnitPrice =
                                ReadDouble(reader, "UnitPrice");

                            string returnUnitName =
                                ReadString(reader, "ReturnUnitName");

                            double returnUnitQuantity =
                                ReadDouble(reader, "ReturnUnitQuantity");

                            double returnUnitPrice =
                                ReadDouble(reader, "ReturnUnitPrice");

                            // توافق مع سجلات المرتجعات القديمة
                            if (string.IsNullOrWhiteSpace(returnUnitName))
                                returnUnitName = originalUnitName;

                            if (returnUnitQuantity <= 0)
                                returnUnitQuantity = originalQuantity;

                            if (returnUnitPrice <= 0)
                                returnUnitPrice = originalUnitPrice;

                            var item = new SalesReturnItem
                            {
                                Id = ReadInt(reader, "Id"),
                                ReturnId = ReadInt(reader, "ReturnId"),
                                InvoiceItemId = ReadInt(reader, "InvoiceItemId"),
                                ProductId = ReadInt(reader, "ProductId"),
                                ItemName = ReadString(reader, "ItemName"),

                                UnitName = returnUnitName,
                                Quantity = returnUnitQuantity,
                                UnitPrice = returnUnitPrice,

                                Total = ReadDouble(reader, "Total"),
                                PackageQuantity = ReadDouble(reader, "PackageQuantity"),

                                ReturnUnitName = returnUnitName,
                                ReturnUnitQuantity = returnUnitQuantity,
                                ReturnUnitPrice = returnUnitPrice,

                                DiscountAmount = ReadDouble(reader, "DiscountAmount"),
                                TaxableAmount = ReadDouble(reader, "TaxableAmount"),
                                VatAmount = ReadDouble(reader, "VatAmount"),
                                ScheduleTaxAmount = ReadDouble(reader, "ScheduleTaxAmount"),
                                TaxStatus = ReadString(reader, "TaxStatus")
                            };

                            item.TaxTotal =
                                item.VatAmount + item.ScheduleTaxAmount;

                            details.Items.Add(item);
                        }
                    }
                }

                // ==========================================
                // قراءة مكونات الضريبة لكل بند
                // ==========================================

                string componentsSql = @"
                    SELECT
                        c.ReturnItemId,
                        c.TaxProfileRateId,
                        c.ComponentCode,
                        c.ComponentName,
                        c.ComponentType,
                        c.RatePercent,
                        c.CalculationOrder,
                        c.IsCompound,
                        c.TaxableBase,
                        c.TaxAmount
                    FROM SalesReturnItemTaxComponents c
                    INNER JOIN SalesReturnItems sri
                        ON sri.Id = c.ReturnItemId
                    WHERE sri.ReturnId = @ReturnId
                    ORDER BY
                        c.ReturnItemId,
                        c.CalculationOrder,
                        c.Id;";

                using (var command = new SQLiteCommand(
                    componentsSql, connection))
                {
                    command.Parameters.AddWithValue("@ReturnId", returnId);

                    using (var reader = command.ExecuteReader())
                    {
                        var itemsById =
                            new Dictionary<int, SalesReturnItem>();

                        foreach (SalesReturnItem item in details.Items)
                        {
                            itemsById[item.Id] = item;
                        }

                        while (reader.Read())
                        {
                            int returnItemId =
                                ReadInt(reader, "ReturnItemId");

                            SalesReturnItem item;

                            if (!itemsById.TryGetValue(
                                returnItemId, out item))
                            {
                                continue;
                            }

                            item.TaxComponents.Add(
                                new SalesReturnTaxComponent
                                {
                                    OriginalRateId = ReadInt(
                                        reader, "TaxProfileRateId"),

                                    ComponentCode = ReadString(
                                        reader, "ComponentCode"),

                                    ComponentName = ReadString(
                                        reader, "ComponentName"),

                                    ComponentType = ReadString(
                                        reader, "ComponentType"),

                                    RatePercent = ReadDouble(
                                        reader, "RatePercent"),

                                    CalculationOrder = ReadInt(
                                        reader, "CalculationOrder"),

                                    IsCompound = ReadBool(
                                        reader, "IsCompound"),

                                    TaxableBase = ReadDouble(
                                        reader, "TaxableBase"),

                                    TaxAmount = ReadDouble(
                                        reader, "TaxAmount")
                                });
                        }
                    }
                }
            }

            return details;
        }
    }
}
