
using System;
using System.Windows;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class ManagerAccountWindow : Window
    {
        private readonly int managerUserId;
        private readonly UserService userService;

        public ManagerAccountWindow(int userId)
        {
            InitializeComponent();

            managerUserId = userId;
            userService = new UserService();

            User manager = userService.GetAllUsers().Find(
                u => u.Id == managerUserId &&
                     u.Role == "Manager");

            if (manager == null)
            {
                MessageBox.Show(
                    "لا يمكن العثور على حساب المدير.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
                return;
            }

            UsernameTextBox.Text = manager.Username;
            FullNameTextBox.Text = manager.FullName;
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string currentPassword = CurrentPasswordBox.Password;
            string newPassword = NewPasswordBox.Password;
            string confirmPassword = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                MessageBox.Show(
                    "يرجى إدخال اسم الدخول.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(FullNameTextBox.Text))
            {
                MessageBox.Show(
                    "يرجى إدخال الاسم الظاهر.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(currentPassword))
            {
                MessageBox.Show(
                    "يرجى إدخال كلمة المرور الحالية لتأكيد هويتك.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                MessageBox.Show(
                    "يرجى إدخال كلمة المرور الجديدة.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!string.Equals(
                newPassword,
                confirmPassword,
                StringComparison.Ordinal))
            {
                MessageBox.Show(
                    "كلمتا المرور الجديدتان غير متطابقتين.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                bool updated = userService.UpdateManagerAccount(
                    managerUserId,
                    currentPassword,
                    UsernameTextBox.Text,
                    FullNameTextBox.Text,
                    newPassword);

                if (updated)
                {
                    MessageBox.Show(
                        "تم تحديث بيانات حساب المدير بنجاح.\n"
                        + "استخدم بيانات الدخول الجديدة في المرة القادمة.",
                        "نجاح",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    Close();
                }
                else
                {
                    MessageBox.Show(
                        "لم يتم تحديث بيانات الحساب.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "خطأ أثناء تحديث الحساب",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
