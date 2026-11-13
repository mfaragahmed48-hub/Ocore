using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class AuditLogService
    {
        private readonly string connectionString;

        public AuditLogService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        public List<AuditLog> GetAllLogs()
        {
            var logs = new List<AuditLog>();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    @"SELECT Id, UserId, Username, ActionType,
                             EntityType, EntityId, Details,
                             DeviceName, CreatedAt
                      FROM AuditLogs
                      ORDER BY Id DESC
                      LIMIT 1000;",
                    connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            logs.Add(new AuditLog
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                UserId = reader["UserId"] == DBNull.Value
                                    ? (int?)null
                                    : Convert.ToInt32(reader["UserId"]),
                                Username = reader["Username"].ToString(),
                                ActionType = GetActionDisplayName(
    reader["ActionType"].ToString()),
                                EntityType = reader["EntityType"].ToString(),
                                EntityId = reader["EntityId"] == DBNull.Value
                                    ? (int?)null
                                    : Convert.ToInt32(reader["EntityId"]),
                                Details = reader["Details"] == DBNull.Value
                                    ? ""
                                    : reader["Details"].ToString(),
                                DeviceName = reader["DeviceName"].ToString(),
                                CreatedAt = DateTime.Parse(
                                    reader["CreatedAt"].ToString())
                            });
                        }
                    }
                }
            }

            return logs;
        }
        private string GetActionDisplayName(string action)
        {
            switch (action)
            {
                case "Create":
                    return "إنشاء";

                case "Update":
                    return "تعديل";

                case "Activate":
                    return "تنشيط حساب";

                case "Deactivate":
                    return "إيقاف حساب";

                case "Delete":
                    return "حذف";

                case "Login":
                    return "تسجيل دخول";

                case "Logout":
                    return "تسجيل خروج";

                default:
                    return action;
            }
        }

    }
}