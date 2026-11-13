
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Ocore.desktop.Data;

namespace Ocore.desktop.Views
{
    public partial class BackupManagementWindow : Window
    {
        private readonly DatabaseBackupService backupService;
        private bool restorePrepared = false;

        public BackupManagementWindow()
        {
            InitializeComponent();

            backupService = new DatabaseBackupService();

            RestoreBackupButton.IsEnabled = false;

            RefreshBackupsButton.Click += RefreshBackupsButton_Click;
            RestoreBackupButton.Click += RestoreBackupButton_Click;
            ExportDataButton.Click += ExportDataButton_Click;
            ImportDataButton.Click += ImportDataButton_Click;
            CloseButton.Click += CloseButton_Click;

            BackupsGrid.SelectionChanged += BackupsGrid_SelectionChanged;
            BackupsGrid.PreviewMouseLeftButtonDown +=
                BackupsGrid_PreviewMouseLeftButtonDown;

            LoadBackups();
        }

        private void LoadBackups()
        {
            try
            {
                BackupsGrid.UnselectAll();
                BackupsGrid.SelectedItem = null;
                RestoreBackupButton.IsEnabled = false;

                var backups = backupService.GetAvailableBackups();

                var backupItems = backups
                    .Select(path =>
                    {
                        var fileInfo = new FileInfo(path);

                        string backupType;

                        if (fileInfo.Name.StartsWith(
                            "OCORE_Daily_",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            backupType = "نسخة يومية";
                        }
                        else if (fileInfo.Name.StartsWith(
                            "OCORE_Backup_Before_Update_",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            backupType = "قبل تحديث";
                        }
                        else if (fileInfo.Name.StartsWith(
                            "OCORE_Before_Restore_",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            backupType = "نسخة أمان";
                        }
                        else
                        {
                            backupType = "نسخة احتياطية";
                        }

                        return new
                        {
                            FilePath = path,
                            FileName = fileInfo.Name,
                            CreationDate = fileInfo.LastWriteTime.ToString(
                                "yyyy/MM/dd  hh:mm tt"),
                            FileSize = FormatFileSize(fileInfo.Length),
                            BackupType = backupType
                        };
                    })
                    .OrderByDescending(
                        item => new FileInfo(item.FilePath).LastWriteTime)
                    .ToList();

                BackupsGrid.ItemsSource = backupItems;

                BackupStatusText.Text =
                    backupItems.Count == 0
                    ? "لا توجد نسخ احتياطية متاحة حاليًا."
                    : "عدد النسخ الاحتياطية المتاحة: " + backupItems.Count;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل النسخ الاحتياطية:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return bytes + " بايت";

            if (bytes < 1024 * 1024)
            {
                return (bytes / 1024.0).ToString("0.00") +
                       " كيلوبايت";
            }

            return (bytes / (1024.0 * 1024.0)).ToString("0.00") +
                   " ميجابايت";
        }

        private void BackupsGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            dynamic selectedBackup = BackupsGrid.SelectedItem;

            if (selectedBackup == null)
            {
                BackupStatusText.Text =
                    "اختر نسخة احتياطية من الجدول لعرض تفاصيلها.";

                RestoreBackupButton.IsEnabled = false;
                return;
            }

            RestoreBackupButton.IsEnabled = !restorePrepared;

            BackupStatusText.Text =
                "النسخة المحددة: " + selectedBackup.FileName;
        }

        // إلغاء التحديد عند الضغط خارج صفوف البيانات
        private void BackupsGrid_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            DependencyObject source =
                e.OriginalSource as DependencyObject;

            while (source != null && !(source is DataGridRow))
            {
                source = VisualTreeHelper.GetParent(source);
            }

            if (source == null)
            {
                BackupsGrid.UnselectAll();
                BackupsGrid.SelectedItem = null;
            }
        }

        private void RefreshBackupsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (restorePrepared)
                return;

            LoadBackups();
        }

        // تصدير قاعدة البيانات كاملة إلى مكان خارجي
        private void ExportDataButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (restorePrepared)
                return;

            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "تصدير قاعدة بيانات O CORE",
                    Filter = "SQLite Database (*.db)|*.db",
                    DefaultExt = ".db",
                    AddExtension = true,
                    FileName =
                        "OCORE_Export_" +
                        DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                        ".db",
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog(this) != true)
                    return;

                ExportDataButton.IsEnabled = false;

                string result = backupService.ExportDatabase(
                    dialog.FileName);

