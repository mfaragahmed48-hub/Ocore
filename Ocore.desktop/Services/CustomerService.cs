using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class CustomerService
    {
        private readonly string connectionString;
        private readonly PermissionService permissionService;
        private readonly AuditService auditService;

        public CustomerService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";

            EnsureCustomerAddressColumns();

            permissionService = new PermissionService();
            auditService = new AuditService();
        }

        #region Database Migration

        private void EnsureCustomerAddressColumns()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                var existingColumns = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                using (var command = new SQLiteCommand(
                    "PRAGMA table_info(Customers);", connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        existingColumns.Add(
                            Convert.ToString(reader["name"]));
                    }
                }

                var requiredColumns = new Dictionary<string, string>
                {
                    { "HouseNumber", "TEXT NOT NULL DEFAULT ''" },
                    { "Street", "TEXT NOT NULL DEFAULT ''" },
                    { "Area", "TEXT NOT NULL DEFAULT ''" },
                    { "Floor", "TEXT NOT NULL DEFAULT ''" },
                    { "Apartment", "TEXT NOT NULL DEFAULT ''" }
                };

                foreach (var column in requiredColumns)
                {
                    if (existingColumns.Contains(column.Key))
                        continue;

                    using (var command = new SQLiteCommand(
                        "ALTER TABLE Customers ADD COLUMN "
                        + column.Key + " " + column.Value + ";",
                        connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }

                // نقل العنوان النصي القديم إلى خانة الشارع
                // فقط إذا كانت جميع الخانات الجديدة فارغة.
                const string migrateLegacyAddressSql = @"
                    UPDATE Customers
                    SET Street = Address
                    WHERE
                        IFNULL(TRIM(Address), '') <> ''
                        AND IFNULL(TRIM(HouseNumber), '') = ''
                        AND IFNULL(TRIM(Street), '') = ''
                        AND IFNULL(TRIM(Area), '') = ''
                        AND IFNULL(TRIM(Floor), '') = ''
                        AND IFNULL(TRIM(Apartment), '') = '';";

                using (var command = new SQLiteCommand(
                    migrateLegacyAddressSql, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        #endregion

        #region Get Customers

        public List<Customer> GetCustomers(
            int currentUserId,
            bool includeInactive = false,
            string searchText = "")
        {
            EnsurePermission(currentUserId, "Customers.View");

            var customers = new List<Customer>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string sql = @"
                    SELECT
                        Id,
                        CustomerCode,
                        Name,
                        Phone,
                        Address,
                        HouseNumber,
                        Street,
                        Area,
                        Floor,
                        Apartment,
                        Notes,
                        IsActive,
                        CreatedAt,
                        CreatedBy
                    FROM Customers
                    WHERE 1 = 1";

                if (!includeInactive)
                    sql += " AND IsActive = 1";

                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    sql += @"
                        AND (
                            Name LIKE @Search
                            OR CustomerCode LIKE @Search
                            OR Phone LIKE @Search
                        )";
                }

                sql += " ORDER BY Name COLLATE NOCASE ASC, Id ASC;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    if (!string.IsNullOrWhiteSpace(searchText))
                    {
                        command.Parameters.AddWithValue(
                            "@Search",
                            "%" + searchText.Trim() + "%");
                    }

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            customers.Add(MapCustomer(reader));
                        }
                    }
                }
            }

            return customers;
        }

        public Customer GetCustomerById(
            int currentUserId,
            int customerId)
        {
            EnsurePermission(currentUserId, "Customers.View");

            if (customerId <= 0)
                throw new ArgumentException("رقم العميل غير صحيح.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                const string sql = @"
                    SELECT
                        Id,
                        CustomerCode,
                        Name,
                        Phone,
                        Address,
                        HouseNumber,
                        Street,
                        Area,
                        Floor,
                        Apartment,
                        Notes,
                        IsActive,
                        CreatedAt,
                        CreatedBy
                    FROM Customers
                    WHERE Id = @Id
                    LIMIT 1;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", customerId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                            return MapCustomer(reader);
                    }
                }
            }

            return null;
        }

        #endregion

        #region Add Customer

        public Customer AddCustomer(
            int currentUserId,
            Customer customer)
        {
            EnsurePermission(currentUserId, "Customers.Create");

            ValidateCustomer(customer);

            string customerCode;
            int customerId;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction(
                    IsolationLevel.Serializable))
                {
                    try
                    {
                        string temporaryCode =
                            "TMP-" + Guid.NewGuid().ToString("N");

                        const string insertSql = @"
                            INSERT INTO Customers
                            (
                                CustomerCode,
                                Name,
                                Phone,
                                Address,
                                HouseNumber,
                                Street,
                                Area,
                                Floor,
                                Apartment,
                                Notes,
                                IsActive,
                                CreatedAt,
                                CreatedBy
                            )
                            VALUES
                            (
                                @CustomerCode,
                                @Name,
                                @Phone,
                                @Address,
                                @HouseNumber,
                                @Street,
                                @Area,
                                @Floor,
                                @Apartment,
                                @Notes,
                                1,
                                @CreatedAt,
                                @CreatedBy
                            );";

                        using (var command = new SQLiteCommand(
                            insertSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@CustomerCode", temporaryCode);

                            command.Parameters.AddWithValue(
                                "@Name", customer.Name.Trim());

                            command.Parameters.AddWithValue(
                                "@Phone", Normalize(customer.Phone));

                            command.Parameters.AddWithValue(
                                "@Address", BuildLegacyAddress(customer));

                            command.Parameters.AddWithValue(
                                "@HouseNumber", Normalize(customer.HouseNumber));

                            command.Parameters.AddWithValue(
                                "@Street", Normalize(customer.Street));

                            command.Parameters.AddWithValue(
                                "@Area", Normalize(customer.Area));

                            command.Parameters.AddWithValue(
                                "@Floor", Normalize(customer.Floor));

                            command.Parameters.AddWithValue(
                                "@Apartment", Normalize(customer.Apartment));

                            command.Parameters.AddWithValue(
                                "@Notes", Normalize(customer.Notes));

                            command.Parameters.AddWithValue(
                                "@CreatedAt",
                                DateTime.Now.ToString(
                                    "yyyy-MM-dd HH:mm:ss",
                                    CultureInfo.InvariantCulture));

                            command.Parameters.AddWithValue(
                                "@CreatedBy", currentUserId);

                            command.ExecuteNonQuery();
                        }

                        using (var command = new SQLiteCommand(
                            "SELECT last_insert_rowid();",
                            connection,
                            transaction))
                        {
                            customerId = Convert.ToInt32(
                                command.ExecuteScalar(),
                                CultureInfo.InvariantCulture);
                        }

                        customerCode =
                            "CUST-" + customerId.ToString(
                                "D6", CultureInfo.InvariantCulture);

                        const string updateCodeSql = @"
                            UPDATE Customers
                            SET CustomerCode = @CustomerCode
                            WHERE Id = @Id;";

                        using (var command = new SQLiteCommand(
                            updateCodeSql, connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@CustomerCode", customerCode);

                            command.Parameters.AddWithValue(
                                "@Id", customerId);

                            if (command.ExecuteNonQuery() != 1)
                                throw new Exception(
                                    "تعذر إنشاء كود العميل.");
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

            customer.Id = customerId;
            customer.CustomerCode = customerCode;
            customer.Address = BuildLegacyAddress(customer);
            customer.IsActive = true;
            customer.CreatedBy = currentUserId;
            customer.CreatedAt = DateTime.Now;

            auditService.Log(
                currentUserId,
                GetUsername(currentUserId),
                "Create",
                "Customer",
                customerId,
                "تم إنشاء العميل: " + customerCode);

            return customer;
        }

        #endregion

        #region Update Customer

        public void UpdateCustomer(
            int currentUserId,
            Customer customer)
        {
            EnsurePermission(currentUserId, "Customers.Edit");

            ValidateCustomer(customer);

            if (customer.Id <= 0)
                throw new ArgumentException("رقم العميل غير صحيح.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                const string sql = @"
                    UPDATE Customers
                    SET
                        Name = @Name,
                        Phone = @Phone,
                        Address = @Address,
                        HouseNumber = @HouseNumber,
                        Street = @Street,
                        Area = @Area,
                        Floor = @Floor,
                        Apartment = @Apartment,
                        Notes = @Notes
                    WHERE Id = @Id;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@Name", customer.Name.Trim());

                    command.Parameters.AddWithValue(
                        "@Phone", Normalize(customer.Phone));

                    command.Parameters.AddWithValue(
                        "@Address", BuildLegacyAddress(customer));

                    command.Parameters.AddWithValue(
                        "@HouseNumber", Normalize(customer.HouseNumber));

                    command.Parameters.AddWithValue(
                        "@Street", Normalize(customer.Street));

                    command.Parameters.AddWithValue(
                        "@Area", Normalize(customer.Area));

                    command.Parameters.AddWithValue(
                        "@Floor", Normalize(customer.Floor));

                    command.Parameters.AddWithValue(
                        "@Apartment", Normalize(customer.Apartment));

                    command.Parameters.AddWithValue(
                        "@Notes", Normalize(customer.Notes));

                    command.Parameters.AddWithValue(
                        "@Id", customer.Id);

                    if (command.ExecuteNonQuery() != 1)
                        throw new Exception("العميل غير موجود.");
                }
            }

            customer.Address = BuildLegacyAddress(customer);

            auditService.Log(
                currentUserId,
                GetUsername(currentUserId),
                "Update",
                "Customer",
                customer.Id,
                "تم تعديل بيانات العميل: " + customer.CustomerCode);
        }

        #endregion

        #region Activate / Deactivate Customer

        public void SetCustomerActive(
            int currentUserId,
            int customerId,
            bool isActive)
        {
            EnsurePermission(currentUserId, "Customers.Edit");

            if (customerId <= 0)
                throw new ArgumentException("رقم العميل غير صحيح.");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                const string sql = @"
                    UPDATE Customers
                    SET IsActive = @IsActive
                    WHERE Id = @Id;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue(
                        "@IsActive", isActive ? 1 : 0);

                    command.Parameters.AddWithValue(
                        "@Id", customerId);

                    if (command.ExecuteNonQuery() != 1)
                        throw new Exception("العميل غير موجود.");
                }
            }

            auditService.Log(
                currentUserId,
                GetUsername(currentUserId),
                isActive ? "Activate" : "Deactivate",
                "Customer",
                customerId,
                isActive
                    ? "تم تنشيط العميل."
                    : "تم إيقاف العميل.");
        }

        #endregion

        #region Permissions

        private void EnsurePermission(
            int userId,
            string permission)
        {
            string role;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                const string sql = @"
                    SELECT Role
                    FROM Users
                    WHERE Id = @Id
                      AND IsActive = 1
                    LIMIT 1;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", userId);

                    object result = command.ExecuteScalar();

                    if (result == null || result == DBNull.Value)
                        throw new UnauthorizedAccessException(
                            "المستخدم غير موجود أو غير نشط.");

                    role = Convert.ToString(
                        result, CultureInfo.InvariantCulture);
                }
            }

            if (!permissionService.HasPermission(role, permission))
            {
                throw new UnauthorizedAccessException(
                    "ليس لديك صلاحية لتنفيذ هذه العملية.");
            }
        }

        #endregion

        #region Helpers

        private string GetUsername(int userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                const string sql = @"
                    SELECT Username
                    FROM Users
                    WHERE Id = @Id
                    LIMIT 1;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Id", userId);

                    object result = command.ExecuteScalar();

                    return result == null || result == DBNull.Value
                        ? string.Empty
                        : Convert.ToString(
                            result, CultureInfo.InvariantCulture);
                }
            }
        }

        private void ValidateCustomer(Customer customer)
        {
            if (customer == null)
                throw new ArgumentNullException(nameof(customer));

            if (string.IsNullOrWhiteSpace(customer.Name))
                throw new ArgumentException("اسم العميل مطلوب.");
        }

        private string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }

        private string BuildLegacyAddress(Customer customer)
        {
            if (customer == null)
                return string.Empty;

            var parts = new List<string>();

            AddAddressPart(parts, "رقم المنزل", customer.HouseNumber);
            AddAddressPart(parts, "الشارع", customer.Street);
            AddAddressPart(parts, "المنطقة", customer.Area);
            AddAddressPart(parts, "الدور", customer.Floor);
            AddAddressPart(parts, "الشقة", customer.Apartment);

            return string.Join(" - ", parts);
        }

        private void AddAddressPart(
            List<string> parts,
            string label,
            string value)
        {
            string normalizedValue = Normalize(value);

            if (!string.IsNullOrWhiteSpace(normalizedValue))
                parts.Add(label + ": " + normalizedValue);
        }

        private Customer MapCustomer(SQLiteDataReader reader)
        {
            var customer = new Customer();

            customer.Id = Convert.ToInt32(reader["Id"]);

            customer.CustomerCode =
                Convert.ToString(reader["CustomerCode"]);

            customer.Name = Convert.ToString(reader["Name"]);
            customer.Phone = Convert.ToString(reader["Phone"]);
            customer.Address = Convert.ToString(reader["Address"]);

            customer.HouseNumber =
                Convert.ToString(reader["HouseNumber"]);

            customer.Street =
                Convert.ToString(reader["Street"]);

            customer.Area =
                Convert.ToString(reader["Area"]);

            customer.Floor =
                Convert.ToString(reader["Floor"]);

            customer.Apartment =
                Convert.ToString(reader["Apartment"]);

            customer.Notes = Convert.ToString(reader["Notes"]);

            customer.IsActive =
                Convert.ToInt32(reader["IsActive"]) == 1;

            DateTime createdAt;

            if (DateTime.TryParse(
                Convert.ToString(reader["CreatedAt"]),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out createdAt))
            {
                customer.CreatedAt = createdAt;
            }

            customer.CreatedBy =
                reader["CreatedBy"] == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(reader["CreatedBy"]);

            return customer;
        }

        #endregion
    }
}