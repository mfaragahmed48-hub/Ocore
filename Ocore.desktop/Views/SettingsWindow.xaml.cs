
using System;
using System.IO;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Ocore.desktop.Models;
using Ocore.desktop.Services;
using Ocore.desktop.Data;

namespace Ocore.desktop.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly int currentUserId;
        private readonly PharmacySettingsService settingsService;
        private readonly DatabaseManager databaseManager;

        private string selectedLogoPath = "";

        public SettingsWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;

            settingsService = new PharmacySettingsService();
            databaseManager = new DatabaseManager();

            LoadSettings();
            ConfigureClearButton();
            ConfigureDrugCatalogImportButton();
        }

        // التحقق من أن المستخدم مدير النظام وحسابه نشط
        private bool IsCurrentUserManager()
        {
            try
            {
                string sql = @"
SELECT Role, IsActive
FROM Users
WHERE Id = @UserId
LIMIT 1;";

                using (var connection = databaseManager.GetConnection())
                {
                    connection.Open();

                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue(
                            "@UserId", currentUserId);

                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                                return false;

                            string role = reader["Role"].ToString();

                            bool isActive =
                                Convert.ToInt32(reader["IsActive"]) == 1;

                            return isActive &&
                                   string.Equals(
                                       role,
                                       "Manager",
                                       StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        // إظهار زر التنظيف للمدير فقط
        private void ConfigureClearButton()
        {
            ClearTestDataButton.Visibility =
                IsCurrentUserManager()
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        // إظهار زر استيراد دليل الأدوية للمدير فقط
        private void ConfigureDrugCatalogImportButton()
        {
            ImportDrugCatalogButton.Visibility =
                IsCurrentUserManager()
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void LoadSettings()
        {
            try
            {
                PharmacySettings settings =
                    settingsService.GetSettings(currentUserId);

                if (settings == null)
                    return;

                PharmacyNameTextBox.Text =
                    settings.PharmacyName;

                AddressTextBox.Text =
                    settings.Address;

                PhoneTextBox.Text =
                    settings.Phone;

                TaxNumberTextBox.Text =
                    settings.TaxNumber;

                ManagerNameTextBox.Text =
                    settings.ManagerName;

                selectedLogoPath =
                    settings.LogoPath ?? "";

                LoadLogoPreview();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحميل إعدادات الصيدلية:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // اختيار صورة الشعار
        private void ChooseLogoButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog dialog = new OpenFileDialog
                {
                    Title = "اختيار شعار الصيدلية",
                    Filter =
                        "ملفات الصور|*.png;*.jpg;*.jpeg;*.bmp|" +
                        "PNG|*.png|" +
                        "JPEG|*.jpg;*.jpeg|" +
                        "Bitmap|*.bmp",

                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    selectedLogoPath = dialog.FileName;
                    LoadLogoPreview();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء اختيار الشعار:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // عرض معاينة الشعار
        private void LoadLogoPreview()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(selectedLogoPath) ||
                    !File.Exists(selectedLogoPath))
                {
                    LogoPreviewImage.Source = null;
                    LogoPathText.Text = "لم يتم اختيار شعار بعد";
                    return;
                }

                BitmapImage bitmap = new BitmapImage();

                bitmap.BeginInit();

                bitmap.UriSource = new Uri(
                    selectedLogoPath,
                    UriKind.Absolute);

                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 300;

                bitmap.EndInit();
                bitmap.Freeze();

                LogoPreviewImage.Source = bitmap;
                LogoPathText.Text = selectedLogoPath;
            }
            catch (Exception ex)
            {
                LogoPreviewImage.Source = null;
                LogoPathText.Text = "تعذر عرض صورة الشعار";

                MessageBox.Show(
                    "تعذر تحميل صورة الشعار:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SaveSettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                PharmacySettings settings =
                    settingsService.GetSettings(currentUserId);

                if (settings == null)
                    settings = new PharmacySettings();

                settings.PharmacyName =
                    PharmacyNameTextBox.Text;

                settings.Address =
                    AddressTextBox.Text;

                settings.Phone =
                    PhoneTextBox.Text;

                settings.TaxNumber =
                    TaxNumberTextBox.Text;

                settings.ManagerName =
                    ManagerNameTextBox.Text;

                settings.LogoPath =
                    selectedLogoPath;

                settingsService.SaveSettings(
                    currentUserId,
                    settings);

                MessageBox.Show(
                    "تم حفظ إعدادات الصيدلية والشعار بنجاح.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "صلاحيات غير كافية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر حفظ الإعدادات:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // استيراد دليل الأدوية من ملف CSV
        private void ImportDrugCatalogButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                // إعادة التحقق من صلاحية المدير
                if (!IsCurrentUserManager())
                {
                    MessageBox.Show(
                        "استيراد دليل الأدوية متاح لمدير النظام فقط.",
                        "صلاحيات غير كافية",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ImportDrugCatalogButton.Visibility =
                        Visibility.Collapsed;

                    return;
                }

                OpenFileDialog dialog = new OpenFileDialog
                {
                    Title = "اختيار ملف دليل الأدوية",
                    Filter = "ملفات CSV (*.csv)|*.csv",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() != true)
                    return;

                MessageBoxResult confirmation =
                    MessageBox.Show(
                        "سيتم استيراد بيانات الأدوية من الملف المحدد إلى دليل الأدوية.\n\n" +
                        "لن تتم إضافة هذه الأدوية إلى المخزون أو إنشاء أصناف قابلة للبيع تلقائيًا.\n\n" +
                        "هل تريد بدء الاستيراد؟",
                        "تأكيد استيراد دليل الأدوية",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                ImportDrugCatalogButton.IsEnabled = false;
                DrugCatalogImportStatusText.Text =
                    "جارٍ قراءة الملف واستيراد بيانات الأدوية...";

                DrugCatalogImportStatusText.Foreground =
                    System.Windows.Media.Brushes.DarkBlue;

                DrugCatalogImportStatusText.UpdateLayout();

                DrugCatalogImportService importService =
                    new DrugCatalogImportService();

                DrugCatalogImportService.ImportResult result =
                    importService.ImportCsv(dialog.FileName);

                DrugCatalogImportStatusText.Text =
                    "اكتمل الاستيراد.\n" +
                    "إجمالي السجلات المقروءة: " + result.Total + "\n" +
                    "السجلات المستوردة: " + result.Imported + "\n" +
                    "السجلات المتخطاة أو المكررة: " + result.Skipped;

                DrugCatalogImportStatusText.Foreground =
                    System.Windows.Media.Brushes.DarkGreen;

                MessageBox.Show(
                    "تم الانتهاء من استيراد دليل الأدوية.\n\n" +
                    "إجمالي السجلات: " + result.Total + "\n" +
                    "تم استيراد: " + result.Imported + "\n" +
                    "تم تخطي: " + result.Skipped,
                    "نتيجة استيراد دليل الأدوية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DrugCatalogImportStatusText.Text =
                    "فشل استيراد دليل الأدوية: " + ex.Message;

                DrugCatalogImportStatusText.Foreground =
                    System.Windows.Media.Brushes.DarkRed;

                MessageBox.Show(
                    "تعذر استيراد دليل الأدوية:\n\n" + ex.Message,
                    "خطأ في الاستيراد",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ImportDrugCatalogButton.IsEnabled = true;
            }
        }

        // تنفيذ إعادة ضبط البيانات التجريبية
        private void ClearTestDataButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                // إعادة التحقق من صلاحية المدير قبل التنفيذ
                if (!IsCurrentUserManager())
                {
                    MessageBox.Show(
                        "هذه العملية متاحة لمدير النظام فقط.",
                        "صلاحيات غير كافية",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    ClearTestDataButton.Visibility =
                        Visibility.Collapsed;

                    return;
                }

                MessageBoxResult firstConfirmation =
                    MessageBox.Show(
                        "تحذير: سيتم حذف البيانات التالية نهائيًا:\n\n" +
                        "• جميع الأصناف والباركود.\n" +
                        "• جميع التصنيفات والموردين.\n" +
                        "• جميع التشغيلات وكميات المخزون.\n" +
                        "• جميع فواتير البيع والشراء.\n" +
                        "• جميع مرتجعات البيع.\n\n" +
                        "سيتم الاحتفاظ بالمستخدمين والصلاحيات وإعدادات الصيدلية والطابعة وسجل العمليات.\n\n" +
                        "سيتم إنشاء نسخة احتياطية قبل الحذف.\n\n" +
                        "هل تريد المتابعة؟",
                        "تأكيد إعادة ضبط البيانات",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);

                if (firstConfirmation != MessageBoxResult.Yes)
                    return;

                MessageBoxResult finalConfirmation =
                    MessageBox.Show(
                        "هل أنت متأكد تمامًا؟\n\n" +
                        "لن يمكن التراجع عن الحذف من داخل البرنامج.\n" +
                        "يمكنك استعادة النسخة الاحتياطية عند الحاجة.",
                        "التأكيد النهائي",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Stop,
                        MessageBoxResult.No);

                if (finalConfirmation != MessageBoxResult.Yes)
                    return;

                ClearTestDataButton.IsEnabled = false;

                // تنفيذ عملية التنظيف
                string result = databaseManager.ClearTestData();

                MessageBox.Show(
                    result,
                    "O CORE - إعادة ضبط البيانات",
                    MessageBoxButton.OK,
                    result.StartsWith("تم تنظيف")
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Error);

                if (result.StartsWith("تم تنظيف"))
                {
                    MessageBox.Show(
                        "أصبحت قاعدة المخزون جاهزة لإضافة دليل الأدوية الجديد باللغة الإنجليزية.",
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء إعادة ضبط البيانات:\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ClearTestDataButton.IsEnabled = true;
            }
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
