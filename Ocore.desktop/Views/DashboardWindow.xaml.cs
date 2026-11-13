
using System;
using System.Windows;
using Ocore.desktop.Services;
using Ocore.desktop.Services.Printing;

namespace Ocore.desktop.Views
{
    public partial class DashboardWindow : Window
    {
        private readonly int currentUserId;
        private readonly string currentRole;
        private readonly PermissionService permissionService;

        public DashboardWindow(
            int userId,
            string fullName,
            string role)
        {
            InitializeComponent();

            currentUserId = userId;
            currentRole = role;

            permissionService = new PermissionService();

            UserNameText.Text = fullName;
            UserRoleText.Text = GetRoleDisplayName(role);

            // بيانات حسابي: للمدير فقط
            ManagerAccountButton.Visibility =
                currentRole == "Manager"
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // النسخ الاحتياطي والاستعادة: للمدير فقط
            BackupManagementButton.Visibility =
                currentRole == "Manager"
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            UsersManagementButton.Visibility =
                CanManageUsers()
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PermissionsManagementButton.Visibility =
                currentRole == "Manager"
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            AuditLogsButton.Visibility =
                CanViewAuditLogs()
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // إدارة الأصناف
            ProductsManagementButton.Visibility =
                permissionService.HasPermission(currentRole, "Products.View")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // طباعة الباركود: تحتاج صلاحية عرض الأصناف
            BarcodePrintingButton.Visibility =
                permissionService.HasPermission(currentRole, "Products.View")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PurchasesButton.Visibility =
                permissionService.HasPermission(currentRole, "Purchases.View")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PurchaseInvoicesButton.Visibility =
                permissionService.HasPermission(currentRole, "Purchases.View")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            SalesInvoicesButton.Visibility =
                permissionService.HasPermission(currentRole, "Sales.View")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            SuppliersManagementButton.Visibility =
                permissionService.HasPermission(currentRole, "Purchases.View")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            // إدارة العملاء
            CustomersManagementButton.Visibility =
                permissionService.HasPermission(currentRole, "Customers.View")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            SettingsButton.Visibility =
                permissionService.HasPermission(currentRole, "Settings.Edit")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            SalesReportsButton.Visibility =
                permissionService.HasPermission(currentRole, "Reports.View")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            SalesReturnsButton.Visibility =
                permissionService.HasPermission(currentRole, "Sales.Return")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            SalesReturnsHistoryButton.Visibility =
                permissionService.HasPermission(currentRole, "Sales.Return")
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        // ==========================================
        // فتح نافذة مستقلة دون تعطيل لوحة التحكم
        // ==========================================

        private void OpenIndependentWindow(Window window)
        {
            window.Owner = this;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            window.Show();
            window.Activate();
        }

        private string GetRoleDisplayName(string role)
        {
            switch (role)
            {
                case "Manager":
                    return "مدير النظام";

                case "Pharmacist":
                    return "صيدلي";

                case "Cashier":
                    return "كاشير";

                default:
                    return "مستخدم";
            }
        }

        private bool CanManageUsers()
        {
            return permissionService.CanManageUsers(currentRole);
        }

        private bool CanViewAuditLogs()
        {
            return permissionService.HasPermission(
                currentRole,
                "Audit.View");
        }

        // ==========================================
        // بيانات حساب المدير
        // ==========================================

        private void ManagerAccountButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (currentRole != "Manager")
            {
                MessageBox.Show(
                    "هذه الشاشة مخصصة لمدير النظام فقط.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var window = new ManagerAccountWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // النسخ الاحتياطي والاستعادة
        // ==========================================

        private void BackupManagementButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (currentRole != "Manager")
            {
                MessageBox.Show(
                    "هذه الشاشة مخصصة لمدير النظام فقط.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var window = new BackupManagementWindow();

                OpenIndependentWindow(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح شاشة النسخ الاحتياطي والاستعادة:\n\n"
                    + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================================
        // إدارة المستخدمين
        // ==========================================

        private void UsersManagementButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!CanManageUsers())
            {
                MessageBox.Show(
                    "ليس لديك صلاحية إدارة المستخدمين.");
                return;
            }

            var window = new UsersManagementWindow(
                currentUserId,
                currentRole);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // إدارة الصلاحيات
        // ==========================================

        private void PermissionsManagementButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (currentRole != "Manager")
            {
                MessageBox.Show(
                    "ليس لديك صلاحية إدارة الصلاحيات.");
                return;
            }

            var window = new PermissionsManagementWindow();

            OpenIndependentWindow(window);
        }

        // ==========================================
        // سجل النشاطات
        // ==========================================

        private void AuditLogsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!CanViewAuditLogs())
            {
                MessageBox.Show(
                    "ليس لديك صلاحية عرض سجل النشاطات.");
                return;
            }

            var window = new AuditLogsWindow(
                currentUserId,
                currentRole);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // المبيعات
        // ==========================================

        private void SalesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Sales.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية الدخول إلى شاشة المبيعات.");
                return;
            }

            var window = new SalesWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // سجل فواتير البيع
        // ==========================================

        private void SalesInvoicesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Sales.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية عرض سجل فواتير البيع.");
                return;
            }

            var window = new SalesInvoicesWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // مرتجعات البيع
        // ==========================================

        private void SalesReturnsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Sales.Return"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية تنفيذ مرتجعات البيع.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var window = new SalesReturnsWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // سجل مرتجعات البيع
        // ==========================================

        private void SalesReturnsHistoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Sales.Return"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية عرض سجل مرتجعات البيع.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var window =
                new SalesReturnsHistoryWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // تقارير المبيعات
        // ==========================================

        private void SalesReportsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Reports.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية عرض تقارير المبيعات.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            var window = new SalesReportsWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // إدارة الأصناف والمخزون
        // ==========================================

        private void ProductsManagementButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Products.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية الدخول إلى شاشة إدارة الأصناف.");
                return;
            }

