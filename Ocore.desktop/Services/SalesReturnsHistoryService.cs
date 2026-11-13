
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
                                Id = Convert.ToInt32(reader["Id"]),
                                ReturnNumber = Convert.ToString(
                                    reader["ReturnNumber"]),
                                InvoiceNumber = Convert.ToString(
                                    reader["InvoiceNumber"]),
                                EmployeeName = Convert.ToString(
                                    reader["EmployeeName"]),
                                Total = Convert.ToDouble(reader["Total"]),
                                Reason = Convert.ToString(reader["Reason"]),
                                Status = Convert.ToString(reader["Status"]),
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

                        details.Id = Convert.ToInt32(reader["Id"]);
                        details.ReturnNumber = Convert.ToString(
                            reader["ReturnNumber"]);
                        details.InvoiceId = Convert.ToInt32(
                            reader["InvoiceId"]);
                        details.InvoiceNumber = Convert.ToString(
                            reader["InvoiceNumber"]);
                        details.UserId = Convert.ToInt32(
                            reader["UserId"]);
                        details.EmployeeName = Convert.ToString(
                            reader["EmployeeName"]);
                        details.Total = Convert.ToDouble(reader["Total"]);
                        details.Reason = Convert.ToString(reader["Reason"]);
                        details.Status = Convert.ToString(reader["Status"]);
                        details.CreatedAt = createdAt;
                    }
                }

                // قراءة الوحدة والكمية والسعر الفعلي للمرتجع
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
                        sri.ReturnUnitPrice
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
                                Convert.ToString(reader["UnitName"]);

                            double originalQuantity =
                                Convert.ToDouble(reader["Quantity"]);

                            double originalUnitPrice =
                                Convert.ToDouble(reader["UnitPrice"]);

                            string returnUnitName =
                                reader["ReturnUnitName"] == DBNull.Value
                                    ? ""
                                    : Convert.ToString(
                                        reader["ReturnUnitName"]);

                            double returnUnitQuantity =
                                reader["ReturnUnitQuantity"] == DBNull.Value
                                    ? 0
                                    : Convert.ToDouble(
                                        reader["ReturnUnitQuantity"]);

                            double returnUnitPrice =
                                reader["ReturnUnitPrice"] == DBNull.Value
                                    ? 0
                                    : Convert.ToDouble(
                                        reader["ReturnUnitPrice"]);

                            // توافق مع سجلات المرتجعات القديمة
                            if (string.IsNullOrWhiteSpace(returnUnitName))
                                returnUnitName = originalUnitName;

                            if (returnUnitQuantity <= 0)
                                returnUnitQuantity = originalQuantity;

                            if (returnUnitPrice <= 0)
                                returnUnitPrice = originalUnitPrice;

                            details.Items.Add(new SalesReturnItem
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                ReturnId = Convert.ToInt32(reader["ReturnId"]),
                                InvoiceItemId = Convert.ToInt32(
                                    reader["InvoiceItemId"]),
                                ProductId = Convert.ToInt32(
                                    reader["ProductId"]),
                                ItemName = Convert.ToString(reader["ItemName"]),

                                // تعرض الشاشة بيانات وحدة المرتجع الفعلية
                                UnitName = returnUnitName,
                                Quantity = returnUnitQuantity,
                                UnitPrice = returnUnitPrice,

                                Total = Convert.ToDouble(reader["Total"]),
                                PackageQuantity = Convert.ToDouble(
                                    reader["PackageQuantity"]),

                                ReturnUnitName = returnUnitName,
                                ReturnUnitQuantity = returnUnitQuantity,
                                ReturnUnitPrice = returnUnitPrice
                            });
                        }
                    }
                }
            }

            return details;
        }
    }
}
