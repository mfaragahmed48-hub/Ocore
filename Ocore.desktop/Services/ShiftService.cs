
using System;
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

                // إجمالي المبلغ الذي تم رده فعليًا للعميل.
                // لا نفترض أنه يساوي إجمالي قيمة الأصناف المرتجعة.
                EnsureColumn(
                    connection,
                    "SalesReturns",
                    "RefundAmount",
                    "REAL NOT NULL DEFAULT 0");

                // وسيلة رد المبلغ الفعلية وقت تنفيذ المرتجع.
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

        public bool CloseCurrentShift(int userId)
        {
            if (userId <= 0)
                throw new InvalidOperationException("رقم المستخدم غير صحيح.");

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
                                "إغلاق الوردية متاح للصيادلة ومدير النظام فقط.");
                        }

                        string closedAt = DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);

                        using (var command = new SQLiteCommand(@"
                            UPDATE Shifts
                            SET
                                Status = 'Closed',
                                ClosedByUserId = @ClosedByUserId,
                                ClosedAt = @ClosedAt
                            WHERE Id =
                            (
                                SELECT Id
                                FROM Shifts
                                WHERE Status = 'Open'
                                ORDER BY Id DESC
                                LIMIT 1
                            )
                            AND Status = 'Open';",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ClosedByUserId", userId);

                            command.Parameters.AddWithValue(
                                "@ClosedAt", closedAt);

                            int affected = command.ExecuteNonQuery();

                            if (affected == 0)
                            {
                                transaction.Rollback();
                                return false;
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
    }
}
