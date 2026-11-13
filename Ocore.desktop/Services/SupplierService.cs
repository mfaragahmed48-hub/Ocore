
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class SupplierService
    {
        private readonly DatabaseManager databaseManager;
        private readonly PermissionService permissionService;

        public SupplierService()
        {
            databaseManager = new DatabaseManager();
            databaseManager.InitializeDatabase();

            permissionService = new PermissionService();
        }

        public List<Supplier> GetSuppliers(int userId, bool includeInactive = false)
        {
            var suppliers = new List<Supplier>();

            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                string role = GetActiveUserRole(connection, userId);

                if (string.IsNullOrWhiteSpace(role) ||
                    !permissionService.HasPermission(role, "Purchases.View"))
                {
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية عرض الموردين.");
                }

                string sql = @"
                    SELECT Id, Name, Phone, Address, IsActive
                    FROM Suppliers
                    WHERE (@IncludeInactive = 1 OR IsActive = 1)
                    ORDER BY Name COLLATE NOCASE;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@IncludeInactive",
                        includeInactive ? 1 : 0);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            suppliers.Add(new Supplier
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Name = Convert.ToString(reader["Name"]),
                                Phone = Convert.ToString(reader["Phone"]),
                                Address = Convert.ToString(reader["Address"]),
                                IsActive = Convert.ToInt32(reader["IsActive"]) == 1
                            });
                        }
                    }
                }
            }

            return suppliers;
        }

        public int AddSupplier(int userId, string name, string phone, string address)
        {
            name = (name ?? string.Empty).Trim();
            phone = (phone ?? string.Empty).Trim();
            address = (address ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("اسم المورد مطلوب.");

            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string role = GetActiveUserRole(
                            connection, userId, transaction);

                        if (string.IsNullOrWhiteSpace(role) ||
                            !permissionService.HasPermission(
                                role, "Purchases.Create"))
                        {
                            throw new UnauthorizedAccessException(
                                "ليس لديك صلاحية إضافة مورد.");
                        }

                        string sql = @"
                            INSERT INTO Suppliers
                            (Name, Phone, Address, IsActive)
                            VALUES
                            (@Name, @Phone, @Address, 1);

                            SELECT last_insert_rowid();";

                        int supplierId;

                        using (var command = new SQLiteCommand(
                            sql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Name", name);
                            command.Parameters.AddWithValue("@Phone", phone);
                            command.Parameters.AddWithValue("@Address", address);

                            supplierId = Convert.ToInt32(
                                command.ExecuteScalar());
                        }

                        transaction.Commit();
                        return supplierId;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public void UpdateSupplier(
            int userId,
            int supplierId,
            string name,
            string phone,
            string address)
        {
            if (supplierId <= 0)
                throw new ArgumentException("معرف المورد غير صحيح.");

            name = (name ?? string.Empty).Trim();
            phone = (phone ?? string.Empty).Trim();
            address = (address ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("اسم المورد مطلوب.");

            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string role = GetActiveUserRole(
                            connection, userId, transaction);

                        if (string.IsNullOrWhiteSpace(role) ||
                            !permissionService.HasPermission(
                                role, "Purchases.Create"))
                        {
                            throw new UnauthorizedAccessException(
                                "ليس لديك صلاحية تعديل بيانات المورد.");
                        }

                        string sql = @"
                            UPDATE Suppliers
                            SET Name = @Name,
                                Phone = @Phone,
                                Address = @Address
                            WHERE Id = @Id;";

                        using (var command = new SQLiteCommand(
                            sql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@Name", name);
                            command.Parameters.AddWithValue("@Phone", phone);
                            command.Parameters.AddWithValue("@Address", address);
                            command.Parameters.AddWithValue("@Id", supplierId);

                            if (command.ExecuteNonQuery() == 0)
                                throw new Exception("المورد غير موجود.");
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

        public void SetSupplierActive(
            int userId,
            int supplierId,
            bool isActive)
        {
            if (supplierId <= 0)
                throw new ArgumentException("معرف المورد غير صحيح.");

            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string role = GetActiveUserRole(
                            connection, userId, transaction);

                        if (string.IsNullOrWhiteSpace(role) ||
                            !permissionService.HasPermission(
                                role, "Purchases.Create"))
                        {
                            throw new UnauthorizedAccessException(
                                "ليس لديك صلاحية تغيير حالة المورد.");
                        }

                        string sql = @"
                            UPDATE Suppliers
                            SET IsActive = @IsActive
                            WHERE Id = @Id;";

                        using (var command = new SQLiteCommand(
                            sql, connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@IsActive", isActive ? 1 : 0);

                            command.Parameters.AddWithValue("@Id", supplierId);

                            if (command.ExecuteNonQuery() == 0)
                                throw new Exception("المورد غير موجود.");
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

        private string GetActiveUserRole(
            SQLiteConnection connection,
            int userId,
            SQLiteTransaction transaction = null)
        {
            string sql = @"
                SELECT Role
                FROM Users
                WHERE Id = @UserId
                  AND IsActive = 1
                LIMIT 1;";

            using (var command = new SQLiteCommand(
                sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@UserId", userId);

                object result = command.ExecuteScalar();

                return result == null || result == DBNull.Value
                    ? null
                    : Convert.ToString(result);
            }
        }
    }
}
