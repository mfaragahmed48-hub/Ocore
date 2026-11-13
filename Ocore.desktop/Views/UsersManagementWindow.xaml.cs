
using System;
using System.Windows;
using System.Windows.Controls;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class UsersManagementWindow : Window
    {
        private readonly UserService userService;
        private readonly PermissionService permissionService;

        private readonly int currentUserId;
        private readonly string currentRole;

        public UsersManagementWindow(int userId, string role)
        {
            InitializeComponent();

            currentUserId = userId;
            currentRole = role;

            userService = new UserService();
            permissionService = new PermissionService();

            if (!CanManageUsers())
            {
                MessageBox.Show(
                    "ليس لديك صلاحية إدارة المستخدمين.");

                Loaded += (s, e) => Close();
                return;
            }

            LoadUsers();
        }

        private bool CanManageUsers()
        {
            return permissionService.CanManageUsers(currentRole);
        }

        private void LoadUsers()
        {
            var users = userService.GetAllUsers();

            foreach (var user in users)
            {
                user.StatusText =
                    user.IsActive ? "نشط" : "موقوف";
            }

            UsersDataGrid.ItemsSource = null;
            UsersDataGrid.ItemsSource = users;
        }

        private void EditUser_Click(
            object sender,
            RoutedEventArgs e)
        {
            var button = sender as Button;

            var user = button?.DataContext
                as Ocore.desktop.Models.User;

            if (user == null)
                return;

            if (!CanManageUsers())
            {
                MessageBox.Show("ليس لديك صلاحية.");
                return;
            }

            if (user.Role == "Manager")
            {
                MessageBox.Show("لا يمكن تعديل حساب المدير.");
                return;
            }

            var editWindow = new EditUserWindow(user);
            editWindow.Owner = this;

            if (editWindow.ShowDialog() == true)
            {
                try
                {
                    userService.UpdateUser(
                        currentUserId,
                        user.Id,
                        editWindow.UpdatedFullName,
                        editWindow.UpdatedRole);

                    LoadUsers();

                    MessageBox.Show(
                        "تم تعديل بيانات المستخدم بنجاح.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "تعذر تعديل المستخدم: " + ex.Message);
                }
            }
        }

        private void ChangeUserStatus_Click(
            object sender,
            RoutedEventArgs e)
        {
            var button = sender as Button;

            var user = button?.DataContext
                as Ocore.desktop.Models.User;

            if (user == null)
                return;

            if (!CanManageUsers())
            {
                MessageBox.Show("ليس لديك صلاحية.");
                return;
            }

            if (user.Role == "Manager")
            {
                MessageBox.Show(
                    "لا يمكن تغيير حالة حساب المدير من هذه الشاشة.");
                return;
            }

            if (user.Id == currentUserId)
            {
                MessageBox.Show(
                    "لا يمكنك إيقاف حسابك الحالي.");
                return;
            }

            bool newStatus = !user.IsActive;

            string message = newStatus
                ? "هل تريد إعادة تفعيل هذا الحساب؟"
                : "هل تريد إيقاف هذا الحساب؟";

            if (MessageBox.Show(
                message,
                "تأكيد العملية",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
                != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                userService.SetUserActive(
                    currentUserId,
                    user.Id,
                    newStatus);

                LoadUsers();

                MessageBox.Show("تم تغيير حالة المستخدم بنجاح.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تغيير حالة المستخدم: " + ex.Message);
            }
        }

        private void ResetPassword_Click(
            object sender,
            RoutedEventArgs e)
        {
            var button = sender as Button;

            var user = button?.DataContext
                as Ocore.desktop.Models.User;

            if (user == null)
                return;

            if (!CanManageUsers())
            {
                MessageBox.Show("ليس لديك صلاحية.");
                return;
            }

            if (user.Role == "Manager")
            {
                MessageBox.Show(
                    "لا يمكن تغيير كلمة مرور حساب المدير من هذه الشاشة.");
                return;
            }

            if (user.Id == currentUserId)
            {
                MessageBox.Show(
                    "لا يمكنك تغيير كلمة مرور حسابك من هذه الشاشة.");
                return;
            }

            var passwordWindow = new Window
            {
                Title = "إعادة تعيين كلمة المرور",
                Width = 400,
                Height = 280,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                FlowDirection = FlowDirection.RightToLeft,
                Background = System.Windows.Media.Brushes.White
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(20)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "المستخدم: " + user.Username,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 15)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "كلمة المرور الجديدة"
            });

            var newPasswordBox = new PasswordBox
            {
                Height = 35,
                Margin = new Thickness(0, 5, 0, 12)
            };

            panel.Children.Add(newPasswordBox);

            panel.Children.Add(new TextBlock
            {
                Text = "تأكيد كلمة المرور"
            });

            var confirmPasswordBox = new PasswordBox
            {
                Height = 35,
                Margin = new Thickness(0, 5, 0, 15)
            };

            panel.Children.Add(confirmPasswordBox);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var saveButton = new Button
            {
                Content = "حفظ كلمة المرور",
                Width = 140,
                Height = 38,
                Margin = new Thickness(5),
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(22, 134, 93)),
                Foreground = System.Windows.Media.Brushes.White
            };

            var cancelButton = new Button
            {
                Content = "إلغاء",
                Width = 90,
                Height = 38,
                Margin = new Thickness(5)
            };

            saveButton.Click += (s, args) =>
            {
                string newPassword = newPasswordBox.Password;
                string confirmPassword = confirmPasswordBox.Password;

                if (string.IsNullOrWhiteSpace(newPassword))
                {
                    MessageBox.Show("أدخل كلمة المرور الجديدة.");
                    return;
                }

                if (newPassword.Length < 10)
                {
                    MessageBox.Show(
                        "كلمة المرور يجب ألا تقل عن 10 أحرف.");
                    return;
                }

                if (newPassword != confirmPassword)
                {
                    MessageBox.Show(
                        "كلمتا المرور غير متطابقتين.");
                    return;
                }

                if (MessageBox.Show(
                    "هل أنت متأكد من تغيير كلمة مرور المستخدم؟",
                    "تأكيد العملية",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                try
                {
                    userService.ResetUserPassword(
                        currentUserId,
                        user.Id,
                        newPassword);

                    MessageBox.Show(
                        "تم تغيير كلمة المرور بنجاح.");

                    passwordWindow.DialogResult = true;
                    passwordWindow.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "تعذر تغيير كلمة المرور: " + ex.Message);
                }
            };

            cancelButton.Click += (s, args) =>
            {
                passwordWindow.DialogResult = false;
                passwordWindow.Close();
            };

            buttonsPanel.Children.Add(saveButton);
            buttonsPanel.Children.Add(cancelButton);

            panel.Children.Add(buttonsPanel);

            passwordWindow.Content = panel;

            passwordWindow.ShowDialog();
        }

        private void AddUser_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!CanManageUsers())
            {
                MessageBox.Show("ليس لديك صلاحية.");
                return;
            }

            string fullName = FullNameBox.Text.Trim();
            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Password;

            var selectedRole =
                RoleComboBox.SelectedItem as ComboBoxItem;

            if (selectedRole == null)
            {
                MessageBox.Show("اختر نوع المستخدم.");
                return;
            }

            string role = selectedRole.Tag.ToString();

            try
            {
                bool created = userService.CreateUser(
                    currentUserId,
                    username,
                    password,
                    fullName,
                    role);

                if (created)
                {
                    MessageBox.Show(
                        "تم إنشاء المستخدم بنجاح.");

                    FullNameBox.Clear();
                    UsernameBox.Clear();
                    PasswordBox.Clear();

                    RoleComboBox.SelectedIndex = 0;

                    LoadUsers();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر إنشاء المستخدم: " + ex.Message);
            }
        }
    }
}
