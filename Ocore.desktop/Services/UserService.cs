
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class UserService
    {
        private readonly string connectionString;
        private readonly PasswordHasher passwordHasher;
        private readonly PermissionService permissionService;
        private readonly AuditService auditService;

        public UserService()
        {
            var database = new DatabaseManager();

            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";

            passwordHasher = new PasswordHasher();
            permissionService = new PermissionService();
            auditService = new AuditService();
        }

        private void EnsurePermission(int userId, string permission)
        {
            if (userId <= 0)
                throw new UnauthorizedAccessException(
                    "هوية المستخدم غير صحيحة.");

            string role;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    @"SELECT Role
                      FROM Users
                      WHERE Id = @Id
                      AND IsActive = 1
                      LIMIT 1;",
                    connection))
                {
                    command.Parameters.AddWithValue("@Id", userId);

                    object result = command.ExecuteScalar();

                    if (result == null || result == DBNull.Value)
                        throw new UnauthorizedAccessException(
                            "المستخدم غير موجود أو حسابه موقوف.");

                    role = result.ToString();
                }
            }

            if (!permissionService.HasPermission(role, permission))
                throw new UnauthorizedAccessException(
                    "ليس لديك صلاحية تنفيذ هذه العملية.");
        }

        public bool CreateUser(
            string username,
            string password,
            string fullName,
            string role)
        {
            if (role != "Manager" || HasManager())
            {
                throw new UnauthorizedAccessException(
                    "لا يمكن إنشاء حساب مدير بهذه الطريقة.");
            }

            return CreateUser(
                0,
                username,
                password,
                fullName,
                role);
        }

        public bool CreateUser(
            int currentUserId,
            string username,
            string password,
            string fullName,
            string role)
        {
            if (currentUserId > 0)
            {
                EnsurePermission(currentUserId, "Users.Create");
            }
            else if (role != "Manager" || HasManager())
            {
                throw new UnauthorizedAccessException(
                    "غير مسموح بإنشاء هذا المستخدم.");
            }

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException("جميع بيانات المستخدم مطلوبة.");
            }

            username = username.Trim();
            fullName = fullName.Trim();
            role = role.Trim();

            // لا يوجد حد أدنى أو أقصى لطول كلمة المرور.

            if (role != "Manager" &&
                role != "Pharmacist" &&
                role != "Cashier")
            {
                throw new ArgumentException("نوع المستخدم غير مسموح.");
            }

            string passwordHash =
                passwordHasher.HashPassword(password);

            int newUserId = 0;
            bool created = false;

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        if (role == "Manager")
                        {
                            using (var check = new SQLiteCommand(
                                "SELECT COUNT(*) FROM Users WHERE Role = 'Manager';",
                                connection,
                                transaction))
                            {
                                int count = Convert.ToInt32(
                                    check.ExecuteScalar());

                                if (count > 0)
                                {
                                    throw new InvalidOperationException(
                                        "حساب المدير موجود بالفعل.");
                                }
                            }
                        }

                        using (var command = new SQLiteCommand(
                            @"INSERT INTO Users
                              (Username, PasswordHash, FullName, Role, IsActive, CreatedAt)
                              VALUES
                              (@username, @hash, @name, @role, 1, @date);",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue("@username", username);
                            command.Parameters.AddWithValue("@hash", passwordHash);
                            command.Parameters.AddWithValue("@name", fullName);
                            command.Parameters.AddWithValue("@role", role);
                            command.Parameters.AddWithValue(
                                "@date",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                            created = command.ExecuteNonQuery() == 1;

                            if (created)
                            {
                                newUserId = Convert.ToInt32(
                                    command.Connection.LastInsertRowId);
                            }
                        }

                        transaction.Commit();
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

            if (created)
            {
                auditService.Log(
                    currentUserId > 0 ? (int?)currentUserId : null,
                    currentUserId > 0 ? GetUsername(currentUserId) : "System",
                    "Create",
                    "User",
                    newUserId,
                    "تم إنشاء المستخدم: " + username +
                    " - الصلاحية: " + role);
            }

            return created;
        }

        private string GetUsername(int userId)
        {
            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    "SELECT Username FROM Users WHERE Id = @Id;",
                    connection))
                {
                    command.Parameters.AddWithValue("@Id", userId);

                    object result = command.ExecuteScalar();

                    return result == null || result == DBNull.Value
                        ? "Unknown"
                        : result.ToString();
                }
            }
        }

        public bool HasManager()
        {
            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    "SELECT COUNT(*) FROM Users WHERE Role = 'Manager';",
                    connection))
                {
                    int count = Convert.ToInt32(
                        command.ExecuteScalar());

                    return count > 0;
                }
            }
        }

        public Models.User GetAuthenticatedUser(
            string username,
            string password)
        {
            if (!Authenticate(username, password))
                return null;

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    @"SELECT Id, Username, FullName, Role, IsActive, CreatedAt
                      FROM Users
                      WHERE Username = @username
                      AND IsActive = 1
                      LIMIT 1;",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@username", username.Trim());

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;

                        return new Models.User
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Username = reader["Username"].ToString(),
                            FullName = reader["FullName"].ToString(),
                            Role = reader["Role"].ToString(),
                            IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                            CreatedAt = DateTime.Parse(reader["CreatedAt"].ToString())
                        };
                    }
                }
            }
        }

        public bool Authenticate(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrEmpty(password))
                return false;

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    @"SELECT PasswordHash
                      FROM Users
                      WHERE Username = @username
                      AND IsActive = 1
                      LIMIT 1;",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@username", username.Trim());

                    object result = command.ExecuteScalar();

                    if (result == null || result == DBNull.Value)
                        return false;

                    return passwordHasher.VerifyPassword(
                        password, result.ToString());
                }
            }
        }

        public List<User> GetAllUsers()
        {
            var users = new List<User>();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    @"SELECT Id, Username, FullName, Role, IsActive, CreatedAt
                      FROM Users
                      ORDER BY Id DESC;",
                    connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            users.Add(new User
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Username = reader["Username"].ToString(),
                                FullName = reader["FullName"].ToString(),
                                Role = reader["Role"].ToString(),
                                IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
                                CreatedAt = DateTime.Parse(
                                    reader["CreatedAt"].ToString())
                            });
                        }
                    }
                }
            }

            return users;
        }

        public bool SetUserActive(
            int currentUserId,
            int targetUserId,
            bool isActive)
        {
            EnsurePermission(currentUserId, "Users.Edit");

            if (currentUserId == targetUserId && !isActive)
            {
                throw new InvalidOperationException(
                    "لا يمكنك إيقاف حسابك الحالي.");
            }

            bool updated;

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    @"UPDATE Users
                      SET IsActive = @active
                      WHERE Id = @id
                      AND Role != 'Manager';",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@active", isActive ? 1 : 0);

                    command.Parameters.AddWithValue(
                        "@id", targetUserId);

                    updated = command.ExecuteNonQuery() == 1;
                }
            }

            if (updated)
            {
                auditService.Log(
                    currentUserId,
                    GetUsername(currentUserId),
                    isActive ? "Activate" : "Deactivate",
                    "User",
                    targetUserId,
                    isActive
                        ? "تم تنشيط حساب المستخدم."
                        : "تم إيقاف حساب المستخدم.");
            }

            return updated;
        }

        public void UpdateUser(
            int currentUserId,
            int userId,
            string fullName,
            string role)
        {
            EnsurePermission(currentUserId, "Users.Edit");

            if (string.IsNullOrWhiteSpace(fullName))
                throw new ArgumentException("الاسم الكامل مطلوب.");

            if (role != "Pharmacist" && role != "Cashier")
                throw new ArgumentException("نوع المستخدم غير صحيح.");

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    @"UPDATE Users
                      SET FullName = @FullName,
                          Role = @Role
                      WHERE Id = @Id
                      AND Role != 'Manager';",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@FullName", fullName.Trim());

                    command.Parameters.AddWithValue("@Role", role);
                    command.Parameters.AddWithValue("@Id", userId);

                    int affectedRows = command.ExecuteNonQuery();

                    if (affectedRows == 0)
                        throw new InvalidOperationException(
                            "لم يتم العثور على المستخدم أو لا يمكن تعديل حساب المدير.");
                }
            }

            auditService.Log(
                currentUserId,
                GetUsername(currentUserId),
                "Update",
                "User",
                userId,
                "تم تعديل بيانات المستخدم: " + fullName.Trim() +
                " - الصلاحية: " + role);
        }

        // إعادة تعيين كلمة مرور مستخدم
        public void ResetUserPassword(
            int currentUserId,
            int userId,
            string newPassword)
        {
            EnsurePermission(currentUserId, "Users.Edit");

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                throw new ArgumentException(
                    "كلمة المرور مطلوبة.");
            }

            if (currentUserId == userId)
            {
                throw new InvalidOperationException(
                    "لا يمكن إعادة تعيين كلمة مرور حسابك من هذه الشاشة.");
            }

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var checkCommand = new SQLiteCommand(
                    @"SELECT COUNT(*)
                      FROM Users
                      WHERE Id = @Id
                      AND Role != 'Manager';",
                    connection))
                {
                    checkCommand.Parameters.AddWithValue("@Id", userId);

                    if (Convert.ToInt32(
                        checkCommand.ExecuteScalar()) == 0)
                    {
                        throw new InvalidOperationException(
                            "المستخدم غير موجود أو لا يمكن تغيير كلمة مرور المدير.");
                    }
                }

                string passwordHash =
                    passwordHasher.HashPassword(newPassword);

                using (var command = new SQLiteCommand(
                    @"UPDATE Users
                      SET PasswordHash = @PasswordHash
                      WHERE Id = @Id
                      AND Role != 'Manager';",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@PasswordHash", passwordHash);

                    command.Parameters.AddWithValue("@Id", userId);

                    if (command.ExecuteNonQuery() == 0)
                    {
                        throw new InvalidOperationException(
                            "فشل تحديث كلمة المرور.");
                    }
                }
            }

            auditService.Log(
                currentUserId,
                GetUsername(currentUserId),
                "PasswordReset",
                "User",
                userId,
                "تم إعادة تعيين كلمة مرور المستخدم رقم: " + userId);
        }

        // ==========================================
        // تغيير بيانات حساب المدير
        // ==========================================

        public bool UpdateManagerAccount(
            int managerUserId,
            string currentPassword,
            string newUsername,
            string newFullName,
            string newPassword)
        {
            if (managerUserId <= 0)
                throw new ArgumentException(
                    "رقم حساب المدير غير صحيح.");

            if (string.IsNullOrWhiteSpace(currentPassword))
                throw new ArgumentException(
                    "أدخل كلمة المرور الحالية.");

            if (string.IsNullOrWhiteSpace(newUsername))
                throw new ArgumentException(
                    "اسم الدخول مطلوب.");

            if (string.IsNullOrWhiteSpace(newFullName))
                throw new ArgumentException(
                    "الاسم الظاهر مطلوب.");

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                throw new ArgumentException(
                    "كلمة المرور الجديدة مطلوبة.");
            }

            newUsername = newUsername.Trim();
            newFullName = newFullName.Trim();

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string storedHash;

                        using (var command = new SQLiteCommand(
                            @"SELECT PasswordHash
                              FROM Users
                              WHERE Id = @Id
                              AND Role = 'Manager'
                              AND IsActive = 1
                              LIMIT 1;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@Id", managerUserId);

                            object result = command.ExecuteScalar();

                            if (result == null ||
                                result == DBNull.Value)
                            {
                                throw new UnauthorizedAccessException(
                                    "حساب المدير غير موجود أو غير نشط.");
                            }

                            storedHash = result.ToString();
                        }

                        if (!passwordHasher.VerifyPassword(
                            currentPassword, storedHash))
                        {
                            throw new UnauthorizedAccessException(
                                "كلمة المرور الحالية غير صحيحة.");
                        }

                        using (var checkCommand = new SQLiteCommand(
                            @"SELECT COUNT(*)
                              FROM Users
                              WHERE Username = @Username
                              AND Id != @Id;",
                            connection,
                            transaction))
                        {
                            checkCommand.Parameters.AddWithValue(
                                "@Username", newUsername);

                            checkCommand.Parameters.AddWithValue(
                                "@Id", managerUserId);

                            if (Convert.ToInt32(
                                checkCommand.ExecuteScalar()) > 0)
                            {
                                throw new InvalidOperationException(
                                    "اسم الدخول مستخدم بالفعل.");
                            }
                        }

                        string newPasswordHash =
                            passwordHasher.HashPassword(newPassword);

                        using (var updateCommand = new SQLiteCommand(
                            @"UPDATE Users
                              SET Username = @Username,
                                  FullName = @FullName,
                                  PasswordHash = @PasswordHash
                              WHERE Id = @Id
                              AND Role = 'Manager';",
                            connection,
                            transaction))
                        {
                            updateCommand.Parameters.AddWithValue(
                                "@Username", newUsername);

                            updateCommand.Parameters.AddWithValue(
                                "@FullName", newFullName);

                            updateCommand.Parameters.AddWithValue(
                                "@PasswordHash", newPasswordHash);

                            updateCommand.Parameters.AddWithValue(
                                "@Id", managerUserId);

                            if (updateCommand.ExecuteNonQuery() != 1)
                            {
                                throw new InvalidOperationException(
                                    "فشل تحديث بيانات المدير.");
                            }
                        }

                        transaction.Commit();
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

            auditService.Log(
                managerUserId,
                newUsername,
                "Update",
                "ManagerAccount",
                managerUserId,
                "تم تغيير اسم الدخول والاسم الظاهر وكلمة المرور.");

            return true;
        }
    }
}
