
using System;
using System.Data.SQLite;
using Ocore.desktop.Data;
using Ocore.desktop.Models.Printing;

namespace Ocore.desktop.Services.Printing
{
    /// <summary>
    /// خدمة إدارة إعدادات أنواع الطابعات الجديدة.
    /// مصدر البيانات الوحيد هو جدول PrinterConfigurations
    /// داخل قاعدة بيانات الجهاز المحلية.
    /// </summary>
    public class PrinterConfigurationService
    {
        private readonly string connectionString;

        public PrinterConfigurationService()
        {
            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        /// <summary>
        /// قراءة إعدادات نوع طابعة.
        /// ترجع null إذا لم توجد إعدادات محفوظة.
        /// لا تنشئ سجلاً افتراضياً.
        /// </summary>
        public PrinterConfiguration GetConfiguration(string printerType)
        {
            ValidatePrinterType(printerType);

            const string sql = @"
SELECT
    Id,
    PrinterType,
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
    BarcodeLabelWidth,
    BarcodeLabelHeight,
    BarcodeGap,
    BarcodeOffsetX,
    BarcodeOffsetY,
    BarcodeShowPrice,
    BarcodeShowProductCode,
    UpdatedAt
FROM PrinterConfigurations
WHERE PrinterType = @PrinterType
LIMIT 1;";

            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue(
                    "@PrinterType", printerType);

                connection.Open();

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return ReadConfiguration(reader);
                }
            }
        }

