
using System;
using System.Data.SQLite;
using System.Windows;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class FirstRunWindow : Window
    {
        public FirstRunWindow()
        {
            InitializeComponent();
        }

        private void CreateAdmin_Click(object sender, RoutedEventArgs e)
        {
            string fullName = FullNameBox.Text.Trim();
            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Password;
            string confirmPassword = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("يرجى إدخال جميع البيانات.");
                return;
            }

            if (password.Length < 10)
            {
                MessageBox.Show("كلمة المرور يجب ألا تقل عن 10 أحرف.");
                return;
            }

            if (password != confirmPassword)
            {
                MessageBox.Show("كلمتا المرور غير متطابقتين.");
                return;
            }

            try
            {
                var userService = new UserService();

                if (userService.HasManager())
                {
                    MessageBox.Show("تم إنشاء حساب المدير مسبقاً.");
                    return;
                }

                bool created = userService.CreateUser(
                    username,
                    password,
                    fullName,
                    "Manager");

                if (created)
                {
                    MessageBox.Show(
                        "تم إنشاء حساب المدير بنجاح.",
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    DialogResult = true;
                    Close();
                }
            }
            catch (SQLiteException)
            {
                MessageBox.Show(
                    "تعذر إنشاء الحساب. تحقق من اسم المستخدم، " +
                    "أو تأكد من عدم إنشاء حساب المدير مسبقاً.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("حدث خطأ: " + ex.Message);
            }
        }
    }
}
