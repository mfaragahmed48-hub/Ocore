
using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Ocore.desktop.Data
{
    public static class TaxDatabaseSchema
    {
        public static void Ensure(SQLiteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    CreateTaxTables(connection, transaction);

                    CreateInvoiceTaxComponentTable(
                        connection,
                        transaction);

                    CreateReturnTaxComponentTable(
                        connection,
                        transaction);

                    EnsureColumns(
                        connection,
                        transaction,
                        "Products",
                        new Dictionary<string, string>
                        {
                            { "TaxProfileId", "INTEGER NULL" },
                            {
                                "TaxPriceMode",
                                "TEXT NOT NULL DEFAULT 'Exclusive'"
                            }
                        });

                    EnsureColumns(
                        connection,
                        transaction,
                        "PharmacySettings",
                        new Dictionary<string, string>
                        {
                            {
                                "VatRegistrationStatus",
                                "TEXT NOT NULL DEFAULT 'Unknown'"
                            },
                            {
                                "TaxInvoiceEnabled",
                                "INTEGER NOT NULL DEFAULT 0"
                            }
                        });

                    EnsureColumns(
                        connection,
                        transaction,
                        "SalesInvoices",
                        new Dictionary<string, string>
                        {
                            { "TaxableAmount", "REAL NOT NULL DEFAULT 0" },
                            { "VatAmount", "REAL NOT NULL DEFAULT 0" },
                            {
                                "ScheduleTaxAmount",
                                "REAL NOT NULL DEFAULT 0"
                            },
                            { "TaxTotal", "REAL NOT NULL DEFAULT 0" },
                            {
                                "TaxStatus",
                                "TEXT NOT NULL DEFAULT 'Legacy'"
                            }
                        });

                    EnsureColumns(
                        connection,
                        transaction,
                        "SalesInvoiceItems",
                        new Dictionary<string, string>
                        {
                            { "TaxProfileId", "INTEGER NULL" },
                            { "TaxProfileCode", "TEXT NOT NULL DEFAULT ''" },
                            { "TaxProfileName", "TEXT NOT NULL DEFAULT ''" },
                            { "TaxType", "TEXT NOT NULL DEFAULT 'Unclassified'" },
                            { "TaxRate", "REAL NOT NULL DEFAULT 0" },
                            {
                                "TaxPriceMode",
                                "TEXT NOT NULL DEFAULT 'Exclusive'"
                            },
                            { "TaxableAmount", "REAL NOT NULL DEFAULT 0" },
                            { "VatAmount", "REAL NOT NULL DEFAULT 0" },
                            {
                                "ScheduleTaxAmount",
                                "REAL NOT NULL DEFAULT 0"
                            },
                            {
                                "TaxStatus",
                                "TEXT NOT NULL DEFAULT 'Legacy'"
                            }
                        });

                    EnsureColumns(
                        connection,
                        transaction,
                        "PurchaseInvoiceItems",
                        new Dictionary<string, string>
                        {
                            {
                                "InputTaxTreatment",
                                "TEXT NOT NULL DEFAULT 'Unknown'"
                            },
                            {
                                "InputTaxBaseAmount",
                                "REAL NOT NULL DEFAULT 0"
                            },
                            {
                                "RecoverableInputTaxAmount",
                                "REAL NOT NULL DEFAULT 0"
                            },
                            {
                                "NonRecoverableInputTaxAmount",
                                "REAL NOT NULL DEFAULT 0"
                            },
                            {
                                "TaxStatus",
                                "TEXT NOT NULL DEFAULT 'Legacy'"
                            }
                        });

                    EnsureColumns(
                        connection,
                        transaction,
                        "SalesReturns",
                        new Dictionary<string, string>
                        {
                            { "DiscountAmount", "REAL NOT NULL DEFAULT 0" },
                            { "TaxableAmount", "REAL NOT NULL DEFAULT 0" },
                            { "VatAmount", "REAL NOT NULL DEFAULT 0" },
                            {
                                "ScheduleTaxAmount",
                                "REAL NOT NULL DEFAULT 0"
                            },
                            { "TaxTotal", "REAL NOT NULL DEFAULT 0" },
                            {
                                "TaxStatus",
                                "TEXT NOT NULL DEFAULT 'Legacy'"
                            }
                        });

                    EnsureColumns(
                        connection,
                        transaction,
                        "SalesReturnItems",
                        new Dictionary<string, string>
                        {
                            { "DiscountAmount", "REAL NOT NULL DEFAULT 0" },
                            { "TaxableAmount", "REAL NOT NULL DEFAULT 0" },
                            { "VatAmount", "REAL NOT NULL DEFAULT 0" },
                            {
                                "ScheduleTaxAmount",
                                "REAL NOT NULL DEFAULT 0"
                            },
                            {
                                "TaxStatus",
                                "TEXT NOT NULL DEFAULT 'Legacy'"
                            }
                        });

                    // لا يتم إنشاء تصنيف ضريبي افتراضي تلقائيًا.
                    // لا يتم تعديل تصنيفات الأصناف القديمة.
                    // يتم الاحتفاظ بالجداول والأعمدة والفهارس
                    // لضمان قراءة السجلات والفواتير التاريخية.

                    CreateTaxIndexes(connection, transaction);

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static void CreateTaxTables(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            string sql = @"

CREATE TABLE IF NOT EXISTS TaxProfiles
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    Code TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,

    TaxType TEXT NOT NULL DEFAULT 'Unclassified',

    Description TEXT NOT NULL DEFAULT '',

    IsActive INTEGER NOT NULL DEFAULT 1,
    IsSystem INTEGER NOT NULL DEFAULT 0,

    CreatedBy INTEGER NULL,
    CreatedAt TEXT NOT NULL DEFAULT '',
    UpdatedAt TEXT NOT NULL DEFAULT '',

    FOREIGN KEY (CreatedBy) REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS TaxProfileRates
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    TaxProfileId INTEGER NOT NULL,

    ComponentCode TEXT NOT NULL DEFAULT '',
    ComponentName TEXT NOT NULL DEFAULT '',

    ComponentType TEXT NOT NULL DEFAULT 'VAT',

    RatePercent REAL NOT NULL DEFAULT 0,

    CalculationOrder INTEGER NOT NULL DEFAULT 1,

    IsCompound INTEGER NOT NULL DEFAULT 0,

    EffectiveFrom TEXT NOT NULL,
    EffectiveTo TEXT NOT NULL DEFAULT '',

    IsActive INTEGER NOT NULL DEFAULT 1,

    CreatedBy INTEGER NULL,
    CreatedAt TEXT NOT NULL DEFAULT '',

    FOREIGN KEY (TaxProfileId)
        REFERENCES TaxProfiles(Id),

    FOREIGN KEY (CreatedBy)
        REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS TaxAuditLogs
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    UserId INTEGER NULL,

    ActionType TEXT NOT NULL,

    EntityType TEXT NOT NULL,
    EntityId INTEGER NULL,

    OldValues TEXT NOT NULL DEFAULT '',
    NewValues TEXT NOT NULL DEFAULT '',

    Reason TEXT NOT NULL DEFAULT '',

    DeviceName TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,

    FOREIGN KEY (UserId) REFERENCES Users(Id)
);

";

            using (var command = new SQLiteCommand(
                sql,
                connection,
                transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void CreateInvoiceTaxComponentTable(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            string sql = @"

CREATE TABLE IF NOT EXISTS SalesInvoiceItemTaxComponents
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    InvoiceItemId INTEGER NOT NULL,

    TaxProfileRateId INTEGER NULL,

    ComponentCode TEXT NOT NULL DEFAULT '',
    ComponentName TEXT NOT NULL DEFAULT '',
    ComponentType TEXT NOT NULL DEFAULT 'VAT',

    RatePercent REAL NOT NULL DEFAULT 0,

    CalculationOrder INTEGER NOT NULL DEFAULT 1,
    IsCompound INTEGER NOT NULL DEFAULT 0,

    TaxableBase REAL NOT NULL DEFAULT 0,
    TaxAmount REAL NOT NULL DEFAULT 0,

    FOREIGN KEY (InvoiceItemId)
        REFERENCES SalesInvoiceItems(Id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_SalesInvoiceItemTaxComponents_InvoiceItemId
ON SalesInvoiceItemTaxComponents(InvoiceItemId);

";

            using (var command = new SQLiteCommand(
                sql,
                connection,
                transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void CreateReturnTaxComponentTable(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            string sql = @"

CREATE TABLE IF NOT EXISTS SalesReturnItemTaxComponents
(
    Id INTEGER PRIMARY KEY AUTOINCREMENT,

    ReturnItemId INTEGER NOT NULL,

    TaxProfileRateId INTEGER NULL,

    ComponentCode TEXT NOT NULL DEFAULT '',
    ComponentName TEXT NOT NULL DEFAULT '',
    ComponentType TEXT NOT NULL DEFAULT 'VAT',

    RatePercent REAL NOT NULL DEFAULT 0,

    CalculationOrder INTEGER NOT NULL DEFAULT 1,
    IsCompound INTEGER NOT NULL DEFAULT 0,

    TaxableBase REAL NOT NULL DEFAULT 0,
    TaxAmount REAL NOT NULL DEFAULT 0,

    FOREIGN KEY (ReturnItemId)
        REFERENCES SalesReturnItems(Id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_SalesReturnItemTaxComponents_ReturnItemId
ON SalesReturnItemTaxComponents(ReturnItemId);

";

            using (var command = new SQLiteCommand(
                sql,
                connection,
                transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureColumns(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string tableName,
            Dictionary<string, string> columnsToAdd)
        {
            var existingColumns = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            using (var command = new SQLiteCommand(
                "PRAGMA table_info(" + tableName + ");",
                connection,
                transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    existingColumns.Add(
                        Convert.ToString(reader["name"]));
                }
            }

            foreach (var column in columnsToAdd)
            {
                if (existingColumns.Contains(column.Key))
                    continue;

                string sql =
                    "ALTER TABLE " + tableName +
                    " ADD COLUMN " + column.Key +
                    " " + column.Value + ";";

                using (var command = new SQLiteCommand(
                    sql,
                    connection,
                    transaction))
                {
                    command.ExecuteNonQuery();
                }

                existingColumns.Add(column.Key);
            }
        }

        private static void CreateTaxIndexes(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            string sql = @"

CREATE INDEX IF NOT EXISTS IX_Products_TaxProfileId
ON Products(TaxProfileId);

CREATE INDEX IF NOT EXISTS IX_TaxProfileRates_ProfileDate
ON TaxProfileRates(TaxProfileId, EffectiveFrom, EffectiveTo);

CREATE INDEX IF NOT EXISTS IX_TaxAuditLogs_CreatedAt
ON TaxAuditLogs(CreatedAt);

CREATE INDEX IF NOT EXISTS IX_TaxAuditLogs_Entity
ON TaxAuditLogs(EntityType, EntityId);

CREATE INDEX IF NOT EXISTS IX_SalesInvoiceItems_TaxProfileId
ON SalesInvoiceItems(TaxProfileId);

";

            using (var command = new SQLiteCommand(
                sql,
                connection,
                transaction))
            {
                command.ExecuteNonQuery();
            }
        }
    }
}
