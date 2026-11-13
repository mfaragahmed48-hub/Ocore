using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Ocore.desktop.Data
{
    public class DatabaseSchema
    {
        public static void CreateCoreTables(SQLiteConnection connection)
        {
            string sql = @"

CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL UNIQUE,
    PasswordHash TEXT NOT NULL,
    FullName TEXT NOT NULL,
    Role TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS
UX_Users_One_Manager
ON Users(Role)
WHERE Role = 'Manager';

CREATE TABLE IF NOT EXISTS Categories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Suppliers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Phone TEXT,
    Address TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Products (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductCode TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,
    CategoryId INTEGER,
    UnitName TEXT NOT NULL DEFAULT 'علبة',
    SmallUnitName TEXT NOT NULL DEFAULT 'وحدة',
    UnitsPerPackage INTEGER NOT NULL DEFAULT 1,
    SalePrice REAL NOT NULL DEFAULT 0,
    PurchasePrice REAL NOT NULL DEFAULT 0,
    MaxDiscountPercentage REAL NOT NULL DEFAULT 0,
    IsArchived INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (CategoryId) REFERENCES Categories(Id)
);

-- دليل الأدوية المستقل عن مخزون الصيدلية
CREATE TABLE IF NOT EXISTS DrugCatalog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RegistrationNumber TEXT NOT NULL DEFAULT '',
    TradeName TEXT NOT NULL,
    TradeNameArabic TEXT NOT NULL DEFAULT '',
    GenericName TEXT NOT NULL DEFAULT '',
    Strength TEXT NOT NULL DEFAULT '',
    DosageForm TEXT NOT NULL DEFAULT '',
    RouteOfAdministration TEXT NOT NULL DEFAULT '',
    Manufacturer TEXT NOT NULL DEFAULT '',
    MarketingAuthorizationHolder TEXT NOT NULL DEFAULT '',
    DrugClass TEXT NOT NULL DEFAULT '',
    PackageDescription TEXT NOT NULL DEFAULT '',
    ReferencePrice REAL NOT NULL DEFAULT 0,
    SourceStatus TEXT NOT NULL DEFAULT '',
    SourceKey TEXT NOT NULL DEFAULT '',
    IsActive INTEGER NOT NULL DEFAULT 1,
    DataSource TEXT NOT NULL DEFAULT 'EDA',
    LastVerifiedAt TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS IX_DrugCatalog_TradeName
ON DrugCatalog(TradeName);

CREATE INDEX IF NOT EXISTS IX_DrugCatalog_GenericName
ON DrugCatalog(GenericName);

CREATE INDEX IF NOT EXISTS IX_DrugCatalog_Manufacturer
ON DrugCatalog(Manufacturer);

CREATE INDEX IF NOT EXISTS IX_DrugCatalog_Strength
ON DrugCatalog(Strength);

CREATE INDEX IF NOT EXISTS IX_DrugCatalog_DosageForm
ON DrugCatalog(DosageForm);

CREATE UNIQUE INDEX IF NOT EXISTS UX_DrugCatalog_RegistrationNumber
ON DrugCatalog(RegistrationNumber)
WHERE RegistrationNumber <> '';

CREATE TABLE IF NOT EXISTS ProductBarcodes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    Barcode TEXT NOT NULL UNIQUE,
    FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

CREATE TABLE IF NOT EXISTS Batches (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductId INTEGER NOT NULL,
    SupplierId INTEGER,
    BatchNumber TEXT,
    ExpiryDate TEXT NOT NULL,
    PurchasePrice REAL NOT NULL DEFAULT 0,
    Quantity REAL NOT NULL DEFAULT 0,
    FOREIGN KEY (ProductId) REFERENCES Products(Id),
    FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id)
);

CREATE INDEX IF NOT EXISTS IX_Products_Name
ON Products(Name);

CREATE INDEX IF NOT EXISTS IX_Batches_ExpiryDate
ON Batches(ExpiryDate);

CREATE INDEX IF NOT EXISTS IX_ProductBarcodes_ProductId
ON ProductBarcodes(ProductId);

-- العملاء
CREATE TABLE IF NOT EXISTS Customers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerCode TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,
    Phone TEXT NOT NULL DEFAULT '',
    Address TEXT NOT NULL DEFAULT '',
    Notes TEXT NOT NULL DEFAULT '',
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    CreatedBy INTEGER,
    FOREIGN KEY (CreatedBy) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_Customers_Name
ON Customers(Name);

CREATE INDEX IF NOT EXISTS IX_Customers_Phone
ON Customers(Phone);

CREATE TABLE IF NOT EXISTS SalesInvoices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNumber TEXT NOT NULL UNIQUE,
    UserId INTEGER NOT NULL,
    CustomerId INTEGER NULL,

    Total REAL NOT NULL DEFAULT 0,
    Subtotal REAL NOT NULL DEFAULT 0,

    DiscountType TEXT NOT NULL DEFAULT 'None',
    DiscountValue REAL NOT NULL DEFAULT 0,
    DiscountAmount REAL NOT NULL DEFAULT 0,

    NetTotal REAL NOT NULL DEFAULT 0,
    PaidAmount REAL NOT NULL DEFAULT 0,
    RemainingAmount REAL NOT NULL DEFAULT 0,

    PaymentStatus TEXT NOT NULL DEFAULT 'Legacy',

    Status TEXT NOT NULL DEFAULT 'Completed',
    CreatedAt TEXT NOT NULL,

    FOREIGN KEY (UserId) REFERENCES Users(Id),
    FOREIGN KEY (CustomerId) REFERENCES Customers(Id)
);

CREATE INDEX IF NOT EXISTS IX_SalesInvoices_CreatedAt
ON SalesInvoices(CreatedAt);

CREATE INDEX IF NOT EXISTS IX_SalesInvoices_UserId
ON SalesInvoices(UserId);

-- دفعات فواتير البيع
CREATE TABLE IF NOT EXISTS SalesInvoicePayments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL,
    PaymentMethod TEXT NOT NULL,
    Amount REAL NOT NULL,
    ReferenceNumber TEXT NOT NULL DEFAULT '',
    Notes TEXT NOT NULL DEFAULT '',
    UserId INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,

    FOREIGN KEY (InvoiceId) REFERENCES SalesInvoices(Id),
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_SalesInvoicePayments_InvoiceId
ON SalesInvoicePayments(InvoiceId);

CREATE INDEX IF NOT EXISTS IX_SalesInvoicePayments_CreatedAt
ON SalesInvoicePayments(CreatedAt);

-- دفتر حساب العميل
-- Amount موجب = مديونية على العميل
-- Amount سالب = تحصيل أو تخفيض للمديونية
CREATE TABLE IF NOT EXISTS CustomerAccountTransactions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL,

    TransactionType TEXT NOT NULL,
    Amount REAL NOT NULL,

    ReferenceType TEXT NOT NULL DEFAULT '',
    ReferenceId INTEGER NULL,

    PaymentMethod TEXT NOT NULL DEFAULT '',
    ReferenceNumber TEXT NOT NULL DEFAULT '',

    Notes TEXT NOT NULL DEFAULT '',
    UserId INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,

    FOREIGN KEY (CustomerId) REFERENCES Customers(Id),
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_CustomerAccountTransactions_CreatedAt
ON CustomerAccountTransactions(CreatedAt);

CREATE INDEX IF NOT EXISTS IX_CustomerAccountTransactions_Reference
ON CustomerAccountTransactions(ReferenceType, ReferenceId);

CREATE TABLE IF NOT EXISTS PurchaseInvoices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceNumber TEXT NOT NULL UNIQUE,
    SupplierId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    Total REAL NOT NULL DEFAULT 0,
    Status TEXT NOT NULL DEFAULT 'Completed',
    CreatedAt TEXT NOT NULL,
    InvoiceDate TEXT NOT NULL DEFAULT '',
    FOREIGN KEY (SupplierId) REFERENCES Suppliers(Id),
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_PurchaseInvoices_CreatedAt
ON PurchaseInvoices(CreatedAt);

CREATE INDEX IF NOT EXISTS IX_PurchaseInvoices_SupplierId
ON PurchaseInvoices(SupplierId);

CREATE TABLE IF NOT EXISTS PurchaseInvoiceItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    BatchId INTEGER NOT NULL,
    ItemName TEXT NOT NULL,
    BatchNumber TEXT,
    ExpiryDate TEXT NOT NULL,
    Quantity REAL NOT NULL,
    UnitPrice REAL NOT NULL,
    Total REAL NOT NULL,
    FOREIGN KEY (InvoiceId) REFERENCES PurchaseInvoices(Id),
    FOREIGN KEY (ProductId) REFERENCES Products(Id),
    FOREIGN KEY (BatchId) REFERENCES Batches(Id)
);

