
using System;
using System.Windows;

namespace Ocore.desktop.Views
{
    public partial class DashboardWindow
    {
        private void ExpensesButton_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            bool canUseExpenses =
                string.Equals(
                    currentRole,
                    "Manager",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    currentRole,
                    "Pharmacist",
                    StringComparison.OrdinalIgnoreCase);

            ExpensesButton.Visibility =
                canUseExpenses
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void ExpensesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!string.Equals(
                currentRole,
                "Manager",
                StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    currentRole,
                    "Pharmacist",
                    StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية الدخول إلى شاشة المصروفات.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var window = new ExpensesWindow(
                    currentUserId,
                    currentRole);

                OpenIndependentWindow(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح شاشة المصروفات:\n\n"
                    + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
