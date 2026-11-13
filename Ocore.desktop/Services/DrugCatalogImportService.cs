
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Ocore.desktop.Data;

namespace Ocore.desktop.Services
{
    public class DrugCatalogImportService
    {
        private readonly string connectionString;

        public DrugCatalogImportService()
        {
            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        public class ImportResult
        {
            public int Imported { get; set; }
            public int Skipped { get; set; }
            public int Total { get; set; }
        }

        public ImportResult ImportCsv(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("مسار الملف غير صحيح.");

            if (!File.Exists(filePath))
                throw new FileNotFoundException(
                    "ملف قاعدة بيانات الأدوية غير موجود.",
                    filePath);

            var result = new ImportResult();

            using (var reader = new StreamReader(
                filePath,
                Encoding.UTF8,
                true))
            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string headerLine = reader.ReadLine();

                        ValidateHeader(headerLine);

                        string line;

                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            result.Total++;

                            List<string> fields = ParseCsvLine(line);

                            if (fields.Count != 7)
                            {
                                result.Skipped++;
                                continue;
                            }

                            string englishName = Clean(fields[0]);
                            string arabicName = Clean(fields[1]);
                            string genericName = Clean(fields[2]);
                            string manufacturer = Clean(fields[3]);
                            string drugClass = Clean(fields[4]);
                            string route = Clean(fields[5]);

                            if (string.IsNullOrWhiteSpace(englishName))
                            {
                                result.Skipped++;
                                continue;
                            }

                            double price;

                            if (!double.TryParse(
                                Clean(fields[6]),
                                NumberStyles.Any,
                                CultureInfo.InvariantCulture,
                                out price))
                            {
                                price = 0;
                            }

                            string sourceStatus =
                                ExtractSourceStatus(englishName);

                            string sourceKey = CreateSourceKey(
                                englishName,
                                arabicName,
                                genericName,
                                manufacturer,
                                drugClass,
                                route,
                                price);

                            string sql = @"
INSERT OR IGNORE INTO DrugCatalog
(
    RegistrationNumber,
    TradeName,
    TradeNameArabic,
    GenericName,
    Strength,
    DosageForm,
    RouteOfAdministration,
    Manufacturer,
    MarketingAuthorizationHolder,
    DrugClass,
    PackageDescription,
    ReferencePrice,
    SourceStatus,
    SourceKey,
    IsActive,
    DataSource,
    LastVerifiedAt,
    CreatedAt
)
VALUES
(
    '',
    @TradeName,
    @TradeNameArabic,
    @GenericName,
    '',
    '',
    @Route,
    @Manufacturer,
    '',
    @DrugClass,
    '',
    @ReferencePrice,
    @SourceStatus,
    @SourceKey,
    1,
    @DataSource,
    @LastVerifiedAt,
    @CreatedAt
);";

                            using (var command =
                                new SQLiteCommand(
                                    sql,
                                    connection,
                                    transaction))
                            {
                                command.Parameters.AddWithValue(
                                    "@TradeName", englishName);

                                command.Parameters.AddWithValue(
                                    "@TradeNameArabic", arabicName);

                                command.Parameters.AddWithValue(
                                    "@GenericName", genericName);

                                command.Parameters.AddWithValue(
                                    "@Route", route);

                                command.Parameters.AddWithValue(
                                    "@Manufacturer", manufacturer);

                                command.Parameters.AddWithValue(
                                    "@DrugClass", drugClass);

                                command.Parameters.AddWithValue(
                                    "@ReferencePrice", price);

                                command.Parameters.AddWithValue(
                                    "@SourceStatus", sourceStatus);

                                command.Parameters.AddWithValue(
                                    "@SourceKey", sourceKey);

                                command.Parameters.AddWithValue(
                                    "@DataSource",
                                    "DrugEye GitHub CSV");

                                command.Parameters.AddWithValue(
                                    "@LastVerifiedAt",
                                    DateTime.Now.ToString(
                                        "yyyy-MM-dd HH:mm:ss"));

                                command.Parameters.AddWithValue(
                                    "@CreatedAt",
                                    DateTime.Now.ToString(
                                        "yyyy-MM-dd HH:mm:ss"));

                                int affected = command.ExecuteNonQuery();

                                if (affected > 0)
                                    result.Imported++;
                                else
                                    result.Skipped++;
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

            return result;
        }

        private void ValidateHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
                throw new InvalidDataException(
                    "ملف CSV فارغ.");

            List<string> columns = ParseCsvLine(header);

            string[] expected =
            {
                "commercial_name_en",
                "commercial_name_ar",
                "scientific_name",
                "manufacturer",
                "drug_class",
                "route",
                "price_egp"
            };

            if (columns.Count != expected.Length)
                throw new InvalidDataException(
                    "أعمدة الملف لا تطابق تنسيق Drug Eye.");

            for (int i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(
                    Clean(columns[i]),
                    expected[i],
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "تنسيق ملف CSV غير صحيح. العمود غير المتوقع: "
                        + columns[i]);
                }
            }
        }

        private List<string> ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var value = new StringBuilder();

            bool insideQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char current = line[i];

                if (current == '"')
                {
                    if (insideQuotes &&
                        i + 1 < line.Length &&
                        line[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                    }
                    else
                    {
                        insideQuotes = !insideQuotes;
                    }
                }
                else if (current == ',' && !insideQuotes)
                {
                    fields.Add(value.ToString());
                    value.Clear();
                }
                else
                {
                    value.Append(current);
                }
            }

            fields.Add(value.ToString());

            return fields;
        }

        private string Clean(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private string ExtractSourceStatus(string name)
        {
            string upper = (name ?? string.Empty).ToUpperInvariant();

            if (upper.Contains("(CANCELLED)"))
                return "CANCELLED";

            if (upper.Contains("(ILLEGAL IMPORT)"))
                return "ILLEGAL IMPORT";

            if (upper.Contains("(N/A YET)"))
                return "N/A YET";

            if (upper.Contains("(N/A)"))
                return "N/A";

            return string.Empty;
        }

        private string CreateSourceKey(
            string englishName,
            string arabicName,
            string genericName,
            string manufacturer,
            string drugClass,
            string route,
            double price)
        {
            string source = string.Join(
                "|",
                Clean(englishName).ToUpperInvariant(),
                Clean(arabicName),
                Clean(genericName).ToUpperInvariant(),
                Clean(manufacturer).ToUpperInvariant(),
                Clean(drugClass).ToUpperInvariant(),
                Clean(route).ToUpperInvariant(),
                price.ToString("0.########", CultureInfo.InvariantCulture));

            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(source);
                byte[] hash = sha.ComputeHash(bytes);

                var builder = new StringBuilder();

                foreach (byte b in hash)
                    builder.Append(b.ToString("X2"));

                return builder.ToString();
            }
        }
    }
}