        /// <summary>
        /// حفظ إعدادات نوع طابعة واحد فقط.
        /// يتم الحفظ عند استدعاء هذه الدالة صراحةً.
        /// </summary>
        public void SaveConfiguration(
            PrinterConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            ValidatePrinterType(configuration.PrinterType);
            ValidateConfiguration(configuration);

            configuration.UpdatedAt =
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        const string updateSql = @"
UPDATE PrinterConfigurations
SET
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
    BarcodeLabelWidth = @BarcodeLabelWidth,
    BarcodeLabelHeight = @BarcodeLabelHeight,
    BarcodeGap = @BarcodeGap,
    BarcodeOffsetX = @BarcodeOffsetX,
    BarcodeOffsetY = @BarcodeOffsetY,
    BarcodeShowPrice = @BarcodeShowPrice,
    BarcodeShowProductCode = @BarcodeShowProductCode,
    UpdatedAt = @UpdatedAt
WHERE PrinterType = @PrinterType;";

                        int affectedRows;

                        using (var command = new SQLiteCommand(
                            updateSql, connection, transaction))
                        {
                            AddConfigurationParameters(
                                command, configuration);

                            affectedRows = command.ExecuteNonQuery();
                        }

                        if (affectedRows == 0)
                        {
                            const string insertSql = @"
INSERT INTO PrinterConfigurations
(
    PrinterType,
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
    BarcodeLabelWidth,
    BarcodeLabelHeight,
    BarcodeGap,
    BarcodeOffsetX,
    BarcodeOffsetY,
    BarcodeShowPrice,
    BarcodeShowProductCode,
    UpdatedAt
)
VALUES
(
    @PrinterType,
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
    @BarcodeLabelWidth,
    @BarcodeLabelHeight,
    @BarcodeGap,
    @BarcodeOffsetX,
    @BarcodeOffsetY,
    @BarcodeShowPrice,
    @BarcodeShowProductCode,
    @UpdatedAt
);";

                            using (var command = new SQLiteCommand(
                                insertSql, connection, transaction))
                            {
                                AddConfigurationParameters(
                                    command, configuration);

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

        private static PrinterConfiguration ReadConfiguration(
            SQLiteDataReader reader)
        {
            return new PrinterConfiguration
            {
                Id = Convert.ToInt32(reader["Id"]),

                PrinterType = Convert.ToString(reader["PrinterType"]),
                PrinterName = Convert.ToString(reader["PrinterName"]),

                PrintMode = Convert.ToString(reader["PrintMode"]),
                PaperSize = Convert.ToString(reader["PaperSize"]),

                PaperWidth = Convert.ToDouble(reader["PaperWidth"]),
                PaperHeight = Convert.ToDouble(reader["PaperHeight"]),
                PrintWidthDots = Convert.ToInt32(reader["PrintWidthDots"]),

                MarginLeft = Convert.ToDouble(reader["MarginLeft"]),
                MarginRight = Convert.ToDouble(reader["MarginRight"]),
                MarginTop = Convert.ToDouble(reader["MarginTop"]),
                MarginBottom = Convert.ToDouble(reader["MarginBottom"]),

                Orientation = Convert.ToString(reader["Orientation"]),
                FontScale = Convert.ToDouble(reader["FontScale"]),

                AutoCut = Convert.ToInt32(reader["AutoCut"]) == 1,
                OpenCashDrawer =
                    Convert.ToInt32(reader["OpenCashDrawer"]) == 1,

                BarcodeLabelWidth =
                    Convert.ToDouble(reader["BarcodeLabelWidth"]),

                BarcodeLabelHeight =
                    Convert.ToDouble(reader["BarcodeLabelHeight"]),

                BarcodeGap =
                    Convert.ToDouble(reader["BarcodeGap"]),

                BarcodeOffsetX =
                    Convert.ToDouble(reader["BarcodeOffsetX"]),

                BarcodeOffsetY =
                    Convert.ToDouble(reader["BarcodeOffsetY"]),

                BarcodeShowPrice =
                    Convert.ToInt32(reader["BarcodeShowPrice"]) == 1,

                BarcodeShowProductCode =
                    Convert.ToInt32(reader["BarcodeShowProductCode"]) == 1,

                UpdatedAt = Convert.ToString(reader["UpdatedAt"])
            };
        }

        private static void AddConfigurationParameters(
            SQLiteCommand command,
            PrinterConfiguration configuration)
        {
            command.Parameters.AddWithValue(
                "@PrinterType", configuration.PrinterType);

            command.Parameters.AddWithValue(
                "@PrinterName", configuration.PrinterName ?? string.Empty);

            command.Parameters.AddWithValue(
                "@PrintMode", configuration.PrintMode ?? "WindowsDriver");

            command.Parameters.AddWithValue(
                "@PaperSize", configuration.PaperSize ?? "A4");

            command.Parameters.AddWithValue(
                "@PaperWidth", configuration.PaperWidth);

            command.Parameters.AddWithValue(
                "@PaperHeight", configuration.PaperHeight);

            command.Parameters.AddWithValue(
                "@PrintWidthDots", configuration.PrintWidthDots);

            command.Parameters.AddWithValue(
                "@MarginLeft", configuration.MarginLeft);

            command.Parameters.AddWithValue(
                "@MarginRight", configuration.MarginRight);

            command.Parameters.AddWithValue(
                "@MarginTop", configuration.MarginTop);

            command.Parameters.AddWithValue(
                "@MarginBottom", configuration.MarginBottom);

            command.Parameters.AddWithValue(
                "@Orientation", configuration.Orientation ?? "Portrait");

            command.Parameters.AddWithValue(
                "@FontScale", configuration.FontScale);

            command.Parameters.AddWithValue(
                "@AutoCut", configuration.AutoCut ? 1 : 0);

            command.Parameters.AddWithValue(
                "@OpenCashDrawer", configuration.OpenCashDrawer ? 1 : 0);

            command.Parameters.AddWithValue(
                "@BarcodeLabelWidth", configuration.BarcodeLabelWidth);

            command.Parameters.AddWithValue(
                "@BarcodeLabelHeight", configuration.BarcodeLabelHeight);

            command.Parameters.AddWithValue(
                "@BarcodeGap", configuration.BarcodeGap);

            command.Parameters.AddWithValue(
                "@BarcodeOffsetX", configuration.BarcodeOffsetX);

            command.Parameters.AddWithValue(
                "@BarcodeOffsetY", configuration.BarcodeOffsetY);

            command.Parameters.AddWithValue(
                "@BarcodeShowPrice",
                configuration.BarcodeShowPrice ? 1 : 0);

            command.Parameters.AddWithValue(
                "@BarcodeShowProductCode",
                configuration.BarcodeShowProductCode ? 1 : 0);

            command.Parameters.AddWithValue(
                "@UpdatedAt", configuration.UpdatedAt ?? string.Empty);
        }

        private static void ValidatePrinterType(string printerType)
        {
            if (printerType != PrinterType.Cashier &&
                printerType != PrinterType.Normal &&
                printerType != PrinterType.BarcodeRoll)
            {
                throw new ArgumentException(
                    "نوع الطابعة غير صحيح.",
                    nameof(printerType));
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        private static void ValidateFiniteValues(
            PrinterConfiguration configuration)
        {
            if (!IsFinite(configuration.PaperWidth) ||
                !IsFinite(configuration.PaperHeight) ||
                !IsFinite(configuration.MarginLeft) ||
                !IsFinite(configuration.MarginRight) ||
                !IsFinite(configuration.MarginTop) ||
                !IsFinite(configuration.MarginBottom) ||
                !IsFinite(configuration.FontScale) ||
                !IsFinite(configuration.BarcodeLabelWidth) ||
                !IsFinite(configuration.BarcodeLabelHeight) ||
                !IsFinite(configuration.BarcodeGap) ||
                !IsFinite(configuration.BarcodeOffsetX) ||
                !IsFinite(configuration.BarcodeOffsetY))
            {
                throw new InvalidOperationException(
                    "توجد قيمة رقمية غير صالحة في إعدادات الطابعة.");
            }
        }

        private static void ValidateConfiguration(
            PrinterConfiguration configuration)
        {
            if (configuration.PrinterName == null)
                configuration.PrinterName = string.Empty;

            ValidateFiniteValues(configuration);

            // التحقق الخاص بطابعة الكاشير.
            if (configuration.PrinterType == PrinterType.Cashier)
            {
                if (configuration.PrintMode != "WindowsDriver" &&
                    configuration.PrintMode != "EscPos")
                {
                    throw new InvalidOperationException(
                        "وضع طباعة الكاشير غير صحيح.");
                }

                if (configuration.PaperWidth != 0 &&
                    configuration.PaperWidth != 58 &&
                    configuration.PaperWidth != 80)
                {
                    throw new InvalidOperationException(
                        "عرض ورق الكاشير يجب أن يكون تلقائيًا أو 58 أو 80 مم.");
                }

                if (configuration.PrintWidthDots < 0 ||
                    configuration.PrintWidthDots > 2048)
                {
                    throw new InvalidOperationException(
                        "عرض الطباعة بالنقاط يجب أن يكون بين 0 و2048.");
                }

                ValidateFontScale(configuration.FontScale);
            }

            // التحقق الخاص بالطابعة العادية.
            else if (configuration.PrinterType == PrinterType.Normal)
            {
                if (configuration.PaperSize != "A4" &&
                    configuration.PaperSize != "A5" &&
                    configuration.PaperSize != "Letter")
                {
                    throw new InvalidOperationException(
                        "مقاس الورق للطابعة العادية غير صحيح.");
                }

                if (configuration.Orientation != "Portrait" &&
                    configuration.Orientation != "Landscape")
                {
                    throw new InvalidOperationException(
                        "اتجاه الورق غير صحيح.");
                }

                if (configuration.MarginLeft < 0 ||
                    configuration.MarginLeft > 100 ||
                    configuration.MarginRight < 0 ||
                    configuration.MarginRight > 100 ||
                    configuration.MarginTop < 0 ||
                    configuration.MarginTop > 100 ||
                    configuration.MarginBottom < 0 ||
                    configuration.MarginBottom > 100)
                {
                    throw new InvalidOperationException(
                        "هوامش الطباعة يجب أن تكون بين 0 و100 مم.");
                }

                ValidateFontScale(configuration.FontScale);
            }

            // التحقق الخاص بطابعة ملصقات الباركود Roll فقط.
            else if (configuration.PrinterType == PrinterType.BarcodeRoll)
            {
                if (configuration.BarcodeLabelWidth < 1 ||
                    configuration.BarcodeLabelWidth > 200)
                {
                    throw new InvalidOperationException(
                        "عرض ملصق الباركود يجب أن يكون بين 1 و200 مم.");
                }

                if (configuration.BarcodeLabelHeight < 1 ||
                    configuration.BarcodeLabelHeight > 200)
                {
                    throw new InvalidOperationException(
                        "ارتفاع ملصق الباركود يجب أن يكون بين 1 و200 مم.");
                }

                if (configuration.BarcodeGap < 0 ||
                    configuration.BarcodeGap > 50)
                {
                    throw new InvalidOperationException(
                        "المسافة بين الملصقات يجب أن تكون بين 0 و50 مم.");
                }

                if (configuration.BarcodeOffsetX < -50 ||
                    configuration.BarcodeOffsetX > 50 ||
                    configuration.BarcodeOffsetY < -50 ||
                    configuration.BarcodeOffsetY > 50)
                {
                    throw new InvalidOperationException(
                        "إزاحة ملصق الباركود يجب أن تكون بين -50 و50 مم.");
                }
            }
        }

        private static void ValidateFontScale(double fontScale)
        {
            if (fontScale < 0.7 || fontScale > 1.5)
            {
                throw new InvalidOperationException(
                    "مقياس الخط يجب أن يكون بين 0.7 و1.5.");
            }
        }
    }
}