CREATE INDEX IF NOT EXISTS IX_PurchaseInvoiceItems_InvoiceId
ON PurchaseInvoiceItems(InvoiceId);

CREATE INDEX IF NOT EXISTS IX_PurchaseInvoiceItems_ProductId
ON PurchaseInvoiceItems(ProductId);

CREATE TABLE IF NOT EXISTS SalesInvoiceItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ItemName TEXT NOT NULL,
    UnitName TEXT NOT NULL,
    Quantity REAL NOT NULL,
    UnitPrice REAL NOT NULL,
    Total REAL NOT NULL,
    PackageQuantity REAL NOT NULL,

    DiscountPercentage REAL NOT NULL DEFAULT 0,
    DiscountAmount REAL NOT NULL DEFAULT 0,

    FOREIGN KEY (InvoiceId) REFERENCES SalesInvoices(Id),
    FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

CREATE INDEX IF NOT EXISTS IX_SalesInvoiceItems_InvoiceId
ON SalesInvoiceItems(InvoiceId);

CREATE INDEX IF NOT EXISTS IX_SalesInvoiceItems_ProductId
ON SalesInvoiceItems(ProductId);

CREATE TABLE IF NOT EXISTS SalesInvoiceBatchAllocations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    InvoiceItemId INTEGER NOT NULL,
    BatchId INTEGER NOT NULL,
    Quantity REAL NOT NULL,
    FOREIGN KEY (InvoiceItemId) REFERENCES SalesInvoiceItems(Id),
    FOREIGN KEY (BatchId) REFERENCES Batches(Id)
);

CREATE INDEX IF NOT EXISTS IX_SalesInvoiceBatchAllocations_ItemId
ON SalesInvoiceBatchAllocations(InvoiceItemId);