            var window =
                new ProductsManagementWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // طباعة الباركود
        // ==========================================

        private void BarcodePrintingButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Products.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية الدخول إلى شاشة طباعة الباركود.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var window = new BarcodePrintingWindow(currentUserId);

                OpenIndependentWindow(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح شاشة طباعة الباركود:\n\n"
                    + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================================
        // فاتورة شراء جديدة
        // ==========================================

        private void PurchasesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Purchases.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية الدخول إلى شاشة المشتريات.");
                return;
            }

            var window = new PurchaseWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // سجل فواتير الشراء
        // ==========================================

        private void PurchaseInvoicesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Purchases.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية عرض سجل فواتير الشراء.");
                return;
            }

            var window =
                new PurchaseInvoicesWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // إدارة الموردين
        // ==========================================

        private void SuppliersManagementButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Purchases.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية الدخول إلى شاشة إدارة الموردين.");
                return;
            }

            var window =
                new SuppliersManagementWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // إدارة العملاء
        // ==========================================

        private void CustomersManagementButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Customers.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية الدخول إلى شاشة إدارة العملاء.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var window =
                    new CustomersManagementWindow(currentUserId);

                OpenIndependentWindow(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح شاشة إدارة العملاء:\n\n"
                    + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================================
        // إعدادات الصيدلية
        // ==========================================

        private void SettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Settings.Edit"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية تعديل إعدادات الصيدلية.");
                return;
            }

            var window = new SettingsWindow(currentUserId);

            OpenIndependentWindow(window);
        }

        // ==========================================
        // إعدادات الطابعات الجديدة
        // ==========================================

        private void PrinterSettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Settings.Edit"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية تعديل إعدادات الطابعات.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            PrinterSettingsNavigationService.Open(this);
        }

        // ==========================================
        // تسجيل الخروج
        // ==========================================

        private void LogoutButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "هل تريد تسجيل الخروج؟\n"
                + "سيتم إغلاق النوافذ المفتوحة.",
                "تسجيل الخروج",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            MainWindow loginWindow = new MainWindow();

            loginWindow.Show();

            Close();
        }
    }
}
