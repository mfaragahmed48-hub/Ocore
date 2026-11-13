
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public class ShiftService
    {
        private readonly string connectionString;

        public ShiftService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";

            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    CREATE TABLE IF NOT EXISTS Shifts
                    (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ShiftNumber TEXT NOT NULL UNIQUE,
                        Status TEXT NOT NULL DEFAULT 'Open',
                        OpenedByUserId INTEGER NOT NULL,
                        OpenedAt TEXT NOT NULL,
                        ClosedByUserId INTEGER NULL,
                        ClosedAt TEXT NULL,
                        FOREIGN KEY (OpenedByUserId) REFERENCES Users(Id),
                        FOREIGN KEY (ClosedByUserId) REFERENCES Users(Id)
                    );

                    CREATE UNIQUE INDEX IF NOT EXISTS UX_Shifts_OneOpenShift
                    ON Shifts(Status)
                    WHERE Status = 'Open';

                    CREATE INDEX IF NOT EXISTS IX_Shifts_OpenedAt
                    ON Shifts(OpenedAt);

                    CREATE INDEX IF NOT EXISTS IX_Shifts_ClosedAt
                    ON Shifts(ClosedAt);

                    CREATE TABLE IF NOT EXISTS ShiftClosures
                    (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ShiftId INTEGER NOT NULL UNIQUE,

                        ExpectedCash REAL NOT NULL DEFAULT 0,
                        ExpectedCard REAL NOT NULL DEFAULT 0,
                        ExpectedInstaPay REAL NOT NULL DEFAULT 0,
                        ExpectedTransfer REAL NOT NULL DEFAULT 0,

                        ActualCash REAL NOT NULL DEFAULT 0,
                        ActualCard REAL NOT NULL DEFAULT 0,
                        ActualInstaPay REAL NOT NULL DEFAULT 0,
                        ActualTransfer REAL NOT NULL DEFAULT 0,

                        DifferenceCash REAL NOT NULL DEFAULT 0,
                        DifferenceCard REAL NOT NULL DEFAULT 0,
                        DifferenceInstaPay REAL NOT NULL DEFAULT 0,
                        DifferenceTransfer REAL NOT NULL DEFAULT 0,

                        TotalExpected REAL NOT NULL DEFAULT 0,
                        TotalActual REAL NOT NULL DEFAULT 0,
                        TotalDifference REAL NOT NULL DEFAULT 0,

                        ClosedByUserId INTEGER NOT NULL,
                        ClosedAt TEXT NOT NULL,

                        FOREIGN KEY (ShiftId) REFERENCES Shifts(Id),
                        FOREIGN KEY (ClosedByUserId) REFERENCES Users(Id)
                    );

                    CREATE INDEX IF NOT EXISTS IX_ShiftClosures_ClosedAt
                    ON ShiftClosures(ClosedAt);
                ", connection))
                {
                    command.ExecuteNonQuery();
                }

                EnsureColumn(connection, "SalesInvoices", "ShiftId",
                    "INTEGER NULL");

                EnsureColumn(connection, "SalesReturns", "ShiftId",
                    "INTEGER NULL");

                EnsureColumn(
                    connection,
                    "CustomerAccountTransactions",
                    "ShiftId",
                    "INTEGER NULL");

                EnsureColumn(
                    connection,
                    "SalesReturns",
                    "RefundAmount",
                    "REAL NOT NULL DEFAULT 0");

                EnsureColumn(
                    connection,
                    "SalesReturns",
                    "RefundPaymentMethod",
                    "TEXT NOT NULL DEFAULT ''");

                using (var command = new SQLiteCommand(@"
                    CREATE INDEX IF NOT EXISTS IX_SalesInvoices_ShiftId
                    ON SalesInvoices(ShiftId);

                    CREATE INDEX IF NOT EXISTS IX_SalesReturns_ShiftId
                    ON SalesReturns(ShiftId);

                    CREATE INDEX IF NOT EXISTS IX_CustomerAccountTransactions_ShiftId
                    ON CustomerAccountTransactions(ShiftId);
                ", connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void EnsureColumn(
            SQLiteConnection connection,
            string tableName,
            string columnName,
            string columnDefinition)
        {
            bool exists = false;

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(" + tableName + ");",
                connection))
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

            if (exists)
                return;

            using (var command = new SQLiteCommand(
                "ALTER TABLE " + tableName +
                " ADD COLUMN " + columnName + " " +
                columnDefinition + ";",
                connection))
            {
                command.ExecuteNonQuery();
            }
        }

        public static int EnsureOpenShift(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            string movementDate)
        {
            if (connection == null)
                throw new ArgumentNullException("connection");

            if (transaction == null)
                throw new ArgumentNullException("transaction");

            if (userId <= 0)
                throw new InvalidOperationException("رقم المستخدم غير صحيح.");

            using (var command = new SQLiteCommand(@"
                SELECT Id
                FROM Shifts
                WHERE Status = 'Open'
                ORDER BY Id DESC
                LIMIT 1;",
                connection,
                transaction))
            {
                object existingId = command.ExecuteScalar();

                if (existingId != null && existingId != DBNull.Value)
                    return Convert.ToInt32(existingId);
            }

            string openedAt = string.IsNullOrWhiteSpace(movementDate)
                ? DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture)
                : movementDate;

            string shiftNumber =
                "SH-" +
                DateTime.Now.ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture) +
                "-" +
                Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();

            using (var command = new SQLiteCommand(@"
                INSERT INTO Shifts
                (
                    ShiftNumber,
                    Status,
                    OpenedByUserId,
                    OpenedAt
                )
                VALUES
                (
                    @ShiftNumber,
                    'Open',
                    @OpenedByUserId,
                    @OpenedAt
                );

                SELECT last_insert_rowid();",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@ShiftNumber", shiftNumber);

                command.Parameters.AddWithValue(
                    "@OpenedByUserId", userId);

                command.Parameters.AddWithValue(
                    "@OpenedAt", openedAt);

                try
                {
                    return Convert.ToInt32(command.ExecuteScalar());
                }
                catch (SQLiteException)
                {
                    using (var retryCommand = new SQLiteCommand(@"
                        SELECT Id
                        FROM Shifts
                        WHERE Status = 'Open'
                        ORDER BY Id DESC
                        LIMIT 1;",
                        connection,
                        transaction))
                    {
                        object existingId = retryCommand.ExecuteScalar();

                        if (existingId != null && existingId != DBNull.Value)
                            return Convert.ToInt32(existingId);
                    }

                    throw;
                }
            }
        }

        public ShiftInfo GetCurrentShift()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    SELECT
                        s.Id,
                        s.ShiftNumber,
                        s.Status,
                        s.OpenedByUserId,
                        COALESCE(opened.FullName, opened.Username, '') AS OpenedByName,
                        s.OpenedAt,
                        s.ClosedByUserId,
                        COALESCE(closed.FullName, closed.Username, '') AS ClosedByName,
                        s.ClosedAt
                    FROM Shifts s
                    LEFT JOIN Users opened ON opened.Id = s.OpenedByUserId
                    LEFT JOIN Users closed ON closed.Id = s.ClosedByUserId
                    WHERE s.Status = 'Open'
                    ORDER BY s.Id DESC
                    LIMIT 1;",
                    connection))
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return ReadShift(reader);
                }
            }
        }

        // جلب سجل جميع الشيفتات المفتوحة والمغلقة.
        public List<ShiftInfo> GetAllShifts()
        {
            var shifts = new List<ShiftInfo>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    SELECT
                        s.Id,
                        s.ShiftNumber,
                        s.Status,
                        s.OpenedByUserId,
                        COALESCE(opened.FullName, opened.Username, '') AS OpenedByName,
                        s.OpenedAt,
                        s.ClosedByUserId,
                        COALESCE(closed.FullName, closed.Username, '') AS ClosedByName,
                        s.ClosedAt
                    FROM Shifts s
                    LEFT JOIN Users opened
                        ON opened.Id = s.OpenedByUserId
                    LEFT JOIN Users closed
                        ON closed.Id = s.ClosedByUserId
                    ORDER BY s.Id DESC;",
                    connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        shifts.Add(ReadShift(reader));
                    }
                }
            }

            return shifts;
        }

        // جلب الملخص المالي لأي شيفت، مفتوح أو مغلق.
        public ShiftFinancialSummary GetShiftFinancialSummary(int shiftId)
        {
            if (shiftId <= 0)
                throw new ArgumentOutOfRangeException("shiftId");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                return GetFinancialSummary(
                    connection,
                    null,
                    shiftId);
            }
        }

        // ملخص الحركات المالية للشيفت المفتوح.
        public ShiftFinancialSummary GetCurrentShiftFinancialSummary()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                ShiftInfo shift = GetCurrentShift(connection, null);

                if (shift == null)
                    return null;

                return GetFinancialSummary(
                    connection,
                    null,
                    shift.Id);
            }
        }

        private ShiftInfo GetCurrentShift(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            using (var command = new SQLiteCommand(@"
                SELECT
                    s.Id,
                    s.ShiftNumber,
                    s.Status,
                    s.OpenedByUserId,
                    COALESCE(opened.FullName, opened.Username, '') AS OpenedByName,
                    s.OpenedAt,
                    s.ClosedByUserId,
                    COALESCE(closed.FullName, closed.Username, '') AS ClosedByName,
                    s.ClosedAt
                FROM Shifts s
                LEFT JOIN Users opened ON opened.Id = s.OpenedByUserId
                LEFT JOIN Users closed ON closed.Id = s.ClosedByUserId
                WHERE s.Status = 'Open'
                ORDER BY s.Id DESC
                LIMIT 1;",
                connection,
                transaction))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                    return null;

                return ReadShift(reader);
            }
        }

        private ShiftFinancialSummary GetFinancialSummary(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int shiftId)
        {
            var summary = new ShiftFinancialSummary();

            // المقبوضات الفعلية من دفعات فواتير البيع المكتملة فقط.
            // لا يتم استخدام إجمالي الفاتورة أو المديونية الآجلة.
            using (var command = new SQLiteCommand(@"
                SELECT
                    COALESCE(SUM(CASE
                        WHEN p.PaymentMethod = 'Cash'
                        THEN p.Amount ELSE 0 END), 0) AS CashAmount,

                    COALESCE(SUM(CASE
                        WHEN p.PaymentMethod = 'Card'
                        THEN p.Amount ELSE 0 END), 0) AS CardAmount,

                    COALESCE(SUM(CASE
                        WHEN p.PaymentMethod = 'InstaPay'
                        THEN p.Amount ELSE 0 END), 0) AS InstaPayAmount,

                    COALESCE(SUM(CASE
                        WHEN p.PaymentMethod = 'Transfer'
                        THEN p.Amount ELSE 0 END), 0) AS TransferAmount

                FROM SalesInvoicePayments p
                INNER JOIN SalesInvoices i
                    ON i.Id = p.InvoiceId
                WHERE i.ShiftId = @ShiftId
                  AND i.Status = 'Completed';",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@ShiftId", shiftId);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        summary.SalesCash = ReadMoney(reader["CashAmount"]);
                        summary.SalesCard = ReadMoney(reader["CardAmount"]);
                        summary.SalesInstaPay = ReadMoney(reader["InstaPayAmount"]);
                        summary.SalesTransfer = ReadMoney(reader["TransferAmount"]);
                    }
                }
            }

            // التحصيلات من دفتر العميل فقط.
            // Amount سالب في سجل التحصيل، لذلك نعكس الإشارة.
            using (var command = new SQLiteCommand(@"
                SELECT
                    COALESCE(SUM(CASE
                        WHEN PaymentMethod = 'Cash'
                        THEN -Amount ELSE 0 END), 0) AS CashAmount,

                    COALESCE(SUM(CASE
                        WHEN PaymentMethod = 'Card'
                        THEN -Amount ELSE 0 END), 0) AS CardAmount,

                    COALESCE(SUM(CASE
                        WHEN PaymentMethod = 'InstaPay'
                        THEN -Amount ELSE 0 END), 0) AS InstaPayAmount,

                    COALESCE(SUM(CASE
                        WHEN PaymentMethod = 'Transfer'
                        THEN -Amount ELSE 0 END), 0) AS TransferAmount

                FROM CustomerAccountTransactions
                WHERE ShiftId = @ShiftId
                  AND TransactionType = 'Collection'
                  AND Amount < 0;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@ShiftId", shiftId);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        summary.CollectionsCash = ReadMoney(reader["CashAmount"]);
                        summary.CollectionsCard = ReadMoney(reader["CardAmount"]);
                        summary.CollectionsInstaPay = ReadMoney(reader["InstaPayAmount"]);
                        summary.CollectionsTransfer = ReadMoney(reader["TransferAmount"]);
                    }
                }
            }

            // خصم المبلغ الذي تم رده فعليًا فقط، حسب وسيلة الرد المسجلة.
            // لا نستخدم إجمالي قيمة الأصناف المرتجعة.
            using (var command = new SQLiteCommand(@"
                SELECT
                    COALESCE(SUM(CASE
                        WHEN RefundPaymentMethod = 'Cash'
                        THEN RefundAmount ELSE 0 END), 0) AS CashAmount,

                    COALESCE(SUM(CASE
                        WHEN RefundPaymentMethod = 'Card'
                        THEN RefundAmount ELSE 0 END), 0) AS CardAmount,

                    COALESCE(SUM(CASE
                        WHEN RefundPaymentMethod = 'InstaPay'
                        THEN RefundAmount ELSE 0 END), 0) AS InstaPayAmount,

                    COALESCE(SUM(CASE
                        WHEN RefundPaymentMethod = 'Transfer'
                        THEN RefundAmount ELSE 0 END), 0) AS TransferAmount

                FROM SalesReturns
                WHERE ShiftId = @ShiftId
                  AND Status = 'Completed'
                  AND RefundAmount > 0;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@ShiftId", shiftId);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        summary.RefundsCash = ReadMoney(reader["CashAmount"]);
                        summary.RefundsCard = ReadMoney(reader["CardAmount"]);
                        summary.RefundsInstaPay = ReadMoney(reader["InstaPayAmount"]);
                        summary.RefundsTransfer = ReadMoney(reader["TransferAmount"]);
                    }
                }
            }

            // مصروفات الشيفت غير الملغاة، موزعة حسب وسيلة الدفع.
            bool expenseTableExists = false;

            using (var command = new SQLiteCommand(@"
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table'
                  AND name = 'ExpenseTransactions';",
                connection,
                transaction))
            {
                expenseTableExists =
                    Convert.ToInt32(command.ExecuteScalar()) > 0;
            }

            if (expenseTableExists)
            {
                using (var command = new SQLiteCommand(@"
                    SELECT
                        COALESCE(SUM(CASE
                            WHEN PaymentMethod = 'Cash'
                            THEN Amount ELSE 0 END), 0) AS CashAmount,

                        COALESCE(SUM(CASE
                            WHEN PaymentMethod = 'Card'
                            THEN Amount ELSE 0 END), 0) AS CardAmount,

                        COALESCE(SUM(CASE
                            WHEN PaymentMethod = 'InstaPay'
                            THEN Amount ELSE 0 END), 0) AS InstaPayAmount,

                        COALESCE(SUM(CASE
                            WHEN PaymentMethod = 'Transfer'
                            THEN Amount ELSE 0 END), 0) AS TransferAmount

                    FROM ExpenseTransactions
                    WHERE ShiftId = @ShiftId
                      AND COALESCE(IsVoided, 0) = 0;",
                    connection,
                    transaction))
                {
                    command.Parameters.AddWithValue("@ShiftId", shiftId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            summary.ExpensesCash = ReadMoney(reader["CashAmount"]);
                            summary.ExpensesCard = ReadMoney(reader["CardAmount"]);
                            summary.ExpensesInstaPay = ReadMoney(reader["InstaPayAmount"]);
                            summary.ExpensesTransfer = ReadMoney(reader["TransferAmount"]);
                        }
                    }
                }
            }

            // المتوقع لكل وسيلة:
            // المبيعات + التحصيلات - المرتجعات الفعلية - المصروفات.
            summary.ExpectedCash = RoundMoney(
                summary.SalesCash +
                summary.CollectionsCash -
                summary.RefundsCash -
                summary.ExpensesCash);

            summary.ExpectedCard = RoundMoney(
                summary.SalesCard +
                summary.CollectionsCard -
                summary.RefundsCard -
                summary.ExpensesCard);

            summary.ExpectedInstaPay = RoundMoney(
                summary.SalesInstaPay +
                summary.CollectionsInstaPay -
                summary.RefundsInstaPay -
                summary.ExpensesInstaPay);

            summary.ExpectedTransfer = RoundMoney(
                summary.SalesTransfer +
                summary.CollectionsTransfer -
                summary.RefundsTransfer -
                summary.ExpensesTransfer);

            summary.TotalExpected = RoundMoney(
                summary.ExpectedCash +
                summary.ExpectedCard +
                summary.ExpectedInstaPay +
                summary.ExpectedTransfer);

            return summary;
        }

        // إغلاق الشيفت بالمبالغ الفعلية لكل وسيلة دفع.
        public bool CloseCurrentShift(
            int userId,
            double actualCash,
            double actualCard,
            double actualInstaPay,
            double actualTransfer)
        {
            if (userId <= 0)
                throw new InvalidOperationException("رقم المستخدم غير صحيح.");

            ValidateActualAmount(actualCash, "النقدية");
            ValidateActualAmount(actualCard, "الفيزا");
            ValidateActualAmount(actualInstaPay, "إنستا باي");
            ValidateActualAmount(actualTransfer, "التحويل البنكي");

            actualCash = RoundMoney(actualCash);
            actualCard = RoundMoney(actualCard);
            actualInstaPay = RoundMoney(actualInstaPay);
            actualTransfer = RoundMoney(actualTransfer);

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction =
                    connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        string role;

                        using (var command = new SQLiteCommand(@"
                            SELECT Role
                            FROM Users
                            WHERE Id = @UserId
                              AND IsActive = 1
                            LIMIT 1;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue("@UserId", userId);

                            object result = command.ExecuteScalar();

                            if (result == null || result == DBNull.Value)
                            {
                                throw new UnauthorizedAccessException(
                                    "المستخدم غير موجود أو غير نشط.");
                            }

                            role = Convert.ToString(result);
                        }

                        if (!string.Equals(
                                role,
                                "Pharmacist",
                                StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(
                                role,
                                "Manager",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            throw new UnauthorizedAccessException(
                                "إغلاق الشيفت متاح للصيادلة ومدير النظام فقط.");
                        }

                        ShiftInfo shift = GetCurrentShift(
                            connection,
                            transaction);

                        if (shift == null)
                        {
                            transaction.Rollback();
                            return false;
                        }

                        ShiftFinancialSummary summary =
                            GetFinancialSummary(
                                connection,
                                transaction,
                                shift.Id);

                        double totalActual = RoundMoney(
                            actualCash +
                            actualCard +
                            actualInstaPay +
                            actualTransfer);

                        double totalDifference = RoundMoney(
                            totalActual - summary.TotalExpected);

                        double differenceCash = RoundMoney(
                            actualCash - summary.ExpectedCash);

                        double differenceCard = RoundMoney(
                            actualCard - summary.ExpectedCard);

                        double differenceInstaPay = RoundMoney(
                            actualInstaPay - summary.ExpectedInstaPay);

                        double differenceTransfer = RoundMoney(
                            actualTransfer - summary.ExpectedTransfer);

                        string closedAt = DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);

                        // حفظ تقرير الإغلاق أولًا داخل نفس المعاملة.
                        using (var command = new SQLiteCommand(@"
                            INSERT INTO ShiftClosures
                            (
                                ShiftId,

                                ExpectedCash,
                                ExpectedCard,
                                ExpectedInstaPay,
                                ExpectedTransfer,

                                ActualCash,
                                ActualCard,
                                ActualInstaPay,
                                ActualTransfer,

                                DifferenceCash,
                                DifferenceCard,
                                DifferenceInstaPay,
                                DifferenceTransfer,

                                TotalExpected,
                                TotalActual,
                                TotalDifference,

                                ClosedByUserId,
                                ClosedAt
                            )
                            VALUES
                            (
                                @ShiftId,

                                @ExpectedCash,
                                @ExpectedCard,
                                @ExpectedInstaPay,
                                @ExpectedTransfer,

                                @ActualCash,
                                @ActualCard,
                                @ActualInstaPay,
                                @ActualTransfer,

                                @DifferenceCash,
                                @DifferenceCard,
                                @DifferenceInstaPay,
                                @DifferenceTransfer,

                                @TotalExpected,
                                @TotalActual,
                                @TotalDifference,

                                @ClosedByUserId,
                                @ClosedAt
                            );",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue("@ShiftId", shift.Id);

                            command.Parameters.AddWithValue(
                                "@ExpectedCash", summary.ExpectedCash);

                            command.Parameters.AddWithValue(
                                "@ExpectedCard", summary.ExpectedCard);

                            command.Parameters.AddWithValue(
                                "@ExpectedInstaPay", summary.ExpectedInstaPay);

                            command.Parameters.AddWithValue(
                                "@ExpectedTransfer", summary.ExpectedTransfer);

                            command.Parameters.AddWithValue(
                                "@ActualCash", actualCash);

                            command.Parameters.AddWithValue(
                                "@ActualCard", actualCard);

                            command.Parameters.AddWithValue(
                                "@ActualInstaPay", actualInstaPay);

                            command.Parameters.AddWithValue(
                                "@ActualTransfer", actualTransfer);

                            command.Parameters.AddWithValue(
                                "@DifferenceCash", differenceCash);

                            command.Parameters.AddWithValue(
                                "@DifferenceCard", differenceCard);

                            command.Parameters.AddWithValue(
                                "@DifferenceInstaPay", differenceInstaPay);

                            command.Parameters.AddWithValue(
                                "@DifferenceTransfer", differenceTransfer);

                            command.Parameters.AddWithValue(
                                "@TotalExpected", summary.TotalExpected);

                            command.Parameters.AddWithValue(
                                "@TotalActual", totalActual);

                            command.Parameters.AddWithValue(
                                "@TotalDifference", totalDifference);

                            command.Parameters.AddWithValue(
                                "@ClosedByUserId", userId);

                            command.Parameters.AddWithValue(
                                "@ClosedAt", closedAt);

                            command.ExecuteNonQuery();
                        }

                        // تحديث حالة الشيفت في نفس المعاملة.
                        using (var command = new SQLiteCommand(@"
                            UPDATE Shifts
                            SET
                                Status = 'Closed',
                                ClosedByUserId = @ClosedByUserId,
                                ClosedAt = @ClosedAt
                            WHERE Id = @ShiftId
                              AND Status = 'Open';",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ClosedByUserId", userId);

                            command.Parameters.AddWithValue(
                                "@ClosedAt", closedAt);

                            command.Parameters.AddWithValue(
                                "@ShiftId", shift.Id);

                            int affected = command.ExecuteNonQuery();

                            if (affected == 0)
                            {
                                throw new InvalidOperationException(
                                    "تعذر إغلاق الشيفت لأنه لم يعد مفتوحًا.");
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

        // يمنع الإغلاق القديم الذي لا يسجل المبالغ الفعلية.
        public bool CloseCurrentShift(int userId)
        {
            throw new InvalidOperationException(
                "يجب إدخال المبالغ الفعلية للنقدية والفيزا وإنستا باي والتحويل البنكي قبل إغلاق الشيفت.");
        }

        // قراءة تقرير إغلاق شيفت سابق.
        public ShiftClosureInfo GetShiftClosure(int shiftId)
        {
            if (shiftId <= 0)
                throw new ArgumentOutOfRangeException("shiftId");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    SELECT
                        c.*,
                        COALESCE(u.FullName, u.Username, '') AS ClosedByName
                    FROM ShiftClosures c
                    LEFT JOIN Users u
                        ON u.Id = c.ClosedByUserId
                    WHERE c.ShiftId = @ShiftId
                    LIMIT 1;",
                    connection))
                {
                    command.Parameters.AddWithValue("@ShiftId", shiftId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;

                        return new ShiftClosureInfo
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            ShiftId = Convert.ToInt32(reader["ShiftId"]),

                            ExpectedCash = ReadMoney(reader["ExpectedCash"]),
                            ExpectedCard = ReadMoney(reader["ExpectedCard"]),
                            ExpectedInstaPay = ReadMoney(reader["ExpectedInstaPay"]),
                            ExpectedTransfer = ReadMoney(reader["ExpectedTransfer"]),

                            ActualCash = ReadMoney(reader["ActualCash"]),
                            ActualCard = ReadMoney(reader["ActualCard"]),
                            ActualInstaPay = ReadMoney(reader["ActualInstaPay"]),
                            ActualTransfer = ReadMoney(reader["ActualTransfer"]),

                            DifferenceCash = ReadMoney(reader["DifferenceCash"]),
                            DifferenceCard = ReadMoney(reader["DifferenceCard"]),
                            DifferenceInstaPay = ReadMoney(reader["DifferenceInstaPay"]),
                            DifferenceTransfer = ReadMoney(reader["DifferenceTransfer"]),

                            TotalExpected = ReadMoney(reader["TotalExpected"]),
                            TotalActual = ReadMoney(reader["TotalActual"]),
                            TotalDifference = ReadMoney(reader["TotalDifference"]),

                            ClosedByUserId = Convert.ToInt32(reader["ClosedByUserId"]),
                            ClosedByName = Convert.ToString(reader["ClosedByName"]),
                            ClosedAt = Convert.ToString(reader["ClosedAt"])
                        };
                    }
                }
            }
        }

        private static void ValidateActualAmount(
            double amount,
            string paymentMethodName)
        {
            if (double.IsNaN(amount) ||
                double.IsInfinity(amount) ||
                amount < 0)
            {
                throw new InvalidOperationException(
                    "المبلغ الفعلي لـ " + paymentMethodName +
                    " غير صحيح. يجب إدخال صفر أو مبلغ موجب.");
            }
        }

        private static double ReadMoney(object value)
        {
            if (value == null || value == DBNull.Value)
                return 0;

            return Convert.ToDouble(
                value,
                CultureInfo.InvariantCulture);
        }

        private static double RoundMoney(double value)
        {
            return Math.Round(
                value,
                2,
                MidpointRounding.AwayFromZero);
        }

        private static ShiftInfo ReadShift(SQLiteDataReader reader)
        {
            return new ShiftInfo
            {
                Id = Convert.ToInt32(reader["Id"]),
                ShiftNumber = Convert.ToString(reader["ShiftNumber"]),
                Status = Convert.ToString(reader["Status"]),
                OpenedByUserId = Convert.ToInt32(reader["OpenedByUserId"]),
                OpenedByName = Convert.ToString(reader["OpenedByName"]),
                OpenedAt = Convert.ToString(reader["OpenedAt"]),
                ClosedByUserId = reader["ClosedByUserId"] == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(reader["ClosedByUserId"]),
                ClosedByName = Convert.ToString(reader["ClosedByName"]),
                ClosedAt = reader["ClosedAt"] == DBNull.Value
                    ? string.Empty
                    : Convert.ToString(reader["ClosedAt"])
            };
        }

        public class ShiftInfo
        {
            public int Id { get; set; }
            public string ShiftNumber { get; set; }
            public string Status { get; set; }
            public int OpenedByUserId { get; set; }
            public string OpenedByName { get; set; }
            public string OpenedAt { get; set; }
            public int? ClosedByUserId { get; set; }
            public string ClosedByName { get; set; }
            public string ClosedAt { get; set; }
        }

        public class ShiftFinancialSummary
        {
            public double SalesCash { get; set; }
            public double SalesCard { get; set; }
            public double SalesInstaPay { get; set; }
            public double SalesTransfer { get; set; }

            public double CollectionsCash { get; set; }
            public double CollectionsCard { get; set; }
            public double CollectionsInstaPay { get; set; }
            public double CollectionsTransfer { get; set; }

            public double RefundsCash { get; set; }
            public double RefundsCard { get; set; }
            public double RefundsInstaPay { get; set; }
            public double RefundsTransfer { get; set; }

            public double ExpensesCash { get; set; }
            public double ExpensesCard { get; set; }
            public double ExpensesInstaPay { get; set; }
            public double ExpensesTransfer { get; set; }

            public double ExpectedCash { get; set; }
            public double ExpectedCard { get; set; }
            public double ExpectedInstaPay { get; set; }
            public double ExpectedTransfer { get; set; }

            public double TotalExpected { get; set; }
        }

        public class ShiftClosureInfo
        {
            public int Id { get; set; }
            public int ShiftId { get; set; }

            public double ExpectedCash { get; set; }
            public double ExpectedCard { get; set; }
            public double ExpectedInstaPay { get; set; }
            public double ExpectedTransfer { get; set; }

            public double ActualCash { get; set; }
            public double ActualCard { get; set; }
            public double ActualInstaPay { get; set; }
            public double ActualTransfer { get; set; }

            public double DifferenceCash { get; set; }
            public double DifferenceCard { get; set; }
            public double DifferenceInstaPay { get; set; }
            public double DifferenceTransfer { get; set; }

            public double TotalExpected { get; set; }
            public double TotalActual { get; set; }
            public double TotalDifference { get; set; }

            public int ClosedByUserId { get; set; }
            public string ClosedByName { get; set; }
            public string ClosedAt { get; set; }
        }
    }
}
