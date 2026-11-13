
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class PermissionService
    {
        private readonly DatabaseManager databaseManager;

        public PermissionService()
        {
            databaseManager = new DatabaseManager();
            databaseManager.InitializeDatabase();
        }

        // التحقق من صلاحية معينة
        public bool HasPermission(string role, string permission)
        {
            if (string.IsNullOrWhiteSpace(role) ||
                string.IsNullOrWhiteSpace(permission))
                return false;

            // مدير النظام يمتلك جميع الصلاحيات
            if (string.Equals(role, "Manager",
                StringComparison.OrdinalIgnoreCase))
                return true;

            string sql = @"
SELECT COUNT(*)
FROM RolePermissions rp
INNER JOIN Roles r ON r.Id = rp.RoleId
INNER JOIN Permissions p ON p.Id = rp.PermissionId
WHERE r.RoleCode = @Role
AND p.PermissionCode = @Permission
AND rp.IsAllowed = 1;";

            using (var connection = new SQLiteConnection(
                "Data Source=" + databaseManager.GetDatabasePath() + ";Version=3;"))
            {
                connection.Open();

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Role", role);
                    command.Parameters.AddWithValue("@Permission", permission);

                    return Convert.ToInt32(command.ExecuteScalar()) > 0;
                }
            }
        }

        // جلب صلاحيات دور معين
        public IEnumerable<string> GetPermissions(string role)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(role))
                return result;

            string sql = @"
SELECT p.PermissionCode
FROM RolePermissions rp
INNER JOIN Roles r ON r.Id = rp.RoleId
INNER JOIN Permissions p ON p.Id = rp.PermissionId
WHERE r.RoleCode = @Role
AND rp.IsAllowed = 1
ORDER BY p.PermissionCode;";

            using (var connection = new SQLiteConnection(
                "Data Source=" + databaseManager.GetDatabasePath() + ";Version=3;"))
            {
                connection.Open();

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Role", role);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(reader["PermissionCode"].ToString());
                        }
                    }
                }
            }

            return result;
        }

        // التحقق من صلاحية إدارة المستخدمين
        public bool CanManageUsers(string role)
        {
            if (string.Equals(role, "Manager",
                StringComparison.OrdinalIgnoreCase))
                return true;

            return HasPermission(role, "Users.View") &&
                   HasPermission(role, "Users.Create");
        }

        // جلب جميع الصلاحيات المسجلة
        public List<PermissionItem> GetAllPermissions()
        {
            var result = new List<PermissionItem>();

            string sql = @"
SELECT PermissionCode, PermissionName, PermissionGroup
FROM Permissions
ORDER BY PermissionGroup, PermissionName;";

            using (var connection = new SQLiteConnection(
                "Data Source=" + databaseManager.GetDatabasePath() + ";Version=3;"))
            {
                connection.Open();

                using (var command = new SQLiteCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new PermissionItem
                        {
                            PermissionCode = reader["PermissionCode"].ToString(),
                            PermissionName = reader["PermissionName"].ToString(),
                            PermissionGroup = reader["PermissionGroup"].ToString()
                        });
                    }
                }
            }

            return result;
        }

        // حفظ صلاحيات الدور
        public void SaveRolePermissions(
            string role,
            List<string> allowedPermissions)
        {
            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException("الدور غير صحيح.");

            if (string.Equals(role, "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "لا يمكن تعديل صلاحيات مدير النظام.");
            }

            if (allowedPermissions == null)
                throw new ArgumentNullException("allowedPermissions");

            string connectionString =
                "Data Source=" + databaseManager.GetDatabasePath() + ";Version=3;";

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // التأكد من وجود الدور
                        string initializeSql = @"
INSERT OR IGNORE INTO RolePermissions
(RoleId, PermissionId, IsAllowed)
SELECT r.Id, p.Id, 0
FROM Roles r
CROSS JOIN Permissions p
WHERE r.RoleCode = @Role;";

                        using (var command = new SQLiteCommand(
                            initializeSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Role", role);
                            command.ExecuteNonQuery();
                        }

                        // إلغاء جميع الصلاحيات القديمة
                        string resetSql = @"
UPDATE RolePermissions
SET IsAllowed = 0
WHERE RoleId = (
    SELECT Id FROM Roles WHERE RoleCode = @Role
);";

                        using (var command = new SQLiteCommand(
                            resetSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Role", role);
                            command.ExecuteNonQuery();
                        }

                        // تفعيل الصلاحيات التي اختارها المدير
                        string updateSql = @"
UPDATE RolePermissions
SET IsAllowed = 1
WHERE RoleId = (
    SELECT Id FROM Roles WHERE RoleCode = @Role
)
AND PermissionId = (
    SELECT Id FROM Permissions
    WHERE PermissionCode = @Permission
);";

                        foreach (string permission in allowedPermissions)
                        {
                            using (var command = new SQLiteCommand(
                                updateSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@Role", role);
                                command.Parameters.AddWithValue(
                                    "@Permission", permission);

                                command.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}
