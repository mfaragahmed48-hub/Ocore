
using System;
using System.Data.SQLite;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class PharmacySettingsService
    {
        private readonly DatabaseManager databaseManager;
        private readonly PermissionService permissionService;

        public PharmacySettingsService()
        {
            databaseManager = new DatabaseManager();
            databaseManager.InitializeDatabase();

            permissionService = new PermissionService();
        }

        // عرض الإعدادات داخل شاشة إعدادات الصيدلية
        // يتطلب صلاحية Settings.Edit
        public PharmacySettings GetSettings(int userId)
        {
            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                string role = GetActiveUserRole(connection, userId);

                if (string.IsNullOrWhiteSpace(role) ||
                    !permissionService.HasPermission(role, "Settings.Edit"))
                {
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية عرض إعدادات الصيدلية.");
                }

                return ReadSettings(connection);
            }
        }

        // قراءة بيانات الصيدلية للطباعة
        // تسمح للمستخدم النشط الذي لديه صلاحية عرض المشتريات أو المبيعات
        public PharmacySettings GetSettingsForPrinting(int userId)
        {
            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                string role = GetActiveUserRole(connection, userId);

                bool canPrint =
                    !string.IsNullOrWhiteSpace(role) &&
                    (
                        permissionService.HasPermission(role, "Settings.Edit") ||
                        permissionService.HasPermission(role, "Purchases.View") ||
                        permissionService.HasPermission(role, "Sales.View")
                    );

                if (!canPrint)
                {
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية قراءة بيانات الصيدلية للطباعة.");
                }

                return ReadSettings(connection);
            }
        }

        // قراءة بيانات الصيدلية وإعدادات الطابعة الحرارية من قاعدة البيانات
        private PharmacySettings ReadSettings(SQLiteConnection connection)
        {
            string sql = @"
                SELECT
                    Id,
                    PharmacyName,
                    Address,
                    Phone,
                    TaxNumber,
                    ManagerName,
                    LogoPath,
                    UpdatedAt,

                    ThermalPrinterName,
                    ThermalPrintMode,
                    ThermalPaperWidth,
                    ThermalPrintWidthDots,
                    ThermalAutoCut,
                    ThermalOpenCashDrawer,
                    ThermalFontScale

                FROM PharmacySettings
                WHERE Id = 1
                LIMIT 1;";

            using (var command = new SQLiteCommand(sql, connection))
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    return new PharmacySettings
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        PharmacyName = Convert.ToString(reader["PharmacyName"]),
                        Address = Convert.ToString(reader["Address"]),
                        Phone = Convert.ToString(reader["Phone"]),
                        TaxNumber = Convert.ToString(reader["TaxNumber"]),
                        ManagerName = Convert.ToString(reader["ManagerName"]),
                        LogoPath = Convert.ToString(reader["LogoPath"]),
                        UpdatedAt = Convert.ToString(reader["UpdatedAt"]),

                        ThermalPrinterName =
                            Convert.ToString(reader["ThermalPrinterName"]),

                        ThermalPrintMode =
                            Convert.ToString(reader["ThermalPrintMode"]),

                        ThermalPaperWidth =
                            Convert.ToInt32(reader["ThermalPaperWidth"]),

                        ThermalPrintWidthDots =
                            Convert.ToInt32(reader["ThermalPrintWidthDots"]),

                        ThermalAutoCut =
                            Convert.ToInt32(reader["ThermalAutoCut"]) != 0,

                        ThermalOpenCashDrawer =
                            Convert.ToInt32(reader["ThermalOpenCashDrawer"]) != 0,

                        ThermalFontScale =
                            Convert.ToDouble(reader["ThermalFontScale"])
                    };
                }
            }

            return new PharmacySettings();
        }

        // حفظ إعدادات الصيدلية وإعدادات الطابعة الحرارية
        public void SaveSettings(
            int userId,
            PharmacySettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            // تنظيف بيانات الصيدلية
            settings.PharmacyName =
                (settings.PharmacyName ?? string.Empty).Trim();

            settings.Address =
                (settings.Address ?? string.Empty).Trim();

            settings.Phone =
                (settings.Phone ?? string.Empty).Trim();

            settings.TaxNumber =
                (settings.TaxNumber ?? string.Empty).Trim();

            settings.ManagerName =
                (settings.ManagerName ?? string.Empty).Trim();

            settings.LogoPath =
                (settings.LogoPath ?? string.Empty).Trim();

            // تنظيف إعدادات الطابعة
            settings.ThermalPrinterName =
                (settings.ThermalPrinterName ?? string.Empty).Trim();

            settings.ThermalPrintMode =
                (settings.ThermalPrintMode ?? "WindowsDriver").Trim();

            if (string.IsNullOrWhiteSpace(settings.PharmacyName))
                throw new ArgumentException("اسم الصيدلية مطلوب.");

            ValidateThermalSettings(settings);

            using (var connection = databaseManager.GetConnection())
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string role = GetActiveUserRole(
                            connection,
                            userId,
                            transaction);

                        if (string.IsNullOrWhiteSpace(role) ||
                            !permissionService.HasPermission(
                                role,
                                "Settings.Edit"))
                        {
                            throw new UnauthorizedAccessException(
                                "ليس لديك صلاحية تعديل إعدادات الصيدلية.");
                        }

                        string sql = @"
                            UPDATE PharmacySettings
                            SET
                                PharmacyName = @PharmacyName,
                                Address = @Address,
                                Phone = @Phone,
                                TaxNumber = @TaxNumber,
                                ManagerName = @ManagerName,
                                LogoPath = @LogoPath,
                                UpdatedAt = @UpdatedAt,

                                ThermalPrinterName = @ThermalPrinterName,
                                ThermalPrintMode = @ThermalPrintMode,
                                ThermalPaperWidth = @ThermalPaperWidth,
                                ThermalPrintWidthDots = @ThermalPrintWidthDots,
                                ThermalAutoCut = @ThermalAutoCut,
                                ThermalOpenCashDrawer = @ThermalOpenCashDrawer,
                                ThermalFontScale = @ThermalFontScale

                            WHERE Id = 1;";

                        using (var command = new SQLiteCommand(
                            sql, connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@PharmacyName", settings.PharmacyName);

                            command.Parameters.AddWithValue(
                                "@Address", settings.Address);

                            command.Parameters.AddWithValue(
                                "@Phone", settings.Phone);

                            command.Parameters.AddWithValue(
                                "@TaxNumber", settings.TaxNumber);

                            command.Parameters.AddWithValue(
                                "@ManagerName", settings.ManagerName);

                            command.Parameters.AddWithValue(
                                "@LogoPath", settings.LogoPath);

                            command.Parameters.AddWithValue(
                                "@UpdatedAt",
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                            command.Parameters.AddWithValue(
                                "@ThermalPrinterName",
                                settings.ThermalPrinterName);

                            command.Parameters.AddWithValue(
                                "@ThermalPrintMode",
                                settings.ThermalPrintMode);

                            command.Parameters.AddWithValue(
                                "@ThermalPaperWidth",
                                settings.ThermalPaperWidth);

                            command.Parameters.AddWithValue(
                                "@ThermalPrintWidthDots",
                                settings.ThermalPrintWidthDots);

                            command.Parameters.AddWithValue(
                                "@ThermalAutoCut",
                                settings.ThermalAutoCut ? 1 : 0);

                            command.Parameters.AddWithValue(
                                "@ThermalOpenCashDrawer",
                                settings.ThermalOpenCashDrawer ? 1 : 0);

                            command.Parameters.AddWithValue(
                                "@ThermalFontScale",
                                settings.ThermalFontScale);

                            if (command.ExecuteNonQuery() == 0)
                            {
                                throw new Exception(
                                    "لم يتم العثور على سجل إعدادات الصيدلية.");
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

        // التحقق من صحة إعدادات الطابعة الحرارية
        private void ValidateThermalSettings(PharmacySettings settings)
        {
            if (settings.ThermalPrintMode != "WindowsDriver" &&
                settings.ThermalPrintMode != "EscPos")
            {
                throw new ArgumentException(
                    "طريقة الطباعة غير صحيحة. اختر WindowsDriver أو EscPos.");
            }

            if (settings.ThermalPaperWidth != 58 &&
                settings.ThermalPaperWidth != 80)
            {
                throw new ArgumentException(
                    "مقاس ورق الطابعة يجب أن يكون 58 أو 80 مم.");
            }

            // صفر يعني استخدام العرض التلقائي.
            // القيم الموجبة تحدد عرض الطباعة الفعلي بالنقاط.
            if (settings.ThermalPrintWidthDots < 0 ||
                settings.ThermalPrintWidthDots > 2048)
            {
                throw new ArgumentException(
                    "عرض الطباعة الفعلي يجب أن يكون صفرًا للوضع التلقائي " +
                    "أو قيمة موجبة لا تتجاوز 2048 نقطة.");
            }

            if (double.IsNaN(settings.ThermalFontScale) ||
                double.IsInfinity(settings.ThermalFontScale) ||
                settings.ThermalFontScale < 0.7 ||
                settings.ThermalFontScale > 1.5)
            {
                throw new ArgumentException(
                    "حجم خط الفاتورة يجب أن يكون بين 0.7 و1.5.");
            }
        }

        // التحقق من المستخدم النشط وإرجاع دوره
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

                if (result == null || result == DBNull.Value)
                    return null;

                return Convert.ToString(result);
            }
        }
    }
}
