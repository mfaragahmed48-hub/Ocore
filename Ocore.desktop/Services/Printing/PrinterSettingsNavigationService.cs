
using System;
using System.Windows;
using Ocore.desktop.Views;

namespace Ocore.desktop.Services.Printing
{
    public static class PrinterSettingsNavigationService
    {
        public static bool Open(Window owner)
        {
            try
            {
                var window = new PrinterSettingsHubWindow();

                if (owner != null)
                {
                    window.Owner = owner;
                    window.WindowStartupLocation =
                        WindowStartupLocation.CenterOwner;
                }

                window.ShowDialog();

                return window.HasSavedConfiguration;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر فتح إعدادات الطابعات:\n\n" + ex.Message,
                    "إعدادات الطابعات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return false;
            }
        }
    }
}