                bool succeeded = result.StartsWith(
                    "تم تصدير قاعدة البيانات كاملة بنجاح.",
                    StringComparison.OrdinalIgnoreCase);

                MessageBox.Show(
                    result,
                    succeeded
                        ? "O CORE - تم التصدير"
                        : "O CORE - تعذر التصدير",
                    MessageBoxButton.OK,
                    succeeded
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Error);

                if (succeeded)
                {
                    BackupStatusText.Text =
                        "تم تصدير قاعدة البيانات بنجاح إلى:\n" +
                        dialog.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تصدير البيانات:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ExportDataButton.IsEnabled = !restorePrepared;
            }
        }

        // استرداد قاعدة بيانات من ملف خارجي
        private void ImportDataButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (restorePrepared)
                return;

            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "اختيار قاعدة بيانات خارجية للاسترداد",
                    Filter = "SQLite Database (*.db)|*.db",
                    DefaultExt = ".db",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog(this) != true)
                    return;

                string selectedFile = dialog.FileName;

                MessageBoxResult confirmation = MessageBox.Show(
                    "أنت على وشك تجهيز استرداد قاعدة بيانات كاملة.\n\n" +
                    "الملف المختار:\n" +
                    Path.GetFileName(selectedFile) +
                    "\n\n" +
                    "سيتم استبدال البيانات الحالية بالكامل عند إغلاق " +
                    "البرنامج وفتحه مرة أخرى.\n\n" +
                    "سيتم إنشاء نسخة أمان من البيانات الحالية قبل الاستبدال.\n\n" +
                    "هل تريد المتابعة؟",
                    "تأكيد استرداد قاعدة بيانات خارجية",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                ImportDataButton.IsEnabled = false;

                string result =
                    backupService.PrepareRestoreExternal(selectedFile);

                bool succeeded = result.StartsWith(
                    "تم تجهيز قاعدة البيانات للاستعادة بنجاح.",
                    StringComparison.OrdinalIgnoreCase);

                MessageBox.Show(
                    result,
                    succeeded
                        ? "O CORE - تجهيز الاستعادة"
                        : "O CORE - تعذر تجهيز الاستعادة",
                    MessageBoxButton.OK,
                    succeeded
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Error);

                if (succeeded)
                {
                    LockRestoreActions();

                    BackupStatusText.Text =
                        "تم تجهيز الاستعادة الخارجية. " +
                        "أغلق البرنامج وافتحه مرة أخرى لإتمام العملية.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء استرداد قاعدة البيانات الخارجية:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ImportDataButton.IsEnabled = !restorePrepared;
            }
        }

        // استعادة نسخة من النسخ الداخلية
        private void RestoreBackupButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (restorePrepared)
                return;

            dynamic selectedBackup = BackupsGrid.SelectedItem;

            if (selectedBackup == null)
            {
                RestoreBackupButton.IsEnabled = false;

                MessageBox.Show(
                    "يرجى اختيار نسخة احتياطية أولاً.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string fileName = selectedBackup.FileName;

            MessageBoxResult confirmation = MessageBox.Show(
                "هل أنت متأكد من تجهيز استعادة النسخة التالية؟\n\n" +
                fileName +
                "\n\nلن تتغير قاعدة البيانات الحالية الآن، ولكن سيتم " +
                "استبدالها عند إغلاق البرنامج وفتحه مرة أخرى.",
                "تأكيد استعادة النسخة الاحتياطية",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                string result = backupService.PrepareRestore(
                    selectedBackup.FilePath);

                bool succeeded = result.StartsWith(
                    "تم تجهيز النسخة الاحتياطية بنجاح.",
                    StringComparison.OrdinalIgnoreCase);

                MessageBox.Show(
                    result,
                    succeeded
                        ? "O CORE - تجهيز الاستعادة"
                        : "O CORE - تعذر تجهيز الاستعادة",
                    MessageBoxButton.OK,
                    succeeded
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Error);

                if (succeeded)
                {
                    LockRestoreActions();

                    BackupStatusText.Text =
                        "تم تجهيز الاستعادة. أغلق البرنامج وافتحه مرة أخرى لإتمام العملية.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تجهيز استعادة النسخة الاحتياطية:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // قفل جميع عمليات البيانات بعد تجهيز الاستعادة
        private void LockRestoreActions()
        {
            restorePrepared = true;

            RestoreBackupButton.IsEnabled = false;
            RefreshBackupsButton.IsEnabled = false;
            ExportDataButton.IsEnabled = false;
            ImportDataButton.IsEnabled = false;
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
