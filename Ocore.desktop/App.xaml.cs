using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ocore.desktop.Data;

namespace Ocore.desktop
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            ErrorDialog.Show("خطأ غير متوقع في البرنامج", e.Exception);
            e.Handled = true;
        }

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
                ErrorDialog.Show(
                    "تعذر بدء تشغيل البرنامج أو استعادة البيانات",
                    ex);

                Shutdown();
            }
        }
    }

    public static class ErrorDialog
    {
        public static void Show(string context, Exception exception)
        {
            string details =
                "O CORE - تفاصيل الخطأ\r\n" +
                "العملية: " + context + "\r\n" +
                "التاريخ والوقت: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n\r\n" +
                (exception == null ? "لا توجد تفاصيل إضافية." : exception.ToString());

            Show(context, details);
        }

        public static void Show(string context, string details)
        {
            var window = new Window
            {
                Title = "O CORE - حدث خطأ",
                Width = 720,
                Height = 470,
                MinWidth = 520,
                MinHeight = 320,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                FlowDirection = FlowDirection.RightToLeft,
                ResizeMode = ResizeMode.CanResize,
                Background = Brushes.White,
                ShowInTaskbar = true
            };

            var root = new DockPanel
            {
                Margin = new Thickness(18),
                LastChildFill = true
            };

            var header = new TextBlock
            {
                Text = "حدث خطأ أثناء تنفيذ: " + context,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkRed,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var copyButton = new Button
            {
                Content = "نسخ الخطأ",
                MinWidth = 120,
                Height = 36,
                Padding = new Thickness(14, 4, 14, 4),
                FontWeight = FontWeights.Bold
            };

            copyButton.Click += (sender, args) =>
            {
                try
                {
                    Clipboard.SetText(details ?? string.Empty);
                    copyButton.Content = "تم النسخ";
                }
                catch
                {
                    copyButton.Content = "تعذر النسخ";
                }
            };

            var closeButton = new Button
            {
                Content = "إغلاق",
                MinWidth = 90,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(14, 4, 14, 4)
            };
            closeButton.Click += (sender, args) => window.Close();

            footer.Children.Add(copyButton);
            footer.Children.Add(closeButton);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var detailsBox = new TextBox
            {
                Text = details ?? string.Empty,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(248, 248, 248))
            };

            root.Children.Add(detailsBox);
            window.Content = root;
            window.ShowDialog();
        }
    }
}