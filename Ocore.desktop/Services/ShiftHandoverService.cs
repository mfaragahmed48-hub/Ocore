
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public class ShiftHandoverService
    {
        private readonly string connectionString;

        public ShiftHandoverService()
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
                    CREATE TABLE IF NOT EXISTS ShiftHandovers
                    (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ShiftId INTEGER NOT NULL,
                        FromUserId INTEGER NOT NULL,
                        ToUserId INTEGER NOT NULL,
                        ActionByUserId INTEGER NOT NULL,
                        HandoverType TEXT NOT NULL,
                        Notes TEXT NOT NULL DEFAULT '',
                        CreatedAt TEXT NOT NULL,

                        FOREIGN KEY (ShiftId) REFERENCES Shifts(Id),
                        FOREIGN KEY (FromUserId) REFERENCES Users(Id),
                        FOREIGN KEY (ToUserId) REFERENCES Users(Id),
                        FOREIGN KEY (ActionByUserId) REFERENCES Users(Id)
                    );

                    CREATE INDEX IF NOT EXISTS IX_ShiftHandovers_ShiftId
                    ON ShiftHandovers(ShiftId);

                    CREATE INDEX IF NOT EXISTS IX_ShiftHandovers_CreatedAt
                    ON ShiftHandovers(CreatedAt);
                ", connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        // ==========================================
        // المستخدمون المؤهلون لاستلام الشيفت
        // ==========================================

        public List<ShiftUserInfo> GetEligibleReceivers()
        {
            var users = new List<ShiftUserInfo>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    SELECT
                        Id,
                        Username,
                        COALESCE(FullName, Username, '') AS FullName,
                        Role
                    FROM Users
                    WHERE IsActive = 1
                      AND Role IN ('Pharmacist', 'Manager')
                    ORDER BY FullName;", connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        users.Add(new ShiftUserInfo
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Username = Convert.ToString(reader["Username"]),
                            FullName = Convert.ToString(reader["FullName"]),
                            Role = Convert.ToString(reader["Role"])
                        });
                    }
                }
            }

            return users;
        }

        // ==========================================
        // معلومات المسؤول الحالي عن الشيفت
        // ==========================================

        public ShiftHandoverInfo GetCurrentHandoverInfo()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    SELECT
                        s.Id AS ShiftId,
                        s.ShiftNumber,
                        s.OpenedByUserId,

                        COALESCE(opened.FullName, opened.Username, '')
                            AS OpenedByName,

                        COALESCE(
                            (
                                SELECT h.ToUserId
                                FROM ShiftHandovers h
                                WHERE h.ShiftId = s.Id
                                ORDER BY h.Id DESC
                                LIMIT 1
                            ),
                            s.OpenedByUserId
                        ) AS CurrentResponsibleUserId,

                        COALESCE(
                            (
                                SELECT u.FullName
                                FROM ShiftHandovers h
                                INNER JOIN Users u ON u.Id = h.ToUserId
                                WHERE h.ShiftId = s.Id
                                ORDER BY h.Id DESC
                                LIMIT 1
                            ),
                            COALESCE(opened.FullName, opened.Username, '')
                        ) AS CurrentResponsibleName

                    FROM Shifts s
                    LEFT JOIN Users opened
                        ON opened.Id = s.OpenedByUserId

                    WHERE s.Status = 'Open'
                    ORDER BY s.Id DESC
                    LIMIT 1;", connection))
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return new ShiftHandoverInfo
                    {
                        ShiftId = Convert.ToInt32(reader["ShiftId"]),
                        ShiftNumber = Convert.ToString(reader["ShiftNumber"]),
                        OpenedByUserId = Convert.ToInt32(reader["OpenedByUserId"]),
                        OpenedByName = Convert.ToString(reader["OpenedByName"]),
                        CurrentResponsibleUserId =
                            Convert.ToInt32(reader["CurrentResponsibleUserId"]),
                        CurrentResponsibleName =
                            Convert.ToString(reader["CurrentResponsibleName"])
                    };
                }
            }
        }

        // ==========================================
        // تسليم الشيفت بشكل طبيعي
        // ==========================================

        public bool HandoverCurrentShift(
            int handingOverUserId,
            int receivingUserId,
            string notes)
        {
            return TransferShift(
                handingOverUserId,
                receivingUserId,
                false,
                notes);
        }

        // ==========================================
        // استلام اضطراري بعد انقطاع الكهرباء
        // ==========================================

        public bool TakeOverCurrentShift(
            int receivingUserId,
            string notes)
        {
            return TransferShift(
                receivingUserId,
                receivingUserId,
                true,
                notes);
        }

        // ==========================================
        // تنفيذ عملية التسليم أو الاستلام
        // ==========================================

        private bool TransferShift(
            int actingUserId,
            int receivingUserId,
            bool emergencyTakeover,
            string notes)
        {
            if (actingUserId <= 0 || receivingUserId <= 0)
                throw new InvalidOperationException(
                    "بيانات المستخدم غير صحيحة.");

            if (actingUserId == receivingUserId && !emergencyTakeover)
                throw new InvalidOperationException(
                    "لا يمكن تسليم الشيفت لنفس المستخدم.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction =
                    connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        int shiftId;
                        int currentResponsibleUserId;

                        using (var command = new SQLiteCommand(@"
                            SELECT
                                s.Id,
                                COALESCE(
                                    (
                                        SELECT h.ToUserId
                                        FROM ShiftHandovers h
                                        WHERE h.ShiftId = s.Id
                                        ORDER BY h.Id DESC
                                        LIMIT 1
                                    ),
                                    s.OpenedByUserId
                                ) AS CurrentResponsibleUserId

                            FROM Shifts s
                            WHERE s.Status = 'Open'
                            ORDER BY s.Id DESC
                            LIMIT 1;",
                            connection,
                            transaction))
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                throw new InvalidOperationException(
                                    "لا يوجد شيفت مفتوح لاستلامه أو تسليمه.");
                            }

                            shiftId = Convert.ToInt32(reader["Id"]);

                            currentResponsibleUserId =
                                Convert.ToInt32(
                                    reader["CurrentResponsibleUserId"]);
                        }

                        // التأكد من أن المستخدم الذي ينفذ العملية نشط ومصرح له.
                        ValidateEligibleUser(
                            connection,
                            transaction,
                            actingUserId);

                        // التأكد من أن المستلم نشط ومصرح له.
                        ValidateEligibleUser(
                            connection,
                            transaction,
                            receivingUserId);

                        if (emergencyTakeover)
                        {
                            // في الاستلام الاضطراري:
                            // المستخدم الحالي هو المستلم نفسه.
                            if (actingUserId != receivingUserId)
                            {
                                throw new UnauthorizedAccessException(
                                    "بيانات الاستلام الاضطراري غير صحيحة.");
                            }
                        }
                        else
                        {
                            // في التسليم الطبيعي:
                            // يجب أن يكون المنفذ هو المسؤول الحالي.
                            if (actingUserId != currentResponsibleUserId)
                            {
                                throw new UnauthorizedAccessException(
                                    "لا يمكن تسليم الشيفت إلا بواسطة المسؤول الحالي عنه.");
                            }
                        }

                        string handoverType =
                            emergencyTakeover
                                ? "Takeover"
                                : "Handover";

                        string createdAt = DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);

                        using (var command = new SQLiteCommand(@"
                            INSERT INTO ShiftHandovers
                            (
                                ShiftId,
                                FromUserId,
                                ToUserId,
                                ActionByUserId,
                                HandoverType,
                                Notes,
                                CreatedAt
                            )
                            VALUES
                            (
                                @ShiftId,
                                @FromUserId,
                                @ToUserId,
                                @ActionByUserId,
                                @HandoverType,
                                @Notes,
                                @CreatedAt
                            );",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@ShiftId", shiftId);

                            command.Parameters.AddWithValue(
                                "@FromUserId", currentResponsibleUserId);

                            command.Parameters.AddWithValue(
                                "@ToUserId", receivingUserId);

                            command.Parameters.AddWithValue(
                                "@ActionByUserId", actingUserId);

                            command.Parameters.AddWithValue(
                                "@HandoverType", handoverType);

                            command.Parameters.AddWithValue(
                                "@Notes", notes ?? string.Empty);

                            command.Parameters.AddWithValue(
                                "@CreatedAt", createdAt);

                            command.ExecuteNonQuery();
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

        // ==========================================
        // التحقق من صلاحية المستخدم
        // ==========================================

        private static void ValidateEligibleUser(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId)
        {
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

                string role = Convert.ToString(result);

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
                        "التسليم والاستلام متاحان للصيادلة ومدير النظام فقط.");
                }
            }
        }

        // ==========================================
        // سجل تسليم واستلام الشيفت
        // ==========================================

        public List<ShiftHandoverRecord> GetHandoverHistory(int shiftId)
        {
            if (shiftId <= 0)
                throw new ArgumentOutOfRangeException("shiftId");

            var records = new List<ShiftHandoverRecord>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(@"
                    SELECT
                        h.Id,
                        h.ShiftId,
                        h.FromUserId,
                        h.ToUserId,
                        h.ActionByUserId,
                        h.HandoverType,
                        h.Notes,
                        h.CreatedAt,

                        COALESCE(fromUser.FullName, fromUser.Username, '')
                            AS FromUserName,

                        COALESCE(toUser.FullName, toUser.Username, '')
                            AS ToUserName,

                        COALESCE(actionUser.FullName, actionUser.Username, '')
                            AS ActionByName

                    FROM ShiftHandovers h

                    LEFT JOIN Users fromUser
                        ON fromUser.Id = h.FromUserId

                    LEFT JOIN Users toUser
                        ON toUser.Id = h.ToUserId

                    LEFT JOIN Users actionUser
                        ON actionUser.Id = h.ActionByUserId

                    WHERE h.ShiftId = @ShiftId
                    ORDER BY h.Id;",
                    connection))
                {
                    command.Parameters.AddWithValue("@ShiftId", shiftId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            records.Add(new ShiftHandoverRecord
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                ShiftId = Convert.ToInt32(reader["ShiftId"]),
                                FromUserId = Convert.ToInt32(reader["FromUserId"]),
                                ToUserId = Convert.ToInt32(reader["ToUserId"]),
                                ActionByUserId =
                                    Convert.ToInt32(reader["ActionByUserId"]),

                                FromUserName =
                                    Convert.ToString(reader["FromUserName"]),

                                ToUserName =
                                    Convert.ToString(reader["ToUserName"]),

                                ActionByName =
                                    Convert.ToString(reader["ActionByName"]),

                                HandoverType =
                                    Convert.ToString(reader["HandoverType"]),

                                Notes = Convert.ToString(reader["Notes"]),

                                CreatedAt =
                                    Convert.ToString(reader["CreatedAt"])
                            });
                        }
                    }
                }
            }

            return records;
        }

        // ==========================================
        // نماذج البيانات
        // ==========================================

        public class ShiftUserInfo
        {
            public int Id { get; set; }
            public string Username { get; set; }
            public string FullName { get; set; }
            public string Role { get; set; }

            public override string ToString()
            {
                return FullName;
            }
        }

        public class ShiftHandoverInfo
        {
            public int ShiftId { get; set; }
            public string ShiftNumber { get; set; }

            public int OpenedByUserId { get; set; }
            public string OpenedByName { get; set; }

            public int CurrentResponsibleUserId { get; set; }
            public string CurrentResponsibleName { get; set; }
        }

        public class ShiftHandoverRecord
        {
            public int Id { get; set; }
            public int ShiftId { get; set; }

            public int FromUserId { get; set; }
            public int ToUserId { get; set; }
            public int ActionByUserId { get; set; }

            public string FromUserName { get; set; }
            public string ToUserName { get; set; }
            public string ActionByName { get; set; }

            public string HandoverType { get; set; }
            public string Notes { get; set; }
            public string CreatedAt { get; set; }
        }
    }
}
