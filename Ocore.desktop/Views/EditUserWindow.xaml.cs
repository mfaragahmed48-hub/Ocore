
using System.Windows;
using System.Windows.Controls;
using Ocore.desktop.Models;

namespace Ocore.desktop.Views
{
    public partial class EditUserWindow : Window
    {
        public string UpdatedFullName { get; private set; }
        public string UpdatedRole { get; private set; }

        public EditUserWindow(User user)
        {
            InitializeComponent();

            FullNameBox.Text = user.FullName;

            foreach (ComboBoxItem item in RoleComboBox.Items)
            {
                if (item.Tag.ToString() == user.Role)
                {
                    RoleComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string fullName = FullNameBox.Text.Trim();

            var selectedRole =
                RoleComboBox.SelectedItem as ComboBoxItem;

            if (string.IsNullOrWhiteSpace(fullName))
            {
                MessageBox.Show("أدخل الاسم الكامل.");
                return;
            }

            if (selectedRole == null)
            {
                MessageBox.Show("اختر نوع المستخدم.");
                return;
            }

            UpdatedFullName = fullName;
            UpdatedRole = selectedRole.Tag.ToString();

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