CREATE INDEX IF NOT EXISTS IX_SalesInvoiceBatchAllocations_BatchId
ON SalesInvoiceBatchAllocations(BatchId);

CREATE TABLE IF NOT EXISTS SalesReturns (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnNumber TEXT NOT NULL UNIQUE,
    InvoiceId INTEGER NOT NULL,
    UserId INTEGER NOT NULL,
    Total REAL NOT NULL DEFAULT 0,
    Reason TEXT NOT NULL DEFAULT '',
    Status TEXT NOT NULL DEFAULT 'Completed',
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (InvoiceId) REFERENCES SalesInvoices(Id),
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_SalesReturns_InvoiceId
ON SalesReturns(InvoiceId);

CREATE INDEX IF NOT EXISTS IX_SalesReturns_CreatedAt
ON SalesReturns(CreatedAt);

CREATE TABLE IF NOT EXISTS SalesReturnItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnId INTEGER NOT NULL,
    InvoiceItemId INTEGER NOT NULL,
    ProductId INTEGER NOT NULL,
    ItemName TEXT NOT NULL,
    UnitName TEXT NOT NULL,
    Quantity REAL NOT NULL,
    UnitPrice REAL NOT NULL,
    Total REAL NOT NULL,
    PackageQuantity REAL NOT NULL,
    FOREIGN KEY (ReturnId) REFERENCES SalesReturns(Id),
    FOREIGN KEY (InvoiceItemId) REFERENCES SalesInvoiceItems(Id),
    FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

CREATE INDEX IF NOT EXISTS IX_SalesReturnItems_ReturnId
ON SalesReturnItems(ReturnId);

CREATE INDEX IF NOT EXISTS IX_SalesReturnItems_InvoiceItemId
ON SalesReturnItems(InvoiceItemId);

CREATE TABLE IF NOT EXISTS SalesReturnBatchAllocations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnItemId INTEGER NOT NULL,
    BatchId INTEGER NOT NULL,
    Quantity REAL NOT NULL,
    Restocked INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (ReturnItemId) REFERENCES SalesReturnItems(Id),
    FOREIGN KEY (BatchId) REFERENCES Batches(Id)
);

CREATE INDEX IF NOT EXISTS IX_SalesReturnBatchAllocations_ReturnItemId
ON SalesReturnBatchAllocations(ReturnItemId);

CREATE INDEX IF NOT EXISTS IX_SalesReturnBatchAllocations_BatchId
ON SalesReturnBatchAllocations(BatchId);

CREATE TABLE IF NOT EXISTS Roles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RoleCode TEXT NOT NULL UNIQUE,
    RoleName TEXT NOT NULL,
    IsSystemRole INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Permissions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PermissionCode TEXT NOT NULL UNIQUE,
    PermissionName TEXT NOT NULL,
    PermissionGroup TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS RolePermissions (
    RoleId INTEGER NOT NULL,
    PermissionId INTEGER NOT NULL,
    IsAllowed INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (RoleId, PermissionId),
    FOREIGN KEY (RoleId) REFERENCES Roles(Id),
    FOREIGN KEY (PermissionId) REFERENCES Permissions(Id)
);

CREATE TABLE IF NOT EXISTS AuditLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER,
    Username TEXT NOT NULL,
    ActionType TEXT NOT NULL,
    EntityType TEXT NOT NULL,
    EntityId INTEGER,
    Details TEXT,
    DeviceName TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS IX_AuditLogs_UserId
ON AuditLogs(UserId);

CREATE INDEX IF NOT EXISTS IX_AuditLogs_CreatedAt
ON AuditLogs(CreatedAt);

CREATE INDEX IF NOT EXISTS IX_AuditLogs_ActionType
ON AuditLogs(ActionType);

CREATE INDEX IF NOT EXISTS IX_RolePermissions_PermissionId
ON RolePermissions(PermissionId);

CREATE TABLE IF NOT EXISTS PharmacySettings (
    Id INTEGER PRIMARY KEY CHECK (Id = 1),
    PharmacyName TEXT NOT NULL DEFAULT '',
    Address TEXT NOT NULL DEFAULT '',
    Phone TEXT NOT NULL DEFAULT '',
    TaxNumber TEXT NOT NULL DEFAULT '',
    ManagerName TEXT NOT NULL DEFAULT '',
    LogoPath TEXT NOT NULL DEFAULT '',
    UpdatedAt TEXT NOT NULL DEFAULT ''
);

INSERT OR IGNORE INTO PharmacySettings (Id)
VALUES (1);

