
using System;
using System.Windows;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class DashboardWindow
    {
        private bool expiryAlertsInitialized;

        private void ExpiryAlertsCard_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Inventory.View"))
            {
                ExpiryAlertsCard.Visibility = Visibility.Collapsed;
                return;
            }

            ExpiryAlertsCard.Visibility = Visibility.Visible;

            if (!expiryAlertsInitialized)
            {
                Activated += DashboardWindow_ExpiryAlerts_Activated;
                expiryAlertsInitialized = true;
            }

            RefreshExpiryAlertsSummary();
        }

        private void DashboardWindow_ExpiryAlerts_Activated(
            object sender,
            EventArgs e)
        {
            RefreshExpiryAlertsSummary();
        }

        private void RefreshExpiryAlertsSummary()
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Inventory.View"))
            {
                ExpiryAlertsCard.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var service = new ExpiryAlertService();

                var alerts = service.GetExpiryAlerts(currentUserId);

                ExpiryAlertsCountText.Text =
                    alerts.Count.ToString("N0");
            }
            catch (Exception ex)
            {
                ExpiryAlertsCountText.Text = "!";

                System.Diagnostics.Debug.WriteLine(
                    "RefreshExpiryAlertsSummary error: " + ex);
            }
        }

        private void ExpiryAlertsCard_MouseLeftButtonUp(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Inventory.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية عرض تنبيهات صلاحية المخزون.",
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var window = new ExpiryAlertsWindow(currentUserId)
                {
                    Owner = this
                };

                window.ShowDialog();

                RefreshExpiryAlertsSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح تنبيهات الصلاحية:\n\n" +
                    ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
