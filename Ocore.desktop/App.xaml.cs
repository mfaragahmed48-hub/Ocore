
using System;
using System.Windows;
using Ocore.desktop.Data;

namespace Ocore.desktop
{
    public partial class App : Application
    {
        private void Application_Startup(
            object sender,
            StartupEventArgs e)
        {
            try
            {
                var backupService = new DatabaseBackupService();

                if (backupService.HasPendingRestore())
                {
                    string result = backupService.ApplyPendingRestore();

                    MessageBox.Show(
                        result,
                        "O CORE - استعادة قاعدة البيانات",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                MainWindow mainWindow = new MainWindow();

                this.MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                string errorDetails =
                    "تعذر إكمال عملية بدء تشغيل البرنامج أو استعادة البيانات.\n\n" +
                    "لم يتم فتح البرنامج حفاظًا على سلامة البيانات.\n\n" +
                    "تفاصيل الخطأ الكاملة:\n\n" +
                    ex.ToString();

                MessageBox.Show(
                    errorDetails,
                    "O CORE - تفاصيل خطأ بدء التشغيل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
            }
        }
    }
}