";

            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }

            // ترقيات قاعدة البيانات دون حذف البيانات القديمة
            EnsurePharmacyThermalPrinterColumns(connection);

            // إنشاء جداول إعدادات الطباعة القديمة مع الحفاظ عليها
            EnsurePrintingProfilesTables(connection);

            // إنشاء جدول إعدادات الطابعات الجديد
            // لا يتم إنشاء سجلات افتراضية أو ترحيل بيانات في هذه المرحلة
            EnsurePrinterConfigurationsTable(connection);

            EnsureProductPackageColumns(connection);
            EnsureProductMaxDiscountPercentageColumn(connection);

            // إضافة الحد الأدنى للمخزون لكل صنف.
            EnsureProductMinimumStockLevelColumn(connection);

            EnsureProductDrugCatalogIdColumn(connection);
            EnsurePurchaseInvoiceDateColumn(connection);
            EnsurePurchaseInvoiceItemUnitColumns(connection);
            EnsureSalesReturnAllocationRestockedColumn(connection);
            EnsureSalesReturnUnitColumns(connection);
            EnsureDrugCatalogImportColumns(connection);

            // إضافة أعمدة الحسابات قبل إنشاء الفهارس المرتبطة بها.
            EnsureSalesAccountingColumns(connection);

            // إضافة تفاصيل الخصم لكل بند في فاتورة البيع.
            EnsureSalesInvoiceItemDiscountColumns(connection);

            EnsureSalesInvoicesCustomerIdIndex(connection);

            // إنشاء فهرس حالة السداد بعد التأكد من وجود العمود.
            using (var command = new SQLiteCommand(@"
                CREATE INDEX IF NOT EXISTS IX_SalesInvoices_PaymentStatus
                ON SalesInvoices(PaymentStatus);
            ", connection))
            {
                command.ExecuteNonQuery();
            }

            // ترقية جدول حركات حساب العملاء وإنشاء فهرسه بعد التأكد من الأعمدة.
            EnsureCustomerAccountTransactionColumns(connection);

            InitializeDefaultRoles(connection);
        }

        // إضافة إعدادات الطابعة الحرارية مع الحفاظ على الإعدادات القديمة.
        private static void EnsurePharmacyThermalPrinterColumns(
            SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(PharmacySettings);",
                connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    existingColumns.Add(reader["name"].ToString());
                }
            }

            var columnsToAdd = new Dictionary<string, string>
            {
                { "ThermalPrinterName", "TEXT NOT NULL DEFAULT ''" },
                { "ThermalPrintMode", "TEXT NOT NULL DEFAULT 'WindowsDriver'" },
                { "ThermalPaperWidth", "INTEGER NOT NULL DEFAULT 80" },
                { "ThermalPrintWidthDots", "INTEGER NOT NULL DEFAULT 0" },
                { "ThermalAutoCut", "INTEGER NOT NULL DEFAULT 0" },
                { "ThermalOpenCashDrawer", "INTEGER NOT NULL DEFAULT 0" },
                { "ThermalFontScale", "REAL NOT NULL DEFAULT 1.0" }
            };

            foreach (var column in columnsToAdd)
            {
                if (ContainsColumn(existingColumns, column.Key))
                    continue;

                using (var command = new SQLiteCommand(
                    "ALTER TABLE PharmacySettings ADD COLUMN "
                    + column.Key + " " + column.Value + ";",
                    connection))
                {
                    command.ExecuteNonQuery();
                }

                existingColumns.Add(column.Key);
            }
        }

        // إنشاء جداول إعدادات الطباعة القديمة.
        // يتم الاحتفاظ بها لحين اكتمال الترحيل والتحقق منه.
        private static void EnsurePrintingProfilesTables(
            SQLiteConnection connection)
        {
            string sql = @"

CREATE TABLE IF NOT EXISTS PrintingProfiles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FunctionKey TEXT NOT NULL UNIQUE,

    PrinterName TEXT NOT NULL DEFAULT '',
    PrintMode TEXT NOT NULL DEFAULT 'WindowsDriver',
    PaperSize TEXT NOT NULL DEFAULT '',
    PaperWidth REAL NOT NULL DEFAULT 0,
    PaperHeight REAL NOT NULL DEFAULT 0,
    PrintWidthDots INTEGER NOT NULL DEFAULT 0,

    MarginLeft REAL NOT NULL DEFAULT 0,
    MarginRight REAL NOT NULL DEFAULT 0,
    MarginTop REAL NOT NULL DEFAULT 0,
    MarginBottom REAL NOT NULL DEFAULT 0,

    Orientation TEXT NOT NULL DEFAULT 'Portrait',
    FontScale REAL NOT NULL DEFAULT 1.0,
    AutoCut INTEGER NOT NULL DEFAULT 0,
    OpenCashDrawer INTEGER NOT NULL DEFAULT 0,

    BarcodePrinter TEXT NOT NULL DEFAULT '',
    BarcodePrintType TEXT NOT NULL DEFAULT 'A4',
    BarcodeShowPrice INTEGER NOT NULL DEFAULT 1,
    BarcodeShowProductCode INTEGER NOT NULL DEFAULT 1,

    BarcodeA4Printer TEXT NOT NULL DEFAULT '',
    BarcodeA4LabelWidth TEXT NOT NULL DEFAULT '50',
    BarcodeA4LabelHeight TEXT NOT NULL DEFAULT '30',
    BarcodeA4Margin TEXT NOT NULL DEFAULT '5',
    BarcodeA4Gap TEXT NOT NULL DEFAULT '2',

    BarcodeRollPrinter TEXT NOT NULL DEFAULT '',
    BarcodeRollLabelWidth TEXT NOT NULL DEFAULT '50',
    BarcodeRollLabelHeight TEXT NOT NULL DEFAULT '30',
    BarcodeRollGap TEXT NOT NULL DEFAULT '2',
    BarcodeRollOffsetX TEXT NOT NULL DEFAULT '0',
    BarcodeRollOffsetY TEXT NOT NULL DEFAULT '0',
    BarcodeRollOrientation TEXT NOT NULL DEFAULT 'Portrait'
);

