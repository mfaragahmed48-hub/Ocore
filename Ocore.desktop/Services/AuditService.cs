
using System;
using System.Data.SQLite;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public class AuditService
    {
        private readonly string connectionString;

        public AuditService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        public void Log(
            int? userId,
            string username,
            string actionType,
            string entityType,
            int? entityId,
            string details)
        {
            if (string.IsNullOrWhiteSpace(username))
                username = "Unknown";

            if (string.IsNullOrWhiteSpace(actionType))
                throw new ArgumentException("نوع العملية مطلوب.");

            if (string.IsNullOrWhiteSpace(entityType))
                throw new ArgumentException("نوع العنصر مطلوب.");

            const string sql = @"
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
    @UserId,
    @Username,
    @ActionType,
    @EntityType,
    @EntityId,
    @Details,
    @DeviceName,
    @CreatedAt
);";

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command =
                    new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@UserId",
                        (object)userId ?? DBNull.Value);

                    command.Parameters.AddWithValue(
                        "@Username", username);

                    command.Parameters.AddWithValue(
                        "@ActionType", actionType);

                    command.Parameters.AddWithValue(
                        "@EntityType", entityType);

                    command.Parameters.AddWithValue(
                        "@EntityId",
                        (object)entityId ?? DBNull.Value);

                    command.Parameters.AddWithValue(
                        "@Details",
                        (object)details ?? DBNull.Value);

                    command.Parameters.AddWithValue(
                        "@DeviceName", Environment.MachineName);

                    command.Parameters.AddWithValue(
                        "@CreatedAt",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
