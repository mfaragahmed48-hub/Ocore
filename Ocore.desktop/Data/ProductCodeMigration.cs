
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace Ocore.desktop.Data
{
    public static class ProductCodeMigration
    {
        private const long MaximumCodeValue = 9999999999L;

        private class ProductCodeRow
        {
            public int Id { get; set; }
            public string OldCode { get; set; }
            public string NewCode { get; set; }
        }

        public static void Execute(SQLiteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var products = new List<ProductCodeRow>();

                    using (var command = new SQLiteCommand(
                        @"SELECT Id, ProductCode
                          FROM Products
                          ORDER BY Id;",
                        connection,
                        transaction))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            products.Add(new ProductCodeRow
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                OldCode = Convert.ToString(
                                    reader["ProductCode"])
                            });
                        }
                    }

                    EnsureSequenceTable(connection, transaction);

                    long sequenceValue = GetSequenceValue(
                        connection,
                        transaction);

                    var usedCodes = new HashSet<string>(
                        StringComparer.Ordinal);

                    long maximumNumericCode = 0;

                    // الاحتفاظ بأول نسخة من كل كود رقمي بعد إزالة الأصفار.
                    foreach (var product in products)
                    {
                        string normalized = NormalizeNumericCode(
                            product.OldCode);

                        if (normalized == null)
                            continue;

                        long numericValue;

                        if (!long.TryParse(
                            normalized,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out numericValue) ||
                            numericValue > MaximumCodeValue)
                        {
                            throw new InvalidOperationException(
                                "الكود الرقمي للصنف رقم " +
                                product.Id +
                                " يتجاوز الحد الأقصى المسموح به.");
                        }

                        if (numericValue > maximumNumericCode)
                            maximumNumericCode = numericValue;

                        if (usedCodes.Add(normalized))
                        {
                            product.NewCode = normalized;
                        }
                    }

                    if (sequenceValue > MaximumCodeValue)
                    {
                        throw new InvalidOperationException(
                            "قيمة تسلسل الأكواد الحالية تتجاوز الحد الأقصى.");
                    }

                    long nextValue = Math.Max(
                        sequenceValue,
                        maximumNumericCode);

                    // الأكواد الحرفية والأكواد الرقمية المتكررة
                    // تحصل على أكواد رقمية جديدة.
                    foreach (var product in products)
                    {
                        if (!string.IsNullOrEmpty(product.NewCode))
                            continue;

                        product.NewCode = AllocateCode(
                            usedCodes,
                            ref nextValue);
                    }

                    // استخدام أكواد مؤقتة لتجنب تعارض UNIQUE
                    // أثناء تبديل الأكواد بين الأصناف.
                    foreach (var product in products)
                    {
                        string temporaryCode =
                            "__OCORE_MIGRATION_" +
                            Guid.NewGuid().ToString("N");

                        using (var command = new SQLiteCommand(
                            @"UPDATE Products
                              SET ProductCode = @Code
                              WHERE Id = @Id;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@Code",
                                temporaryCode);

                            command.Parameters.AddWithValue(
                                "@Id",
                                product.Id);

                            if (command.ExecuteNonQuery() != 1)
                            {
                                throw new InvalidOperationException(
                                    "تعذر تجهيز كود الصنف رقم " +
                                    product.Id);
                            }
                        }
                    }

                    // حفظ الأكواد النهائية.
                    foreach (var product in products)
                    {
                        using (var command = new SQLiteCommand(
                            @"UPDATE Products
                              SET ProductCode = @Code
                              WHERE Id = @Id;",
                            connection,
                            transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@Code",
                                product.NewCode);

                            command.Parameters.AddWithValue(
                                "@Id",
                                product.Id);

                            if (command.ExecuteNonQuery() != 1)
                            {
                                throw new InvalidOperationException(
                                    "تعذر حفظ كود الصنف رقم " +
                                    product.Id);
                            }
                        }
                    }

                    UpdateSequence(
                        connection,
                        transaction,
                        nextValue);

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        private static string NormalizeNumericCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            code = code.Trim();

            for (int i = 0; i < code.Length; i++)
            {
                if (code[i] < '0' || code[i] > '9')
                    return null;
            }

            string normalized = code.TrimStart('0');

            return normalized.Length == 0
                ? "0"
                : normalized;
        }

        private static string AllocateCode(
            HashSet<string> usedCodes,
            ref long nextValue)
        {
            while (true)
            {
                if (nextValue >= MaximumCodeValue)
                {
                    throw new InvalidOperationException(
                        "لا توجد أكواد رقمية متاحة ضمن الحد الأقصى.");
                }

                nextValue++;

                string candidate = nextValue.ToString(
                    CultureInfo.InvariantCulture);

                if (usedCodes.Add(candidate))
                    return candidate;
            }
        }

        private static void EnsureSequenceTable(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            using (var command = new SQLiteCommand(
                @"CREATE TABLE IF NOT EXISTS ProductCodeSequence
                  (
                      Id INTEGER PRIMARY KEY CHECK (Id = 1),
                      LastValue INTEGER NOT NULL
                  );",
                connection,
                transaction))
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand(
                @"INSERT OR IGNORE INTO ProductCodeSequence
                  (Id, LastValue)
                  VALUES (1, 0);",
                connection,
                transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private static long GetSequenceValue(
            SQLiteConnection connection,
            SQLiteTransaction transaction)
        {
            using (var command = new SQLiteCommand(
                @"SELECT LastValue
                  FROM ProductCodeSequence
                  WHERE Id = 1;",
                connection,
                transaction))
            {
                return Convert.ToInt64(command.ExecuteScalar());
            }
        }

        private static void UpdateSequence(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long value)
        {
            using (var command = new SQLiteCommand(
                @"UPDATE ProductCodeSequence
                  SET LastValue = MAX(LastValue, @Value)
                  WHERE Id = 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue("@Value", value);
                command.ExecuteNonQuery();
            }
        }
    }
}