CREATE TABLE IF NOT EXISTS PrintingMigrationState (
    StateKey TEXT NOT NULL PRIMARY KEY,
    StateValue TEXT NOT NULL DEFAULT '',
    UpdatedAt TEXT NOT NULL DEFAULT ''
);

";

            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        // إنشاء جدول إعدادات الطابعات الجديد.
        // كل نوع طابعة له سجل مستقل داخل قاعدة بيانات الجهاز.
        // لا يتم إنشاء سجلات افتراضية أو ترحيل بيانات في هذه المرحلة.
        private static void EnsurePrinterConfigurationsTable(
            SQLiteConnection connection)
        {
            string sql = @"

CREATE TABLE IF NOT EXISTS PrinterConfigurations
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    PrinterType TEXT NOT NULL UNIQUE
        CHECK (PrinterType IN ('Cashier', 'Normal', 'BarcodeRoll')),

    PrinterName TEXT NOT NULL DEFAULT '',

    PrintMode TEXT NOT NULL DEFAULT 'WindowsDriver',

    PaperSize TEXT NOT NULL DEFAULT 'A4',

    PaperWidth REAL NOT NULL DEFAULT 0,
    PaperHeight REAL NOT NULL DEFAULT 0,
    PrintWidthDots INTEGER NOT NULL DEFAULT 0,

    MarginLeft REAL NOT NULL DEFAULT 0,
    MarginRight REAL NOT NULL DEFAULT 0,
    MarginTop REAL NOT NULL DEFAULT 0,
    MarginBottom REAL NOT NULL DEFAULT 0,

    Orientation TEXT NOT NULL DEFAULT 'Portrait',
    FontScale REAL NOT NULL DEFAULT 1.0,

    AutoCut INTEGER NOT NULL DEFAULT 0,
    OpenCashDrawer INTEGER NOT NULL DEFAULT 0,

    BarcodeLabelWidth REAL NOT NULL DEFAULT 35,
    BarcodeLabelHeight REAL NOT NULL DEFAULT 12,
    BarcodeGap REAL NOT NULL DEFAULT 3,
    BarcodeOffsetX REAL NOT NULL DEFAULT 2,
    BarcodeOffsetY REAL NOT NULL DEFAULT 2,

    BarcodeShowPrice INTEGER NOT NULL DEFAULT 1,
    BarcodeShowProductCode INTEGER NOT NULL DEFAULT 1,

    UpdatedAt TEXT NOT NULL DEFAULT ''
);

