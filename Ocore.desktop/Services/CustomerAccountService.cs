
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public class CustomerAccountService
    {
        private readonly string connectionString;

        private const double Epsilon = 0.00000001;
        private const double AmountTolerance = 0.000001;

        public CustomerAccountService()
        {
            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() +
                ";Version=3;";
        }

        // حساب الرصيد الحالي للعميل من دفتر الحركات.
        // الرصيد الموجب يعني أن العميل مدين.
        // الرصيد السالب يعني وجود رصيد لصالح العميل.
        public double GetCustomerBalance(
            int currentUserId,
            int customerId)
        {
            if (customerId <= 0)
                throw new ArgumentException(
                    "رقم العميل غير صحيح.",
                    "customerId");

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(
                    connection, currentUserId);

                ValidatePermission(
                    connection,
                    role,
                    "Customers.View",
                    "ليس لديك صلاحية عرض حسابات العملاء.");

                EnsureCustomerExists(connection, customerId);

                return GetBalance(
                    connection, null, customerId);
            }
        }

        // استرجاع كشف حساب العميل مرتبًا من الأحدث إلى الأقدم.
        public List<CustomerAccountTransaction>
            GetCustomerTransactions(
                int currentUserId,
                int customerId,
                DateTime? fromDate = null,
                DateTime? toDate = null)
        {
            if (customerId <= 0)
                throw new ArgumentException(
                    "رقم العميل غير صحيح.",
                    "customerId");

            if (fromDate.HasValue &&
                toDate.HasValue &&
                fromDate.Value.Date > toDate.Value.Date)
            {
                throw new ArgumentException(
                    "تاريخ البداية لا يمكن أن يكون بعد تاريخ النهاية.");
            }

            var transactions =
                new List<CustomerAccountTransaction>();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetActiveUserRole(
                    connection, currentUserId);

                ValidatePermission(
                    connection,
                    role,
                    "Customers.View",
                    "ليس لديك صلاحية عرض حسابات العملاء.");

                EnsureCustomerExists(connection, customerId);

                string sql = @"
                    SELECT
                        Id,
                        CustomerId,
                        TransactionType,
                        Amount,
                        ReferenceType,
                        ReferenceId,
                        PaymentMethod,
                        ReferenceNumber,
                        Notes,
                        UserId,
                        CreatedAt
                    FROM CustomerAccountTransactions
                    WHERE CustomerId = @customerId ";

                if (fromDate.HasValue)
                    sql += " AND date(CreatedAt) >= date(@fromDate) ";

                if (toDate.HasValue)
                    sql += " AND date(CreatedAt) <= date(@toDate) ";

                sql += @"
                    ORDER BY
                        CreatedAt DESC,
                        Id DESC;";

                using (var command =
                    new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@customerId", customerId);

                    if (fromDate.HasValue)
                    {
                        command.Parameters.AddWithValue(
                            "@fromDate",
                            fromDate.Value.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture));
                    }

                    if (toDate.HasValue)
                    {
                        command.Parameters.AddWithValue(
                            "@toDate",
                            toDate.Value.ToString(
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture));
                    }

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var item =
                                new CustomerAccountTransaction();

                            item.Id = Convert.ToInt64(reader["Id"]);
                            item.CustomerId =
                                Convert.ToInt32(reader["CustomerId"]);

                            item.TransactionType =
                                reader["TransactionType"].ToString();

                            item.Amount =
                                Convert.ToDouble(reader["Amount"]);

                            item.ReferenceType =
                                reader["ReferenceType"].ToString();

                            item.ReferenceId =
                                reader["ReferenceId"] == DBNull.Value
                                    ? (long?)null
                                    : Convert.ToInt64(
                                        reader["ReferenceId"]);

                            item.PaymentMethod =
                                reader["PaymentMethod"].ToString();

                            item.ReferenceNumber =
                                reader["ReferenceNumber"].ToString();

                            item.Notes =
                                reader["Notes"].ToString();

                            item.UserId =
                                Convert.ToInt32(reader["UserId"]);

                            DateTime parsedDate;

                            string createdAt =
                                reader["CreatedAt"].ToString();

                            if (DateTime.TryParseExact(
                                createdAt,
                                "yyyy-MM-dd HH:mm:ss",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None,
                                out parsedDate))
                            {
                                item.CreatedAt = parsedDate;
                            }
                            else if (DateTime.TryParse(
                                createdAt,
                                CultureInfo.CurrentCulture,
                                DateTimeStyles.None,
                                out parsedDate))
                            {
                                item.CreatedAt = parsedDate;
                            }

                            transactions.Add(item);
                        }
                    }
                }
            }

            return transactions;
        }

        // تسجيل تحصيل من مديونية العميل.
        // المبلغ يُسجل بالسالب في دفتر الحساب.
        // لا يتم تسجيله كدفعة فاتورة بيع جديدة.
        public long CollectPayment(
            int currentUserId,
            int customerId,
            double amount,
            string paymentMethod,
            string referenceNumber = "",
            string notes = "")
        {
            if (customerId <= 0)
                throw new ArgumentException(
                    "رقم العميل غير صحيح.",
                    "customerId");

            if (double.IsNaN(amount) ||
                double.IsInfinity(amount) ||
                amount <= 0)
            {
                throw new InvalidOperationException(
                    "مبلغ التحصيل يجب أن يكون أكبر من صفر.");
            }

            amount = Math.Round(
                amount,
                2,
                MidpointRounding.AwayFromZero);

            if (amount <= 0)
            {
                throw new InvalidOperationException(
                    "مبلغ التحصيل غير صحيح.");
            }

            paymentMethod = NormalizePaymentMethod(
                paymentMethod);

            referenceNumber =
                (referenceNumber ?? string.Empty).Trim();

            notes = (notes ?? string.Empty).Trim();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var pragma = new SQLiteCommand(
                    "PRAGMA foreign_keys = ON;",
                    connection))
                {
                    pragma.ExecuteNonQuery();
                }

                using (var transaction = connection.BeginTransaction(
                    IsolationLevel.Serializable))
                {
                    try
                    {
                        string username;
                        string role;

                        GetActiveUser(
                            connection,
                            transaction,
                            currentUserId,
                            out username,
                            out role);

                        ValidatePermission(
                            connection,
                            transaction,
                            role,
                            "Customers.CollectPayment",
                            "ليس لديك صلاحية تحصيل مديونية العملاء.");

                        EnsureCustomerExists(
                            connection,
                            transaction,
                            customerId);

                        double currentBalance = GetBalance(
                            connection,
                            transaction,
                            customerId);

                        if (currentBalance <= Epsilon)
                        {
                            throw new InvalidOperationException(
                                "لا توجد مديونية مستحقة على هذا العميل.");
                        }

                        if (amount - currentBalance >
                            AmountTolerance)
                        {
                            throw new InvalidOperationException(
                                "مبلغ التحصيل أكبر من المديونية المستحقة.\n" +
                                "المديونية الحالية: " +
                                currentBalance.ToString(
                                    "0.00",
                                    CultureInfo.InvariantCulture));
                        }

                        string createdAt = DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);

                        long transactionId;

                        using (var command = new SQLiteCommand(
                            @"
                            INSERT INTO CustomerAccountTransactions
                            (
                                CustomerId,
                                TransactionType,
                                Amount,
                                ReferenceType,
                                ReferenceId,
                                PaymentMethod,
                                ReferenceNumber,
                                Notes,
                                UserId,
                                CreatedAt
                            )
                            VALUES
                            (
                                @customerId,
                                'Collection',
                                @amount,
                                'CustomerCollection',
                                NULL,
                                @paymentMethod,
                                @referenceNumber,
                                @notes,
                                @userId,
                                @createdAt
                            );",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@customerId", customerId);

                            command.Parameters.AddWithValue(
                                "@amount", -amount);

                            command.Parameters.AddWithValue(
                                "@paymentMethod", paymentMethod);

                            command.Parameters.AddWithValue(
                                "@referenceNumber", referenceNumber);

                            command.Parameters.AddWithValue(
                                "@notes", notes);

                            command.Parameters.AddWithValue(
                                "@userId", currentUserId);

                            command.Parameters.AddWithValue(
                                "@createdAt", createdAt);

                            command.ExecuteNonQuery();
                        }

                        using (var command = new SQLiteCommand(
                            "SELECT last_insert_rowid();",
                            connection,
                            transaction))
                        {
                            transactionId = Convert.ToInt64(
                                command.ExecuteScalar());
                        }

                        WriteAuditLog(
                            connection,
                            transaction,
                            currentUserId,
                            username,
                            transactionId,
                            customerId,
                            amount,
                            paymentMethod,
                            referenceNumber,
                            createdAt);

                        transaction.Commit();

                        return transactionId;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        // حساب الرصيد من دفتر الحركات فقط.
        private double GetBalance(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int customerId)
        {
            using (var command = new SQLiteCommand(
                @"
                SELECT COALESCE(SUM(Amount), 0)
                FROM CustomerAccountTransactions
                WHERE CustomerId = @customerId;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@customerId", customerId);

                double balance = Convert.ToDouble(
                    command.ExecuteScalar());

                if (double.IsNaN(balance) ||
                    double.IsInfinity(balance))
                {
                    throw new InvalidOperationException(
                        "تعذر حساب رصيد العميل بسبب بيانات مالية غير صحيحة.");
                }

                return Math.Round(
                    balance,
                    2,
                    MidpointRounding.AwayFromZero);
            }
        }

        private string GetActiveUserRole(
            SQLiteConnection connection,
            int userId)
        {
            using (var command = new SQLiteCommand(
                @"
                SELECT Role
                FROM Users
                WHERE Id = @id
                  AND IsActive = 1;",
                connection))
            {
                command.Parameters.AddWithValue("@id", userId);

                object result = command.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                {
                    throw new UnauthorizedAccessException(
                        "المستخدم غير موجود أو غير نشط.");
                }

                return result.ToString();
            }
        }

        private void GetActiveUser(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            out string username,
            out string role)
        {
            username = null;
            role = null;

            using (var command = new SQLiteCommand(
                @"
                SELECT Username, Role
                FROM Users
                WHERE Id = @id
                  AND IsActive = 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@id", userId);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        throw new UnauthorizedAccessException(
                            "المستخدم غير موجود أو غير نشط.");
                    }

                    username = reader["Username"].ToString();
                    role = reader["Role"].ToString();
                }
            }
        }

        private void ValidatePermission(
            SQLiteConnection connection,
            string role,
            string permissionCode,
            string errorMessage)
        {
            if (string.Equals(
                role,
                "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using (var command = new SQLiteCommand(
                @"
                SELECT COUNT(*)
                FROM RolePermissions rp
                INNER JOIN Roles r ON r.Id = rp.RoleId
                INNER JOIN Permissions p ON p.Id = rp.PermissionId
                WHERE r.RoleCode = @role
                  AND p.PermissionCode = @permission
                  AND rp.IsAllowed = 1;",
                connection))
            {
                command.Parameters.AddWithValue("@role", role);
                command.Parameters.AddWithValue(
                    "@permission", permissionCode);

                int allowed = Convert.ToInt32(
                    command.ExecuteScalar());

                if (allowed == 0)
                    throw new UnauthorizedAccessException(errorMessage);
            }
        }

        private void ValidatePermission(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string role,
            string permissionCode,
            string errorMessage)
        {
            if (string.Equals(
                role,
                "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using (var command = new SQLiteCommand(
                @"
                SELECT COUNT(*)
                FROM RolePermissions rp
                INNER JOIN Roles r ON r.Id = rp.RoleId
                INNER JOIN Permissions p ON p.Id = rp.PermissionId
                WHERE r.RoleCode = @role
                  AND p.PermissionCode = @permission
                  AND rp.IsAllowed = 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@role", role);
                command.Parameters.AddWithValue(
                    "@permission", permissionCode);

                int allowed = Convert.ToInt32(
                    command.ExecuteScalar());

                if (allowed == 0)
                    throw new UnauthorizedAccessException(errorMessage);
            }
        }

        private void EnsureCustomerExists(
            SQLiteConnection connection,
            int customerId)
        {
            using (var command = new SQLiteCommand(
                @"
                SELECT COUNT(*)
                FROM Customers
                WHERE Id = @id;",
                connection))
            {
                command.Parameters.AddWithValue("@id", customerId);

                if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                {
                    throw new InvalidOperationException(
                        "العميل غير موجود.");
                }
            }
        }

        private void EnsureCustomerExists(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int customerId)
        {
            using (var command = new SQLiteCommand(
                @"
                SELECT COUNT(*)
                FROM Customers
                WHERE Id = @id;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@id", customerId);

                if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                {
                    throw new InvalidOperationException(
                        "العميل غير موجود.");
                }
            }
        }

        private string NormalizePaymentMethod(string paymentMethod)
        {
            if (string.IsNullOrWhiteSpace(paymentMethod))
            {
                throw new InvalidOperationException(
                    "يجب تحديد طريقة التحصيل.");
            }

            string value = paymentMethod.Trim();

            if (string.Equals(
                value, "Cash", StringComparison.OrdinalIgnoreCase) ||
                value == "نقدي" ||
                value == "نقداً")
            {
                return "Cash";
            }

            if (string.Equals(
                value, "Card", StringComparison.OrdinalIgnoreCase) ||
                value == "بطاقة" ||
                value == "فيزا")
            {
                return "Card";
            }

            if (string.Equals(
                value, "Transfer", StringComparison.OrdinalIgnoreCase) ||
                value == "تحويل" ||
                value == "تحويل بنكي")
            {
                return "Transfer";
            }

            throw new InvalidOperationException(
                "طريقة التحصيل غير معتمدة.");
        }

        private void WriteAuditLog(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            string username,
            long transactionId,
            int customerId,
            double amount,
            string paymentMethod,
            string referenceNumber,
            string createdAt)
        {
            string details =
                "تحصيل مبلغ " +
                amount.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture) +
                " من العميل رقم " + customerId +
                " - طريقة التحصيل: " + paymentMethod;

            if (!string.IsNullOrWhiteSpace(referenceNumber))
            {
                details +=
                    " - رقم المرجع: " + referenceNumber;
            }

            using (var command = new SQLiteCommand(
                @"
                INSERT INTO AuditLogs
                (
                    UserId,
                    Username,
                    ActionType,
                    EntityType,
                    EntityId,
                    Details,
                    DeviceName,
                    CreatedAt
                )
                VALUES
                (
                    @userId,
                    @username,
                    'CollectPayment',
                    'CustomerAccountTransaction',
                    @entityId,
                    @details,
                    @device,
                    @createdAt
                );",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue(
                    "@entityId", transactionId);
                command.Parameters.AddWithValue("@details", details);
                command.Parameters.AddWithValue(
                    "@device", Environment.MachineName);
                command.Parameters.AddWithValue(
                    "@createdAt", createdAt);

                command.ExecuteNonQuery();
            }
        }

        public class CustomerAccountTransaction
        {
            public long Id { get; set; }

            public int CustomerId { get; set; }

            public string TransactionType { get; set; }

            public double Amount { get; set; }

            public string ReferenceType { get; set; }

            public long? ReferenceId { get; set; }

            public string PaymentMethod { get; set; }

            public string ReferenceNumber { get; set; }

            public string Notes { get; set; }

            public int UserId { get; set; }

            public DateTime CreatedAt { get; set; }

            public CustomerAccountTransaction()
            {
                TransactionType = string.Empty;
                ReferenceType = string.Empty;
                PaymentMethod = string.Empty;
                ReferenceNumber = string.Empty;
                Notes = string.Empty;
            }
        }
    }
}
