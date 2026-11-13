
using System.Windows;
using System.Windows.Input;
using Ocore.desktop.Services;
using Ocore.desktop.Views;

namespace Ocore.desktop
{
    public partial class MainWindow : Window
    {
        private readonly UserService userService;

        public MainWindow()
        {
            InitializeComponent();

            userService = new UserService();

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (!userService.HasManager())
            {
                var firstRun = new FirstRunWindow();

                bool? result = firstRun.ShowDialog();

                if (result != true)
                {
                    Close();
                    return;
                }
            }
        }

        // الانتقال من اسم المستخدم إلى كلمة المرور بزر Enter
        private void UsernameBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PasswordBox.Focus();
                e.Handled = true;
            }
        }

        // تنفيذ تسجيل الدخول عند الضغط على Enter في كلمة المرور
        private void PasswordBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                LoginButton_Click(sender, e);
                e.Handled = true;
            }
        }

        private void LoginButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username))
            {
                StatusText.Text = "من فضلك أدخل اسم المستخدم.";
                UsernameBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                StatusText.Text = "من فضلك أدخل كلمة المرور.";
                PasswordBox.Focus();
                return;
            }

            try
            {
                var user = userService.GetAuthenticatedUser(
                    username,
                    password);

                if (user != null)
                {
                    var dashboard = new DashboardWindow(
                        user.Id,
                        user.DisplayName,
                        user.Role);

                    dashboard.Show();
                    Close();
                }
                else
                {
                    StatusText.Text =
                        "اسم المستخدم أو كلمة المرور غير صحيحة.";

                    PasswordBox.Clear();
                    PasswordBox.Focus();
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تسجيل الدخول: " + ex.Message);
            }
        }
    }
}