";

            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        // فحص أسماء الأعمدة دون حساسية لحالة الأحرف.
        private static bool ContainsColumn(
            List<string> columns,
            string columnName)
        {
            foreach (string column in columns)
            {
                if (string.Equals(
                    column,
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        private static void EnsureSalesInvoicesCustomerIdIndex(
    SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(SalesInvoices);", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    existingColumns.Add(
                        reader["name"].ToString());
                }
            }

            if (!ContainsColumn(existingColumns, "CustomerId"))
            {
                throw new InvalidOperationException(
                    "تعذر إنشاء فهرس العملاء لأن العمود CustomerId غير موجود في جدول SalesInvoices.");
            }

            using (var command = new SQLiteCommand(@"
                CREATE INDEX IF NOT EXISTS IX_SalesInvoices_CustomerId
                ON SalesInvoices(CustomerId);
            ", connection))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureCustomerAccountTransactionColumns(
            SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(CustomerAccountTransactions);", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    existingColumns.Add(reader["name"].ToString());
                }
            }

            var columnsToAdd = new Dictionary<string, string>
            {
                { "CustomerId", "INTEGER NULL" },
                { "PaymentMethod", "TEXT NOT NULL DEFAULT ''" },
                { "ReferenceNumber", "TEXT NOT NULL DEFAULT ''" }
            };

            foreach (var column in columnsToAdd)
            {
                if (ContainsColumn(existingColumns, column.Key))
                    continue;

                using (var command = new SQLiteCommand(
                    "ALTER TABLE CustomerAccountTransactions ADD COLUMN "
                    + column.Key + " " + column.Value + ";",
                    connection))
                {
                    command.ExecuteNonQuery();
                }

                existingColumns.Add(column.Key);
            }

            using (var command = new SQLiteCommand(@"
                CREATE INDEX IF NOT EXISTS
                IX_CustomerAccountTransactions_CustomerId
                ON CustomerAccountTransactions(CustomerId);
            ", connection))
            {
                command.ExecuteNonQuery();
            }
        }

        // إضافة أعمدة خصم كل بند لفواتير البيع القديمة والجديدة.
        // لا يتم تغيير إجمالي الفواتير القديمة أو أي بيانات مالية محفوظة.
        private static void EnsureSalesInvoiceItemDiscountColumns(
            SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(SalesInvoiceItems);", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    existingColumns.Add(reader["name"].ToString());
                }
            }

            var columnsToAdd = new Dictionary<string, string>
            {
                {
                    "DiscountPercentage",
                    "REAL NOT NULL DEFAULT 0"
                },
                {
                    "DiscountAmount",
                    "REAL NOT NULL DEFAULT 0"
                }
            };

            foreach (var column in columnsToAdd)
            {
                if (ContainsColumn(existingColumns, column.Key))
                    continue;

                using (var command = new SQLiteCommand(
                    "ALTER TABLE SalesInvoiceItems ADD COLUMN "
                    + column.Key + " " + column.Value + ";",
                    connection))
                {
                    command.ExecuteNonQuery();
                }

                existingColumns.Add(column.Key);
            }
        }

        private static void EnsureSalesAccountingColumns(
            SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(SalesInvoices);", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    existingColumns.Add(reader["name"].ToString());
            }

            var columnsToAdd = new Dictionary<string, string>
            {
                { "CustomerId", "INTEGER NULL" },
                { "Subtotal", "REAL NOT NULL DEFAULT 0" },
                { "DiscountType", "TEXT NOT NULL DEFAULT 'None'" },
                { "DiscountValue", "REAL NOT NULL DEFAULT 0" },
                { "DiscountAmount", "REAL NOT NULL DEFAULT 0" },
                { "NetTotal", "REAL NOT NULL DEFAULT 0" },
                { "PaidAmount", "REAL NOT NULL DEFAULT 0" },
                { "RemainingAmount", "REAL NOT NULL DEFAULT 0" },
                { "PaymentStatus", "TEXT NOT NULL DEFAULT 'Legacy'" }
            };

            foreach (var column in columnsToAdd)
            {
                if (ContainsColumn(existingColumns, column.Key))
                    continue;

                string columnSql =
                    "ALTER TABLE SalesInvoices ADD COLUMN "
                    + column.Key + " " + column.Value + ";";

                using (var command = new SQLiteCommand(
                    columnSql, connection))
                {
                    command.ExecuteNonQuery();
                }

                existingColumns.Add(column.Key);
            }

            // الفواتير القديمة: نحافظ على Total كما هو
            // ولا نفترض أنها مدفوعة أو آجلة.
            using (var command = new SQLiteCommand(@"
                UPDATE SalesInvoices
                SET
                    Subtotal = Total,
                    NetTotal = Total,
                    PaymentStatus = 'Legacy'
                WHERE PaymentStatus = 'Legacy'
                  AND Subtotal = 0
                  AND NetTotal = 0;
            ", connection))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureProductPackageColumns(
            SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(Products);", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    existingColumns.Add(reader["name"].ToString());
            }

            if (!ContainsColumn(existingColumns, "SmallUnitName"))
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE Products
                      ADD COLUMN SmallUnitName TEXT NOT NULL DEFAULT 'وحدة';",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            if (!ContainsColumn(existingColumns, "UnitsPerPackage"))
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE Products
                      ADD COLUMN UnitsPerPackage INTEGER NOT NULL DEFAULT 1;",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        // إضافة الحد الأقصى للخصم المسموح به لكل صنف.
        // القيمة الافتراضية صفر تعني أن الصنف لا يقبل الخصم.
        // يتم الحفاظ على جميع الأصناف والبيانات القديمة.
        private static void EnsureProductMaxDiscountPercentageColumn(
            SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(Products);", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    existingColumns.Add(reader["name"].ToString());
                }
            }

            if (!ContainsColumn(
                existingColumns,
                "MaxDiscountPercentage"))
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE Products
                      ADD COLUMN MaxDiscountPercentage REAL NOT NULL DEFAULT 0;",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        // إضافة الحد الأدنى للمخزون لكل صنف.
        // القيمة الافتراضية صفر تعني عدم تفعيل تنبيه انخفاض المخزون.
        // يتم الحفاظ على جميع الأصناف والبيانات القديمة.
        private static void EnsureProductMinimumStockLevelColumn(
            SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(Products);", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    existingColumns.Add(reader["name"].ToString());
                }
            }

            if (!ContainsColumn(
                existingColumns,
                "MinimumStockLevel"))
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE Products
                      ADD COLUMN MinimumStockLevel REAL NOT NULL DEFAULT 0;",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        // ربط الأصناف بدليل الأدوية دون التأثير على الأصناف القديمة
        private static void EnsureProductDrugCatalogIdColumn(
            SQLiteConnection connection)
        {
            bool columnExists = false;

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(Products);", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(
                        reader["name"].ToString(),
                        "DrugCatalogId",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        columnExists = true;
                        break;
                    }
                }
            }

            if (!columnExists)
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE Products
                      ADD COLUMN DrugCatalogId INTEGER NULL;",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            using (var command = new SQLiteCommand(@"
                CREATE UNIQUE INDEX IF NOT EXISTS
                UX_Products_DrugCatalogId
                ON Products(DrugCatalogId)
                WHERE DrugCatalogId IS NOT NULL;
            ", connection))
            {
                command.ExecuteNonQuery();
            }
        }
        private static void EnsurePurchaseInvoiceDateColumn(
    SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(PurchaseInvoices);", connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    existingColumns.Add(reader["name"].ToString());
            }

            if (!ContainsColumn(existingColumns, "InvoiceDate"))
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE PurchaseInvoices
                      ADD COLUMN InvoiceDate TEXT NOT NULL DEFAULT '';",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        // إضافة بيانات وحدات الشراء والخصومات والضرائب والبونص
        // مع الحفاظ على الفواتير القديمة
        private static void EnsurePurchaseInvoiceItemUnitColumns(
            SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(PurchaseInvoiceItems);",
                connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    existingColumns.Add(reader["name"].ToString());
            }

            var columnsToAdd = new Dictionary<string, string>
            {
                { "PurchaseUnitName", "TEXT NOT NULL DEFAULT ''" },
                { "PackageUnitName", "TEXT NOT NULL DEFAULT ''" },
                { "SmallUnitName", "TEXT NOT NULL DEFAULT ''" },
                { "UnitsPerPackage", "INTEGER NOT NULL DEFAULT 1" },
                { "PackageQuantity", "REAL NOT NULL DEFAULT 0" },
                { "PackageUnitPrice", "REAL NOT NULL DEFAULT 0" },

                { "ProductCode", "TEXT NOT NULL DEFAULT ''" },
                { "Manufacturer", "TEXT NOT NULL DEFAULT ''" },

                { "FreeQuantity", "REAL NOT NULL DEFAULT 0" },
                { "FreePackageQuantity", "REAL NOT NULL DEFAULT 0" },

                { "DiscountPercent", "REAL NOT NULL DEFAULT 0" },
                { "DiscountAmount", "REAL NOT NULL DEFAULT 0" },

                { "TaxPercent", "REAL NOT NULL DEFAULT 0" },
                { "TaxAmount", "REAL NOT NULL DEFAULT 0" },

                { "Notes", "TEXT NOT NULL DEFAULT ''" }
            };

            foreach (var column in columnsToAdd)
            {
                if (ContainsColumn(existingColumns, column.Key))
                    continue;

                string columnSql =
                    "ALTER TABLE PurchaseInvoiceItems ADD COLUMN "
                    + column.Key + " " + column.Value + ";";

                using (var command = new SQLiteCommand(
                    columnSql, connection))
                {
                    command.ExecuteNonQuery();
                }

                existingColumns.Add(column.Key);
            }

            // ترحيل بيانات الفواتير القديمة دون تغيير قيمها المالية
            using (var command = new SQLiteCommand(@"
                UPDATE PurchaseInvoiceItems
                SET
                    PurchaseUnitName = COALESCE(
                        NULLIF(PurchaseUnitName, ''),
                        (SELECT UnitName
                         FROM Products
                         WHERE Products.Id = PurchaseInvoiceItems.ProductId),
                        'علبة'
                    ),

                    PackageUnitName = COALESCE(
                        NULLIF(PackageUnitName, ''),
                        (SELECT UnitName
                         FROM Products
                         WHERE Products.Id = PurchaseInvoiceItems.ProductId),
                        'علبة'
                    ),

                    SmallUnitName = COALESCE(
                        NULLIF(SmallUnitName, ''),
                        (SELECT SmallUnitName
                         FROM Products
                         WHERE Products.Id = PurchaseInvoiceItems.ProductId),
                        'وحدة'
                    ),

                    UnitsPerPackage = CASE
                        WHEN UnitsPerPackage <= 0 THEN COALESCE(
                            (SELECT UnitsPerPackage
                             FROM Products
                             WHERE Products.Id = PurchaseInvoiceItems.ProductId),
                            1
                        )
                        ELSE UnitsPerPackage
                    END,

                    PackageQuantity = CASE
                        WHEN PackageQuantity = 0 THEN Quantity
                        ELSE PackageQuantity
                    END,

                    PackageUnitPrice = CASE
                        WHEN PackageUnitPrice = 0 THEN UnitPrice
                        ELSE PackageUnitPrice
                    END

                WHERE PurchaseUnitName = ''
                   OR PackageUnitName = ''
                   OR SmallUnitName = ''
                   OR UnitsPerPackage <= 0
                   OR PackageQuantity = 0
                   OR PackageUnitPrice = 0;
            ", connection))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureSalesReturnAllocationRestockedColumn(
            SQLiteConnection connection)
        {
            bool columnExists = false;

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(SalesReturnBatchAllocations);",
                connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (string.Equals(
                        reader["name"].ToString(),
                        "Restocked",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        columnExists = true;
                        break;
                    }
                }
            }

            if (!columnExists)
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE SalesReturnBatchAllocations
                      ADD COLUMN Restocked INTEGER NOT NULL DEFAULT 1;",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        // إضافة بيانات وحدة المرتجع مع الحفاظ على السجلات القديمة
        private static void EnsureSalesReturnUnitColumns(
            SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(SalesReturnItems);",
                connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    existingColumns.Add(reader["name"].ToString());
            }

            if (!ContainsColumn(existingColumns, "ReturnUnitName"))
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE SalesReturnItems
                      ADD COLUMN ReturnUnitName TEXT NOT NULL DEFAULT '';",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            if (!ContainsColumn(existingColumns, "ReturnUnitQuantity"))
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE SalesReturnItems
                      ADD COLUMN ReturnUnitQuantity REAL NOT NULL DEFAULT 0;",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            if (!ContainsColumn(existingColumns, "ReturnUnitPrice"))
            {
                using (var command = new SQLiteCommand(
                    @"ALTER TABLE SalesReturnItems
                      ADD COLUMN ReturnUnitPrice REAL NOT NULL DEFAULT 0;",
                    connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            using (var command = new SQLiteCommand(@"
                UPDATE SalesReturnItems
                SET ReturnUnitName = UnitName,
                    ReturnUnitQuantity = Quantity,
                    ReturnUnitPrice = UnitPrice
                WHERE ReturnUnitName = ''
                   OR ReturnUnitQuantity = 0;",
                connection))
            {
                command.ExecuteNonQuery();
            }
        }

        // إضافة أعمدة استيراد DrugCatalog دون حذف البيانات القديمة
        private static void EnsureDrugCatalogImportColumns(
    SQLiteConnection connection)
        {
            var existingColumns = new List<string>();

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(DrugCatalog);",
                connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                    existingColumns.Add(reader["name"].ToString());
            }

            var columnsToAdd = new Dictionary<string, string>
            {
                { "TradeNameArabic", "TEXT NOT NULL DEFAULT ''" },
                { "DrugClass", "TEXT NOT NULL DEFAULT ''" },
                { "ReferencePrice", "REAL NOT NULL DEFAULT 0" },
                { "SourceStatus", "TEXT NOT NULL DEFAULT ''" },
                { "SourceKey", "TEXT NOT NULL DEFAULT ''" }
            };

            foreach (var column in columnsToAdd)
            {
                if (ContainsColumn(existingColumns, column.Key))
                    continue;

                string columnSql =
                    "ALTER TABLE DrugCatalog ADD COLUMN "
                    + column.Key + " " + column.Value + ";";

                using (var command = new SQLiteCommand(
                    columnSql, connection))
                {
                    command.ExecuteNonQuery();
                }

                existingColumns.Add(column.Key);
            }

            using (var command = new SQLiteCommand(@"
                CREATE UNIQUE INDEX IF NOT EXISTS
                UX_DrugCatalog_SourceKey
                ON DrugCatalog(SourceKey)
                WHERE SourceKey <> '';
            ", connection))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void InitializeDefaultRoles(
            SQLiteConnection connection)
        {
            string sql = @"

INSERT OR IGNORE INTO Roles (RoleCode, RoleName)
VALUES
('Manager', 'مدير النظام'),
('Pharmacist', 'صيدلي'),
('Cashier', 'كاشير');

INSERT OR IGNORE INTO Permissions
(PermissionCode, PermissionName, PermissionGroup)
VALUES
('Sales.View', 'عرض المبيعات', 'المبيعات'),
('Sales.Create', 'إنشاء فاتورة بيع', 'المبيعات'),
('Sales.SearchProducts', 'البحث عن الأصناف للبيع', 'المبيعات'),
('Sales.Cancel', 'إلغاء فاتورة بيع', 'المبيعات'),
('Sales.Return', 'تنفيذ مرتجع بيع', 'المبيعات'),

('Sales.Discount', 'منح خصم على فاتورة بيع', 'المبيعات'),
('Sales.Credit', 'السماح بالبيع الآجل', 'المبيعات'),

('Customers.View', 'عرض العملاء', 'العملاء'),
('Customers.Create', 'إضافة عميل', 'العملاء'),
('Customers.Edit', 'تعديل بيانات عميل', 'العملاء'),
('Customers.CollectPayment', 'تحصيل مديونية عميل', 'العملاء'),

('Products.View', 'عرض الأصناف', 'الأصناف'),
('Products.Create', 'إضافة صنف', 'الأصناف'),
('Products.Edit', 'تعديل صنف', 'الأصناف'),
('Products.SetDiscountLimit', 'تحديد الحد الأقصى لخصم الصنف', 'الأصناف'),
('Products.Delete', 'حذف صنف', 'الأصناف'),

('Inventory.View', 'عرض المخزون', 'المخزون'),
('Inventory.Edit', 'تعديل المخزون', 'المخزون'),

('Purchases.View', 'عرض المشتريات', 'المشتريات'),
('Purchases.Create', 'إنشاء فاتورة شراء', 'المشتريات'),

('Users.View', 'عرض المستخدمين', 'المستخدمون'),
('Users.Create', 'إضافة مستخدم', 'المستخدمون'),
('Users.Edit', 'تعديل المستخدمين', 'المستخدمون'),

('Reports.View', 'عرض التقارير', 'التقارير'),
('Settings.Edit', 'تعديل الإعدادات', 'النظام'),
('Backup.Create', 'إنشاء نسخة احتياطية', 'النسخ الاحتياطي'),
('Backup.Restore', 'استعادة نسخة احتياطية', 'النسخ الاحتياطي'),
('Audit.View', 'عرض سجل العمليات', 'النظام');

INSERT OR IGNORE INTO RolePermissions
(RoleId, PermissionId, IsAllowed)
SELECT
    (SELECT Id FROM Roles WHERE RoleCode = 'Manager'),
    Id,
    1
FROM Permissions;

INSERT OR IGNORE INTO RolePermissions
(RoleId, PermissionId, IsAllowed)
SELECT
    (SELECT Id FROM Roles WHERE RoleCode = 'Pharmacist'),
    Id,
    1
FROM Permissions
WHERE PermissionCode IN (
    'Sales.View',
    'Sales.Create',
    'Sales.SearchProducts',
    'Sales.Discount',
    'Products.View',
    'Inventory.View',
    'Purchases.View',
    'Reports.View'
);

INSERT OR IGNORE INTO RolePermissions
(RoleId, PermissionId, IsAllowed)
SELECT
    (SELECT Id FROM Roles WHERE RoleCode = 'Cashier'),
    Id,
    1
FROM Permissions
WHERE PermissionCode IN (
    'Sales.View',
    'Sales.Create',
    'Sales.SearchProducts',
    'Products.View'
);

";

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    using (var command = new SQLiteCommand(
                        sql, connection))
                    {
                        command.Transaction = transaction;
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
    }
}