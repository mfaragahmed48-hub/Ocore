
using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Windows;

namespace Ocore.desktop.Data
{
    public class DatabaseManager
    {
        private readonly string databasePath;
        private readonly string backupFolder;

        // الإصدار 8: السماح بتعدد كروت الأسعار للدواء الأساسي نفسه.
        private const int CurrentDatabaseVersion = 8;
        private const int MaximumBackupCount = 30;

        public static string GetStorageRootPath()
        {
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            string testModeFile = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "OCORE_TEST_MODE.txt");

            string folderName = File.Exists(testModeFile)
                ? "OCORE_TEST"
                : "OCORE";

            return Path.Combine(localAppData, folderName);
        }

        public DatabaseManager()
        {
            string rootFolder = GetStorageRootPath();
            string folder = Path.Combine(rootFolder, "Data");

            Directory.CreateDirectory(folder);

            databasePath = Path.Combine(folder, "OCORE.db");
            backupFolder = Path.Combine(rootFolder, "Backups");

            Directory.CreateDirectory(backupFolder);
        }

        public void InitializeDatabase()
        {
            bool databaseAlreadyExisted = File.Exists(databasePath);

            if (!databaseAlreadyExisted)
            {
                SQLiteConnection.CreateFile(databasePath);
            }

            using (var connection = new SQLiteConnection(
                "Data Source=" + databasePath + ";Version=3;"))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    "PRAGMA foreign_keys = ON;", connection))
                {
                    command.ExecuteNonQuery();
                }

                int currentVersion = GetDatabaseVersion(connection);

                if (currentVersion > CurrentDatabaseVersion)
                {
                    throw new InvalidOperationException(
                        "قاعدة البيانات أحدث من إصدار البرنامج الحالي. " +
                        "لا يمكن فتحها بهذا الإصدار حتى لا تتعرض البيانات للخطر.");
                }

                bool requiresUpgrade =
                    currentVersion < CurrentDatabaseVersion;

                bool requiresSalesCustomerIdRepair =
                    databaseAlreadyExisted &&
                    !ColumnExists(
                        connection,
                        "SalesInvoices",
                        "CustomerId");

                bool requiresCustomerAccountCustomerIdRepair =
                    databaseAlreadyExisted &&
                    !ColumnExists(
                        connection,
                        "CustomerAccountTransactions",
                        "CustomerId");

                bool requiresProductMaxDiscountRepair =
                    databaseAlreadyExisted &&
                    !ColumnExists(
                        connection,
                        "Products",
                        "MaxDiscountPercentage");

                bool requiresThermalPrinterSettingsRepair =
                    databaseAlreadyExisted &&
                    (
                        !ColumnExists(
                            connection,
                            "PharmacySettings",
                            "ThermalPrinterName") ||
                        !ColumnExists(
                            connection,
                            "PharmacySettings",
                            "ThermalPrintWidthDots")
                    );

                bool requiresPrintingProfilesRepair =
                    databaseAlreadyExisted &&
                    (
                        !TableExists(
                            connection,
                            "PrintingProfiles") ||
                        !TableExists(
                            connection,
                            "PrintingMigrationState")
                    );

                bool requiresPrinterConfigurationsRepair =
                    databaseAlreadyExisted &&
                    !TableExists(
                        connection,
                        "PrinterConfigurations");

                bool requiresSchemaRepair =
                    requiresSalesCustomerIdRepair ||
                    requiresCustomerAccountCustomerIdRepair ||
                    requiresProductMaxDiscountRepair ||
                    requiresThermalPrinterSettingsRepair ||
                    requiresPrintingProfilesRepair ||
                    requiresPrinterConfigurationsRepair;

                if (databaseAlreadyExisted &&
                    (requiresUpgrade || requiresSchemaRepair))
                {
                    string backupName =
                        "OCORE_Backup_Before_Update_" +
                        DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                        ".db";

                    CreateDatabaseBackup(connection, backupName);
                }

                DatabaseSchema.CreateCoreTables(connection);

                // ترحيل قاعدة البيانات إلى الإصدار 8:
                // السماح بوجود عدة كروت أسعار مرتبطة بالدواء الأساسي نفسه.
                if (currentVersion < 8)
                {
                    MigrateProductPriceCardsV8(connection);
                }

                if (!ColumnExists(
                    connection,
                    "CustomerAccountTransactions",
                    "CustomerId"))
                {
                    throw new InvalidOperationException(
                        "تعذر إكمال تهيئة قاعدة البيانات: " +
                        "العمود CustomerId غير موجود في " +
                        "CustomerAccountTransactions.");
                }

                if (!ColumnExists(
                    connection,
                    "Products",
                    "MaxDiscountPercentage"))
                {
                    throw new InvalidOperationException(
                        "تعذر إكمال تهيئة قاعدة البيانات: " +
                        "العمود MaxDiscountPercentage غير موجود في Products.");
                }

                string[] thermalPrinterColumns =
                {
                    "ThermalPrinterName",
                    "ThermalPrintMode",
                    "ThermalPaperWidth",
                    "ThermalPrintWidthDots",
                    "ThermalAutoCut",
                    "ThermalOpenCashDrawer",
                    "ThermalFontScale"
                };

                foreach (string columnName in thermalPrinterColumns)
                {
                    if (!ColumnExists(
                        connection,
                        "PharmacySettings",
                        columnName))
                    {
                        throw new InvalidOperationException(
                            "تعذر إكمال تهيئة قاعدة البيانات: " +
                            "العمود " + columnName +
                            " غير موجود في PharmacySettings.");
                    }
                }

                if (!TableExists(
                    connection,
                    "PrintingProfiles"))
                {
                    throw new InvalidOperationException(
                        "تعذر إكمال تهيئة قاعدة البيانات: " +
                        "جدول PrintingProfiles غير موجود.");
                }

                if (!TableExists(
                    connection,
                    "PrintingMigrationState"))
                {
                    throw new InvalidOperationException(
                        "تعذر إكمال تهيئة قاعدة البيانات: " +
                        "جدول PrintingMigrationState غير موجود.");
                }

                if (!TableExists(
                    connection,
                    "PrinterConfigurations"))
                {
                    throw new InvalidOperationException(
                        "تعذر إكمال تهيئة قاعدة البيانات: " +
                        "جدول PrinterConfigurations غير موجود.");
                }

                SetDatabaseVersion(connection, CurrentDatabaseVersion);

                if (databaseAlreadyExisted &&
                    (requiresUpgrade || requiresSchemaRepair))
                {
                    CleanOldUpdateBackups();
                }

                using (var command = new SQLiteCommand(
                    @"CREATE TABLE IF NOT EXISTS SystemTest (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TestName TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );", connection))
                {
                    command.ExecuteNonQuery();
                }

                CreateDailyBackup(connection);
            }
        }

        private void MigrateProductPriceCardsV8(
            SQLiteConnection connection)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    // إزالة الفهرس القديم الذي كان يمنع تعدد كروت الأسعار.
                    using (var command = new SQLiteCommand(
                        "DROP INDEX IF EXISTS UX_Products_DrugCatalogId;",
                        connection,
                        transaction))
                    {
                        command.ExecuteNonQuery();
                    }

                    // التأكد من وجود فهرس غير فريد يسمح بتعدد الكروت.
                    using (var command = new SQLiteCommand(@"
                CREATE INDEX IF NOT EXISTS
                IX_Products_DrugCatalogId
                ON Products(DrugCatalogId)
                WHERE DrugCatalogId IS NOT NULL;
            ", connection, transaction))
                    {
                        command.ExecuteNonQuery();
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

        private bool ColumnExists(
            SQLiteConnection connection,
            string tableName,
            string columnName)
        {
            using (var command = new SQLiteCommand(
                "PRAGMA table_info(" + tableName + ");",
                connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(
                        reader["name"].ToString(),
                        columnName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TableExists(
            SQLiteConnection connection,
            string tableName)
        {
            using (var command = new SQLiteCommand(
                @"SELECT COUNT(*)
                  FROM sqlite_master
                  WHERE type = 'table'
                  AND name = @tableName;",
                connection))
            {
                command.Parameters.AddWithValue(
                    "@tableName",
                    tableName);

                return Convert.ToInt32(
                    command.ExecuteScalar()) > 0;
            }
        }

        private int GetDatabaseVersion(SQLiteConnection connection)
        {
            using (var command = new SQLiteCommand(
                "PRAGMA user_version;", connection))
            {
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private void SetDatabaseVersion(
            SQLiteConnection connection,
            int version)
        {
            using (var command = new SQLiteCommand(
                "PRAGMA user_version = " + version + ";",
                connection))
            {
                command.ExecuteNonQuery();
            }
        }

        private void CreateDatabaseBackup(
            SQLiteConnection sourceConnection,
            string backupName)
        {
            Directory.CreateDirectory(backupFolder);

            string backupPath = Path.Combine(
                backupFolder,
                backupName);

            try
            {
                if (File.Exists(backupPath))
                {
                    throw new IOException(
                        "ملف النسخة الاحتياطية موجود بالفعل.");
                }

                SQLiteConnection.CreateFile(backupPath);

                using (var destinationConnection =
                    new SQLiteConnection(
                        "Data Source=" + backupPath + ";Version=3;"))
                {
                    destinationConnection.Open();

                    sourceConnection.BackupDatabase(
                        destinationConnection,
                        "main",
                        "main",
                        -1,
                        null,
                        0);
                }

                var fileInfo = new FileInfo(backupPath);

                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    throw new Exception(
                        "ملف النسخة الاحتياطية غير صالح أو فارغ.");
                }

                using (var verificationConnection =
                    new SQLiteConnection(
                        "Data Source=" + backupPath + ";Version=3;"))
                {
                    verificationConnection.Open();

                    using (var command = new SQLiteCommand(
                        "PRAGMA integrity_check;",
                        verificationConnection))
                    {
                        string integrityResult =
                            Convert.ToString(command.ExecuteScalar());

                        if (!string.Equals(
                            integrityResult,
                            "ok",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            throw new Exception(
                                "فشل التحقق من سلامة النسخة الاحتياطية: " +
                                integrityResult);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                catch
                {
                    // الحفاظ على الخطأ الأصلي.
                }

                throw new InvalidOperationException(
                    "تعذر إنشاء النسخة الاحتياطية:\n" +
                    backupName +
                    "\n\n" +
                    ex.Message,
                    ex);
            }
        }

        private void CreateDailyBackup(
            SQLiteConnection sourceConnection)
        {
            string backupName =
                "OCORE_Daily_" +
                DateTime.Now.ToString("yyyyMMdd") +
                ".db";

            string backupPath = Path.Combine(
                backupFolder,
                backupName);

            if (File.Exists(backupPath))
            {
                var existingFile = new FileInfo(backupPath);

                if (existingFile.Length > 0)
                {
                    return;
                }

                File.Delete(backupPath);
            }

            CreateDatabaseBackup(sourceConnection, backupName);

            CleanOldDailyBackups();
        }

        private void CleanOldDailyBackups()
        {
            CleanOldBackups("OCORE_Daily_*.db");
        }

        private void CleanOldUpdateBackups()
        {
            CleanOldBackups("OCORE_Backup_Before_Update_*.db");
        }

        private void CleanOldBackups(string searchPattern)
        {
            if (!Directory.Exists(backupFolder))
                return;

            var backupFiles = Directory.GetFiles(
                backupFolder,
                searchPattern);

            var orderedFiles = backupFiles
                .OrderByDescending(
                    file => File.GetLastWriteTimeUtc(file))
                .ToList();

            for (int i = MaximumBackupCount;
                 i < orderedFiles.Count;
                 i++)
            {
                try
                {
                    File.Delete(orderedFiles[i]);
                }
                catch
                {
                    // تعذر حذف نسخة قديمة لا يؤثر على النسخ الأخرى.
                }
            }
        }

        public string GetDatabasePath()
        {
            // إرجاع مسار قاعدة البيانات دون عرض أي نافذة.
            return databasePath;
        }

        public SQLiteConnection GetConnection()
        {
            return new SQLiteConnection(
                "Data Source=" + databasePath + ";Version=3;");
        }

        public string ClearTestData()
        {
            string backupPath = null;

            try
            {
                if (!File.Exists(databasePath))
                    return "قاعدة البيانات غير موجودة.";

                string folder = Path.GetDirectoryName(databasePath);

                string backupName =
                    "OCORE_Backup_Before_Clear_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".db";

                backupPath = Path.Combine(folder, backupName);

                File.Copy(databasePath, backupPath, false);

                using (var connection = new SQLiteConnection(
                    "Data Source=" + databasePath + ";Version=3;"))
                {
                    connection.Open();

                    using (var pragma = new SQLiteCommand(
                        "PRAGMA foreign_keys = ON;", connection))
                    {
                        pragma.ExecuteNonQuery();
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            string sql = @"

DELETE FROM SalesReturnBatchAllocations;
DELETE FROM SalesReturnItems;
DELETE FROM SalesReturns;

DELETE FROM SalesInvoiceBatchAllocations;
DELETE FROM SalesInvoicePayments;
DELETE FROM CustomerAccountTransactions;
DELETE FROM SalesInvoiceItems;
DELETE FROM SalesInvoices;

DELETE FROM PurchaseInvoiceItems;
DELETE FROM PurchaseInvoices;

DELETE FROM ProductBarcodes;
DELETE FROM Batches;
DELETE FROM Products;
DELETE FROM Categories;
DELETE FROM Suppliers;

DELETE FROM Customers;

DELETE FROM sqlite_sequence
WHERE name IN (
    'SalesReturnBatchAllocations',
    'SalesReturnItems',
    'SalesReturns',
    'SalesInvoiceBatchAllocations',
    'SalesInvoicePayments',
    'CustomerAccountTransactions',
    'SalesInvoiceItems',
    'SalesInvoices',
    'PurchaseInvoiceItems',
    'PurchaseInvoices',
    'ProductBarcodes',
    'Batches',
    'Products',
    'Categories',
    'Suppliers',
    'Customers'
);

";

                            using (var command = new SQLiteCommand(
                                sql, connection, transaction))
                            {
                                command.ExecuteNonQuery();
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

                return
                    "تم تنظيف البيانات التجريبية بنجاح.\n\n" +
                    "تم الاحتفاظ بالمستخدمين والصلاحيات والإعدادات وسجل العمليات.\n\n" +
                    "النسخة الاحتياطية محفوظة في:\n" +
                    backupPath;
            }
            catch (Exception ex)
            {
                return
                    "تعذر تنظيف قاعدة البيانات:\n" +
                    ex.Message +
                    (backupPath != null
                        ? "\n\nالنسخة الاحتياطية:\n" + backupPath
                        : "");
            }
        }

        public string TestDatabase()
        {
            try
            {
                InitializeDatabase();

                using (var connection = new SQLiteConnection(
                    "Data Source=" + databasePath + ";Version=3;"))
                {
                    connection.Open();

                    DatabaseSchema.CreateCoreTables(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var command = new SQLiteCommand(
                            @"INSERT INTO SystemTest
                              (TestName, CreatedAt)
                              VALUES (@name, @date);",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@name", "OCORE TEST");

                            command.Parameters.AddWithValue(
                                "@date",
                                DateTime.Now.ToString(
                                    "yyyy-MM-dd HH:mm:ss"));

                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }

                    using (var command = new SQLiteCommand(
                        "SELECT TestName FROM SystemTest ORDER BY Id DESC LIMIT 1;",
                        connection))
                    {
                        return "نجح اختبار قاعدة البيانات: " +
                               command.ExecuteScalar().ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                return "فشل الاختبار: " + ex.Message;
            }
        }

        public string VerifyTables()
        {
            try
            {
                InitializeDatabase();

                using (var connection = new SQLiteConnection(
                    "Data Source=" + databasePath + ";Version=3;"))
                {
                    connection.Open();

                    string sql = @"
SELECT name
FROM sqlite_master
WHERE type = 'table'
AND name IN (
    'Users',
    'Categories',
    'Suppliers',
    'Products',
    'ProductBarcodes',
    'Batches',
    'Customers',
    'SalesInvoices',
    'SalesInvoicePayments',
    'CustomerAccountTransactions',
    'SalesInvoiceItems',
    'SalesInvoiceBatchAllocations',
    'PrinterConfigurations',
    'SystemTest'
)
ORDER BY name;";

                    using (var command = new SQLiteCommand(
                        sql, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        string result = "";

                        while (reader.Read())
                        {
                            result += reader["name"].ToString() + "\n";
                        }

                        return "الجداول الموجودة:\n" + result;
                    }
                }
            }
            catch (Exception ex)
            {
                return "خطأ: " + ex.Message;
            }
        }
    }
}
