
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class PermissionsManagementWindow : Window
    {
        private readonly PermissionService permissionService;
        private readonly List<CheckBox> permissionCheckBoxes;

        public PermissionsManagementWindow()
        {
            InitializeComponent();

            permissionService = new PermissionService();
            permissionCheckBoxes = new List<CheckBox>();

            RoleComboBox.SelectionChanged += RoleComboBox_SelectionChanged;

            LoadRolePermissions();
        }

        // معرفة الدور المختار
        private string GetSelectedRole()
        {
            ComboBoxItem selectedItem =
                RoleComboBox.SelectedItem as ComboBoxItem;

            if (selectedItem == null)
                return null;

            return selectedItem.Tag?.ToString();
        }

        // تحميل صلاحيات الدور
        private void LoadRolePermissions()
        {
            string role = GetSelectedRole();

            if (string.IsNullOrWhiteSpace(role))
                return;

            PermissionsPanel.Children.Clear();
            permissionCheckBoxes.Clear();

            var allPermissions =
                permissionService.GetAllPermissions();

            var allowedPermissions =
                new HashSet<string>(
                    permissionService.GetPermissions(role),
                    StringComparer.OrdinalIgnoreCase);

            var groups = new Dictionary<string, StackPanel>();

            foreach (PermissionItem permission in allPermissions)
            {
                if (!groups.ContainsKey(permission.PermissionGroup))
                {
                    StackPanel groupPanel = new StackPanel();

                    GroupBox groupBox = new GroupBox
                    {
                        Header = permission.PermissionGroup,
                        Content = groupPanel,
                        Margin = new Thickness(0, 0, 0, 12),
                        Padding = new Thickness(10),
                        FontSize = 15,
                        FontWeight = FontWeights.Bold
                    };

                    groups.Add(permission.PermissionGroup, groupPanel);

                    PermissionsPanel.Children.Add(groupBox);
                }

                CheckBox checkBox = new CheckBox
                {
                    Content = permission.PermissionName,
                    Tag = permission.PermissionCode,
                    IsChecked = allowedPermissions.Contains(
                        permission.PermissionCode),
                    FontSize = 14,
                    Margin = new Thickness(5, 7, 5, 7)
                };

                groups[permission.PermissionGroup].Children.Add(checkBox);

                permissionCheckBoxes.Add(checkBox);
            }
        }

        // تغيير الدور
        private void RoleComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            LoadRolePermissions();
        }

        // حفظ الصلاحيات
        private void SavePermissions_Click(
            object sender,
            RoutedEventArgs e)
        {
            string role = GetSelectedRole();

            if (string.IsNullOrWhiteSpace(role))
            {
                MessageBox.Show("اختر الدور أولاً.");
                return;
            }

            var allowedPermissions = new List<string>();

            foreach (CheckBox checkBox in permissionCheckBoxes)
            {
                if (checkBox.IsChecked == true)
                {
                    allowedPermissions.Add(
                        checkBox.Tag.ToString());
                }
            }

            try
            {
                permissionService.SaveRolePermissions(
                    role,
                    allowedPermissions);

                MessageBox.Show(
                    "تم حفظ الصلاحيات بنجاح.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر حفظ الصلاحيات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // إغلاق النافذة
        private void Close_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
