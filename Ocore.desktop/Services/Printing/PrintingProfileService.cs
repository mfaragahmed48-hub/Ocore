
using Ocore.desktop.Data;
using Ocore.desktop.Models.Printing;
using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Ocore.desktop.Services.Printing
{
    /// <summary>
    /// إدارة إعدادات وظائف الطباعة من قاعدة البيانات المركزية.
    /// </summary>
    public class PrintingProfileService
    {
        private readonly DatabaseManager databaseManager;

        public PrintingProfileService()
            : this(new DatabaseManager())
        {
        }

        public PrintingProfileService(DatabaseManager manager)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            databaseManager = manager;
        }

        /// <summary>
        /// استرجاع إعدادات وظيفة الطباعة.
        /// إذا لم تكن موجودة، يتم إنشاء الإعدادات الافتراضية وحفظها.
        /// </summary>
        public PrintingProfile GetProfile(string functionKey)
        {
            ValidateFunctionKey(functionKey);

            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                PrintingProfile profile =
                    ReadProfile(connection, functionKey);

                if (profile != null)
                    return profile;
            }

            // لا توجد إعدادات محفوظة لهذه الوظيفة.
            // ننشئ الافتراضيات ونحفظها دون المساس بالوظائف الأخرى.
            PrintingProfile defaultProfile =
                PrintingProfileDefaults.Create(functionKey);

            SaveProfile(defaultProfile);

            return defaultProfile;
        }

        /// <summary>
        /// حفظ إعدادات وظيفة طباعة في قاعدة البيانات المركزية.
        /// </summary>
        public void SaveProfile(PrintingProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            ValidateFunctionKey(profile.FunctionKey);

            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int affectedRows;

                        using (var command = new SQLiteCommand(
                            @"UPDATE PrintingProfiles SET
                                PrinterName = @PrinterName,
                                PrintMode = @PrintMode,
                                PaperSize = @PaperSize,
                                PaperWidth = @PaperWidth,
                                PaperHeight = @PaperHeight,
                                PrintWidthDots = @PrintWidthDots,
                                MarginLeft = @MarginLeft,
                                MarginRight = @MarginRight,
                                MarginTop = @MarginTop,
                                MarginBottom = @MarginBottom,
                                Orientation = @Orientation,
                                FontScale = @FontScale,
                                AutoCut = @AutoCut,
                                OpenCashDrawer = @OpenCashDrawer,

                                BarcodePrinter = @BarcodePrinter,
                                BarcodePrintType = @BarcodePrintType,
                                BarcodeShowPrice = @BarcodeShowPrice,
                                BarcodeShowProductCode = @BarcodeShowProductCode,

                                BarcodeA4Printer = @BarcodeA4Printer,
                                BarcodeA4LabelWidth = @BarcodeA4LabelWidth,
                                BarcodeA4LabelHeight = @BarcodeA4LabelHeight,
                                BarcodeA4Margin = @BarcodeA4Margin,
                                BarcodeA4Gap = @BarcodeA4Gap,

                                BarcodeRollPrinter = @BarcodeRollPrinter,
                                BarcodeRollLabelWidth = @BarcodeRollLabelWidth,
                                BarcodeRollLabelHeight = @BarcodeRollLabelHeight,
                                BarcodeRollGap = @BarcodeRollGap,
                                BarcodeRollOffsetX = @BarcodeRollOffsetX,
                                BarcodeRollOffsetY = @BarcodeRollOffsetY,
                                BarcodeRollOrientation = @BarcodeRollOrientation

                              WHERE FunctionKey = @FunctionKey;",
                            connection,
                            transaction))
                        {
                            AddProfileParameters(command, profile);

                            affectedRows = command.ExecuteNonQuery();
                        }

                        if (affectedRows == 0)
                        {
                            using (var command = new SQLiteCommand(
                                @"INSERT INTO PrintingProfiles
                                (
                                    FunctionKey,
                                    PrinterName,
                                    PrintMode,
                                    PaperSize,
                                    PaperWidth,
                                    PaperHeight,
                                    PrintWidthDots,
                                    MarginLeft,
                                    MarginRight,
                                    MarginTop,
                                    MarginBottom,
                                    Orientation,
                                    FontScale,
                                    AutoCut,
                                    OpenCashDrawer,

                                    BarcodePrinter,
                                    BarcodePrintType,
                                    BarcodeShowPrice,
                                    BarcodeShowProductCode,

                                    BarcodeA4Printer,
                                    BarcodeA4LabelWidth,
                                    BarcodeA4LabelHeight,
                                    BarcodeA4Margin,
                                    BarcodeA4Gap,

                                    BarcodeRollPrinter,
                                    BarcodeRollLabelWidth,
                                    BarcodeRollLabelHeight,
                                    BarcodeRollGap,
                                    BarcodeRollOffsetX,
                                    BarcodeRollOffsetY,
                                    BarcodeRollOrientation
                                )
                                VALUES
                                (
                                    @FunctionKey,
                                    @PrinterName,
                                    @PrintMode,
                                    @PaperSize,
                                    @PaperWidth,
                                    @PaperHeight,
                                    @PrintWidthDots,
                                    @MarginLeft,
                                    @MarginRight,
                                    @MarginTop,
                                    @MarginBottom,
                                    @Orientation,
                                    @FontScale,
                                    @AutoCut,
                                    @OpenCashDrawer,

                                    @BarcodePrinter,
                                    @BarcodePrintType,
                                    @BarcodeShowPrice,
                                    @BarcodeShowProductCode,

                                    @BarcodeA4Printer,
                                    @BarcodeA4LabelWidth,
                                    @BarcodeA4LabelHeight,
                                    @BarcodeA4Margin,
                                    @BarcodeA4Gap,

                                    @BarcodeRollPrinter,
                                    @BarcodeRollLabelWidth,
                                    @BarcodeRollLabelHeight,
                                    @BarcodeRollGap,
                                    @BarcodeRollOffsetX,
                                    @BarcodeRollOffsetY,
                                    @BarcodeRollOrientation
                                );",
                                connection,
                                transaction))
                            {
                                AddProfileParameters(command, profile);
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

                // تحديث رقم السجل في الكائن بعد الحفظ.
                using (var command = new SQLiteCommand(
                    @"SELECT Id
                      FROM PrintingProfiles
                      WHERE FunctionKey = @FunctionKey
                      LIMIT 1;",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@FunctionKey",
                        profile.FunctionKey);

                    object result = command.ExecuteScalar();

                    if (result != null && result != DBNull.Value)
                    {
                        profile.Id = Convert.ToInt32(result);
                    }
                }
            }
        }

        /// <summary>
        /// استرجاع جميع إعدادات وظائف الطباعة المحفوظة.
        /// </summary>
        public List<PrintingProfile> GetAllProfiles()
        {
            var profiles = new List<PrintingProfile>();

            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    @"SELECT *
                      FROM PrintingProfiles
                      ORDER BY FunctionKey;",
                    connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        profiles.Add(MapProfile(reader));
                    }
                }
            }

            return profiles;
        }

        private PrintingProfile ReadProfile(
            SQLiteConnection connection,
            string functionKey)
        {
            using (var command = new SQLiteCommand(
                @"SELECT *
                  FROM PrintingProfiles
                  WHERE FunctionKey = @FunctionKey
                  LIMIT 1;",
                connection))
            {
                command.Parameters.AddWithValue(
                    "@FunctionKey",
                    functionKey);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return MapProfile(reader);
                }
            }
        }

        private PrintingProfile MapProfile(SQLiteDataReader reader)
        {
            var profile = new PrintingProfile();

            profile.Id = GetInt(reader, "Id");
            profile.FunctionKey = GetString(reader, "FunctionKey");

            profile.PrinterName = GetString(reader, "PrinterName");
            profile.PrintMode = GetString(reader, "PrintMode");
            profile.PaperSize = GetString(reader, "PaperSize");

            profile.PaperWidth = GetDouble(reader, "PaperWidth");
            profile.PaperHeight = GetDouble(reader, "PaperHeight");
            profile.PrintWidthDots = GetInt(reader, "PrintWidthDots");

            profile.MarginLeft = GetDouble(reader, "MarginLeft");
            profile.MarginRight = GetDouble(reader, "MarginRight");
            profile.MarginTop = GetDouble(reader, "MarginTop");
            profile.MarginBottom = GetDouble(reader, "MarginBottom");

            profile.Orientation = GetString(reader, "Orientation");
            profile.FontScale = GetDouble(reader, "FontScale");

            profile.AutoCut = GetBool(reader, "AutoCut");
            profile.OpenCashDrawer = GetBool(reader, "OpenCashDrawer");

            profile.BarcodePrinter = GetString(reader, "BarcodePrinter");
            profile.BarcodePrintType = GetString(reader, "BarcodePrintType");

            profile.BarcodeShowPrice =
                GetBool(reader, "BarcodeShowPrice");

            profile.BarcodeShowProductCode =
                GetBool(reader, "BarcodeShowProductCode");

            profile.BarcodeA4Printer =
                GetString(reader, "BarcodeA4Printer");

            profile.BarcodeA4LabelWidth =
                GetString(reader, "BarcodeA4LabelWidth");

            profile.BarcodeA4LabelHeight =
                GetString(reader, "BarcodeA4LabelHeight");

            profile.BarcodeA4Margin =
                GetString(reader, "BarcodeA4Margin");

            profile.BarcodeA4Gap =
                GetString(reader, "BarcodeA4Gap");

            profile.BarcodeRollPrinter =
                GetString(reader, "BarcodeRollPrinter");

            profile.BarcodeRollLabelWidth =
                GetString(reader, "BarcodeRollLabelWidth");

            profile.BarcodeRollLabelHeight =
                GetString(reader, "BarcodeRollLabelHeight");

            profile.BarcodeRollGap =
                GetString(reader, "BarcodeRollGap");

            profile.BarcodeRollOffsetX =
                GetString(reader, "BarcodeRollOffsetX");

            profile.BarcodeRollOffsetY =
                GetString(reader, "BarcodeRollOffsetY");

            profile.BarcodeRollOrientation =
                GetString(reader, "BarcodeRollOrientation");

            return profile;
        }

        private void AddProfileParameters(
            SQLiteCommand command,
            PrintingProfile profile)
        {
            command.Parameters.AddWithValue(
                "@FunctionKey", profile.FunctionKey);

            command.Parameters.AddWithValue(
                "@PrinterName", DbValue(profile.PrinterName));

            command.Parameters.AddWithValue(
                "@PrintMode", DbValue(profile.PrintMode));

            command.Parameters.AddWithValue(
                "@PaperSize", DbValue(profile.PaperSize));

            command.Parameters.AddWithValue(
                "@PaperWidth", profile.PaperWidth);

            command.Parameters.AddWithValue(
                "@PaperHeight", profile.PaperHeight);

            command.Parameters.AddWithValue(
                "@PrintWidthDots", profile.PrintWidthDots);

            command.Parameters.AddWithValue(
                "@MarginLeft", profile.MarginLeft);

            command.Parameters.AddWithValue(
                "@MarginRight", profile.MarginRight);

            command.Parameters.AddWithValue(
                "@MarginTop", profile.MarginTop);

            command.Parameters.AddWithValue(
                "@MarginBottom", profile.MarginBottom);

            command.Parameters.AddWithValue(
                "@Orientation", DbValue(profile.Orientation));

            command.Parameters.AddWithValue(
                "@FontScale", profile.FontScale);

            command.Parameters.AddWithValue(
                "@AutoCut", profile.AutoCut ? 1 : 0);

            command.Parameters.AddWithValue(
                "@OpenCashDrawer", profile.OpenCashDrawer ? 1 : 0);

            command.Parameters.AddWithValue(
                "@BarcodePrinter", DbValue(profile.BarcodePrinter));

            command.Parameters.AddWithValue(
                "@BarcodePrintType", DbValue(profile.BarcodePrintType));

            command.Parameters.AddWithValue(
                "@BarcodeShowPrice", profile.BarcodeShowPrice ? 1 : 0);

            command.Parameters.AddWithValue(
                "@BarcodeShowProductCode",
                profile.BarcodeShowProductCode ? 1 : 0);

            command.Parameters.AddWithValue(
                "@BarcodeA4Printer", DbValue(profile.BarcodeA4Printer));

            command.Parameters.AddWithValue(
                "@BarcodeA4LabelWidth",
                DbValue(profile.BarcodeA4LabelWidth));

            command.Parameters.AddWithValue(
                "@BarcodeA4LabelHeight",
                DbValue(profile.BarcodeA4LabelHeight));

            command.Parameters.AddWithValue(
                "@BarcodeA4Margin", DbValue(profile.BarcodeA4Margin));

            command.Parameters.AddWithValue(
                "@BarcodeA4Gap", DbValue(profile.BarcodeA4Gap));

            command.Parameters.AddWithValue(
                "@BarcodeRollPrinter", DbValue(profile.BarcodeRollPrinter));

            command.Parameters.AddWithValue(
                "@BarcodeRollLabelWidth",
                DbValue(profile.BarcodeRollLabelWidth));

            command.Parameters.AddWithValue(
                "@BarcodeRollLabelHeight",
                DbValue(profile.BarcodeRollLabelHeight));

            command.Parameters.AddWithValue(
                "@BarcodeRollGap", DbValue(profile.BarcodeRollGap));

            command.Parameters.AddWithValue(
                "@BarcodeRollOffsetX", DbValue(profile.BarcodeRollOffsetX));

            command.Parameters.AddWithValue(
                "@BarcodeRollOffsetY", DbValue(profile.BarcodeRollOffsetY));

            command.Parameters.AddWithValue(
                "@BarcodeRollOrientation",
                DbValue(profile.BarcodeRollOrientation));
        }

        private object DbValue(string value)
        {
            return value ?? string.Empty;
        }

        private string GetString(
            SQLiteDataReader reader,
            string columnName)
        {
            int index = reader.GetOrdinal(columnName);

            return reader.IsDBNull(index)
                ? string.Empty
                : Convert.ToString(reader.GetValue(index));
        }

        private int GetInt(
            SQLiteDataReader reader,
            string columnName)
        {
            int index = reader.GetOrdinal(columnName);

            return reader.IsDBNull(index)
                ? 0
                : Convert.ToInt32(reader.GetValue(index));
        }

        private double GetDouble(
            SQLiteDataReader reader,
            string columnName)
        {
            int index = reader.GetOrdinal(columnName);

            return reader.IsDBNull(index)
                ? 0
                : Convert.ToDouble(reader.GetValue(index));
        }

        private bool GetBool(
            SQLiteDataReader reader,
            string columnName)
        {
            int index = reader.GetOrdinal(columnName);

            return !reader.IsDBNull(index) &&
                   Convert.ToInt32(reader.GetValue(index)) != 0;
        }

        private void ValidateFunctionKey(string functionKey)
        {
            if (string.IsNullOrWhiteSpace(functionKey))
            {
                throw new ArgumentException(
                    "مفتاح وظيفة الطباعة مطلوب.",
                    nameof(functionKey));
            }
        }
    }
}
