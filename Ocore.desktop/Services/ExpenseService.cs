
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public class ExpenseService
    {
        private readonly string connectionString;

        private static readonly string[] ExpenseCategories =
        {
            "كهرباء ومياه",
            "إيجار",
            "نظافة",
            "صيانة",
            "مواصلات",
            "ضيافة",
            "مستلزمات تشغيل",
            "رواتب وأجور",
            "أخرى"
        };

        public ExpenseService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";

            new ShiftService();

            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS ExpenseTransactions
                    (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ShiftId INTEGER NOT NULL,
                        Amount REAL NOT NULL,
                        Category TEXT NOT NULL,
                        Description TEXT NOT NULL DEFAULT '',
                        PaymentMethod TEXT NOT NULL,
                        CreatedByUserId INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        IsVoided INTEGER NOT NULL DEFAULT 0,
                        VoidedByUserId INTEGER NULL,
                        VoidedAt TEXT NULL,
                        VoidReason TEXT NULL,

                        FOREIGN KEY (ShiftId) REFERENCES Shifts(Id),
                        FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id),
                        FOREIGN KEY (VoidedByUserId) REFERENCES Users(Id),

                        CHECK (Amount > 0),
                        CHECK (
                            PaymentMethod IN
                            ('Cash', 'Card', 'InstaPay', 'Transfer')
                        ),
                        CHECK (IsVoided IN (0, 1))
                    );

                    CREATE INDEX IF NOT EXISTS
                    IX_ExpenseTransactions_ShiftId
                    ON ExpenseTransactions(ShiftId);

                    CREATE INDEX IF NOT EXISTS
                    IX_ExpenseTransactions_CreatedAt
                    ON ExpenseTransactions(CreatedAt);

                    CREATE INDEX IF NOT EXISTS
                    IX_ExpenseTransactions_IsVoided
                    ON ExpenseTransactions(IsVoided);
                ", connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public static string[] GetCategories()
        {
            return (string[])ExpenseCategories.Clone();
        }

        private static void ValidateAmount(double amount)
        {
            if (double.IsNaN(amount) ||
                double.IsInfinity(amount) ||
                amount <= 0)
            {
                throw new InvalidOperationException(
                    "قيمة المصروف يجب أن تكون أكبر من صفر.");
            }
        }

        private static double RoundMoney(double amount)
        {
            return Math.Round(
                amount,
                2,
                MidpointRounding.AwayFromZero);
        }

        private static void ValidatePaymentMethod(string paymentMethod)
        {
            if (paymentMethod != "Cash" &&
                paymentMethod != "Card" &&
                paymentMethod != "InstaPay" &&
                paymentMethod != "Transfer")
            {
                throw new InvalidOperationException(
                    "وسيلة دفع المصروف غير صحيحة.");
            }
        }

        private static void ValidateCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                throw new InvalidOperationException(
                    "يجب اختيار بند المصروف.");
            }

            category = category.Trim();

            bool valid = false;

            foreach (string item in ExpenseCategories)
            {
                if (string.Equals(
                    item,
                    category,
                    StringComparison.Ordinal))
                {
                    valid = true;
                    break;
                }
            }

            if (!valid)
            {
                throw new InvalidOperationException(
                    "بند المصروف غير موجود في القائمة.");
            }
        }

        private static void ValidateUser(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            bool managerOnly)
        {
            if (userId <= 0)
            {
                throw new UnauthorizedAccessException(
                    "رقم المستخدم غير صحيح.");
            }

            using (var command = new SQLiteCommand(@"
                SELECT Role
                FROM Users
                WHERE Id = @UserId
                  AND IsActive = 1
                LIMIT 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@UserId",
                    userId);

                object result = command.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                {
                    throw new UnauthorizedAccessException(
                        "المستخدم غير موجود أو حسابه موقوف.");
                }

                string role = Convert.ToString(result);

                if (managerOnly)
                {
                    if (role != "Manager")
                    {
                        throw new UnauthorizedAccessException(
                            "إلغاء المصروفات متاح لمدير النظام فقط.");
                    }
                }
                else
                {
                    if (role != "Manager" &&
                        role != "Pharmacist")
                    {
                        throw new UnauthorizedAccessException(
                            "ليس لديك صلاحية تسجيل المصروفات.");
                    }
                }
            }
        }

        // تسجيل مصروف جديد وربطه بالشيفت المفتوح.
        public int AddExpense(
            int userId,
            double amount,
            string category,
            string description,
            string paymentMethod)
        {
            ValidateAmount(amount);
            ValidateCategory(category);
            ValidatePaymentMethod(paymentMethod);

            amount = RoundMoney(amount);

            description = string.IsNullOrWhiteSpace(description)
                ? string.Empty
                : description.Trim();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction =
                    connection.BeginTransaction(
                        IsolationLevel.Serializable))
                {
                    try
                    {
                        ValidateUser(
                            connection,
                            transaction,
                            userId,
                            false);

                        int shiftId;

                        using (var command = new SQLiteCommand(@"
                            SELECT Id
                            FROM Shifts
                            WHERE Status = 'Open'
                            ORDER BY Id DESC
                            LIMIT 1;",
                            connection,
                            transaction))
                        {
                            object result = command.ExecuteScalar();

                            if (result == null ||
                                result == DBNull.Value)
                            {
                                throw new InvalidOperationException(
                                    "لا يوجد شيفت مفتوح. "
                                    + "يجب فتح الشيفت قبل تسجيل المصروف.");
                            }

                            shiftId = Convert.ToInt32(result);
                        }

                        string createdAt = DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);

                        using (var command = new SQLiteCommand(@"
                            INSERT INTO ExpenseTransactions
                            (
                                ShiftId,
                                Amount,
                                Category,
                                Description,
                                PaymentMethod,
                                CreatedByUserId,
                                CreatedAt,
                                IsVoided
                            )
                            VALUES
                            (
                                @ShiftId,
                                @Amount,
                                @Category,
                                @Description,
                                @PaymentMethod,
                                @CreatedByUserId,
                                @CreatedAt,
                                0
                            );

                            SELECT last_insert_rowid();",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ShiftId", shiftId);

                            command.Parameters.AddWithValue(
                                "@Amount", amount);

                            command.Parameters.AddWithValue(
                                "@Category", category.Trim());

                            command.Parameters.AddWithValue(
                                "@Description", description);

                            command.Parameters.AddWithValue(
                                "@PaymentMethod", paymentMethod);

                            command.Parameters.AddWithValue(
                                "@CreatedByUserId", userId);

                            command.Parameters.AddWithValue(
                                "@CreatedAt", createdAt);

                            int expenseId = Convert.ToInt32(
                                command.ExecuteScalar());

                            transaction.Commit();

                            return expenseId;
                        }
                    }
                    catch
                    {
                        try
                        {
                            transaction.Rollback();
                        }
                        catch
                        {
                        }

                        throw;
                    }
                }
            }
        }

        // إلغاء مصروف: المدير فقط.
        // لا يسمح بإلغاء مصروف تابع لشيفت مغلق.
        // لا يتم حذف السجل الأصلي.
        public bool CancelExpense(
            int managerUserId,
            int expenseId,
            string reason)
        {
            if (expenseId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "expenseId",
                    "رقم المصروف غير صحيح.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new InvalidOperationException(
                    "يجب كتابة سبب إلغاء المصروف.");
            }

            reason = reason.Trim();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction =
                    connection.BeginTransaction(
                        IsolationLevel.Serializable))
                {
                    try
                    {
                        ValidateUser(
                            connection,
                            transaction,
                            managerUserId,
                            true);

                        string shiftStatus;
                        int isVoided;

                        // قراءة حالة المصروف والشيفت قبل السماح بالإلغاء.
                        using (var command = new SQLiteCommand(@"
                            SELECT
                                s.Status,
                                COALESCE(e.IsVoided, 0) AS IsVoided
                            FROM ExpenseTransactions e
                            INNER JOIN Shifts s
                                ON s.Id = e.ShiftId
                            WHERE e.Id = @ExpenseId
                            LIMIT 1;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ExpenseId",
                                expenseId);

                            using (var reader = command.ExecuteReader())
                            {
                                if (!reader.Read())
                                {
                                    throw new InvalidOperationException(
                                        "المصروف غير موجود.");
                                }

                                shiftStatus = Convert.ToString(
                                    reader["Status"]);

                                isVoided = Convert.ToInt32(
                                    reader["IsVoided"]);
                            }
                        }

                        if (isVoided == 1)
                        {
                            throw new InvalidOperationException(
                                "تم إلغاء هذا المصروف بالفعل.");
                        }

                        if (!string.Equals(
                            shiftStatus,
                            "Open",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException(
                                "لا يمكن إلغاء مصروف مرتبط بشيفت مغلق. "
                                + "يجب معالجة الخطأ بقيد عكسي موثق "
                                + "في شيفت مفتوح.");
                        }

                        using (var command = new SQLiteCommand(@"
                            UPDATE ExpenseTransactions
                            SET
                                IsVoided = 1,
                                VoidedByUserId = @ManagerUserId,
                                VoidedAt = @VoidedAt,
                                VoidReason = @VoidReason
                            WHERE Id = @ExpenseId
                              AND IsVoided = 0
                              AND EXISTS
                              (
                                  SELECT 1
                                  FROM Shifts s
                                  WHERE s.Id = ExpenseTransactions.ShiftId
                                    AND s.Status = 'Open'
                              );",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ManagerUserId",
                                managerUserId);

                            command.Parameters.AddWithValue(
                                "@VoidedAt",
                                DateTime.Now.ToString(
                                    "yyyy-MM-dd HH:mm:ss",
                                    CultureInfo.InvariantCulture));

                            command.Parameters.AddWithValue(
                                "@VoidReason",
                                reason);

                            command.Parameters.AddWithValue(
                                "@ExpenseId",
                                expenseId);

                            int affected = command.ExecuteNonQuery();

                            if (affected == 0)
                            {
                                throw new InvalidOperationException(
                                    "تعذر إلغاء المصروف. "
                                    + "ربما تغيرت حالة الشيفت أو المصروف.");
                            }
                        }

                        transaction.Commit();
                        return true;
                    }
                    catch
                    {
                        try
                        {
                            transaction.Rollback();
                        }
                        catch
                        {
                        }

                        throw;
                    }
                }
            }
        }

        // عرض جميع مصروفات شيفت معين، بما فيها الملغاة.
        public List<ExpenseInfo> GetExpensesByShift(int shiftId)
        {
            if (shiftId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "shiftId");
            }

            var expenses = new List<ExpenseInfo>();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    SELECT
                        e.Id,
                        e.ShiftId,
                        e.Amount,
                        e.Category,
                        e.Description,
                        e.PaymentMethod,
                        e.CreatedByUserId,
                        COALESCE(created.FullName, created.Username, '')
                            AS CreatedByName,
                        e.CreatedAt,
                        e.IsVoided,
                        e.VoidedByUserId,
                        COALESCE(voided.FullName, voided.Username, '')
                            AS VoidedByName,
                        e.VoidedAt,
                        e.VoidReason

                    FROM ExpenseTransactions e

                    LEFT JOIN Users created
                        ON created.Id = e.CreatedByUserId

                    LEFT JOIN Users voided
                        ON voided.Id = e.VoidedByUserId

                    WHERE e.ShiftId = @ShiftId

                    ORDER BY e.Id DESC;",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@ShiftId",
                        shiftId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            expenses.Add(new ExpenseInfo
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                ShiftId = Convert.ToInt32(reader["ShiftId"]),
                                Amount = Convert.ToDouble(
                                    reader["Amount"],
                                    CultureInfo.InvariantCulture),
                                Category = Convert.ToString(
                                    reader["Category"]),
                                Description = Convert.ToString(
                                    reader["Description"]),
                                PaymentMethod = Convert.ToString(
                                    reader["PaymentMethod"]),
                                CreatedByUserId = Convert.ToInt32(
                                    reader["CreatedByUserId"]),
                                CreatedByName = Convert.ToString(
                                    reader["CreatedByName"]),
                                CreatedAt = Convert.ToString(
                                    reader["CreatedAt"]),
                                IsVoided = Convert.ToInt32(
                                    reader["IsVoided"]) == 1,
                                VoidedByUserId =
                                    reader["VoidedByUserId"] == DBNull.Value
                                    ? (int?)null
                                    : Convert.ToInt32(
                                        reader["VoidedByUserId"]),
                                VoidedByName = Convert.ToString(
                                    reader["VoidedByName"]),
                                VoidedAt =
                                    reader["VoidedAt"] == DBNull.Value
                                    ? string.Empty
                                    : Convert.ToString(
                                        reader["VoidedAt"]),
                                VoidReason =
                                    reader["VoidReason"] == DBNull.Value
                                    ? string.Empty
                                    : Convert.ToString(
                                        reader["VoidReason"])
                            });
                        }
                    }
                }
            }

            return expenses;
        }

        public class ExpenseInfo
        {
            public int Id { get; set; }
            public int ShiftId { get; set; }

            public double Amount { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
            public string PaymentMethod { get; set; }

            public int CreatedByUserId { get; set; }
            public string CreatedByName { get; set; }
            public string CreatedAt { get; set; }

            public bool IsVoided { get; set; }

            public int? VoidedByUserId { get; set; }
            public string VoidedByName { get; set; }
            public string VoidedAt { get; set; }
            public string VoidReason { get; set; }
        }
    }
}
