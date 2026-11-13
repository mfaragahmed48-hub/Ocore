
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using Ocore.desktop.Services;
using Ocore.desktop.Services.Printing;
using Ocore.desktop.Models;

namespace Ocore.desktop.Views
{
    public partial class DashboardWindow : Window
    {
        private readonly int currentUserId;
        private readonly string currentRole;
        private readonly PermissionService permissionService;
        private readonly ProductService productService;

        public DashboardWindow(
            int userId,
            string fullName,
            string role)
        {
            InitializeComponent();

            currentUserId = userId;
            currentRole = role;

            permissionService = new PermissionService();
            productService = new ProductService();

            // تحديث نواقص المخزون عند العودة إلى لوحة التحكم
            Activated += DashboardWindow_Activated;

            UserNameText.Text = fullName;
            UserRoleText.Text = GetRoleDisplayName(role);

            ManagerAccountButton.Visibility =
                currentRole == "Manager"
                    ? Visibility.Visible
                    : Visibility.Collapsed;

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

            ProductsManagementButton.Visibility =
                permissionService.HasPermission(currentRole, "Products.View")
                    ? Visibility.Visible
                    : Visibility.Collapsed;

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

            RefreshLowStockSummary();
        }

        // ==========================================
        // تحديث لوحة التحكم عند العودة إليها
        // ==========================================

        private void DashboardWindow_Activated(
            object sender,
            EventArgs e)
        {
            RefreshLowStockSummary();
        }

        // ==========================================
        // نواقص المخزون
        // ==========================================

        private System.Collections.Generic.List<Product> GetLowStockProducts()
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Products.View"))
            {
                return new System.Collections.Generic.List<Product>();
            }

            return productService.GetProductsForManagement(currentUserId)
                .Where(p =>
                    !p.IsArchived &&
                    p.MinimumStockLevel > 0 &&
                    p.Quantity <= p.MinimumStockLevel)
                .OrderBy(p => p.Name)
                .ToList();
        }

        private void RefreshLowStockSummary()
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Products.View"))
            {
                LowStockCountText.Text = "—";
                return;
            }

            try
            {
                var products = GetLowStockProducts();

                LowStockCountText.Text =
                    products.Count.ToString();
            }
            catch
            {
                LowStockCountText.Text = "!";
            }
        }

        private void LowStockCard_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Products.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية عرض نواقص المخزون.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var products = GetLowStockProducts();

                Window window = new Window
                {
                    Title = "نواقص المخزون",
                    Width = 850,
                    Height = 520,
                    MinWidth = 650,
                    MinHeight = 350,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    FlowDirection = FlowDirection.RightToLeft,
                    Background = new SolidColorBrush(
                        Color.FromRgb(243, 246, 248))
                };

                Grid root = new Grid
                {
                    Margin = new Thickness(15)
                };

                root.RowDefinitions.Add(
                    new RowDefinition
                    {
                        Height = GridLength.Auto
                    });

                root.RowDefinitions.Add(
                    new RowDefinition
                    {
                        Height = new GridLength(
                            1,
                            GridUnitType.Star)
                    });

                root.RowDefinitions.Add(
                    new RowDefinition
                    {
                        Height = GridLength.Auto
                    });

                TextBlock title = new TextBlock
                {
                    Text = "الأصناف التي وصلت إلى الحد الأدنى للمخزون",
                    FontSize = 19,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(24, 63, 54)),
                    Margin = new Thickness(0, 0, 0, 12)
                };

                Grid.SetRow(title, 0);
                root.Children.Add(title);

                DataGrid dataGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    IsReadOnly = true,
                    CanUserAddRows = false,
                    CanUserDeleteRows = false,
                    SelectionMode = DataGridSelectionMode.Single,
                    SelectionUnit = DataGridSelectionUnit.FullRow,
                    ItemsSource = products,
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(
                        Color.FromRgb(220, 228, 225)),
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    RowHeight = 34,
                    FontSize = 13
                };

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "كود الصنف",
                    Binding = new Binding("ProductCode"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                });

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "اسم الصنف",
                    Binding = new Binding("Name"),
                    Width = new DataGridLength(2, DataGridLengthUnitType.Star)
                });

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "الرصيد الصالح للبيع",
                    Binding = new Binding("Quantity")
                    {
                        StringFormat = "N3"
                    },
                    Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
                });

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "الحد الأدنى",
                    Binding = new Binding("MinimumStockLevel")
                    {
                        StringFormat = "N3"
                    },
                    Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
                });

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "الوحدة",
                    Binding = new Binding("SmallUnitName"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                });

                Grid.SetRow(dataGrid, 1);
                root.Children.Add(dataGrid);

                TextBlock footer = new TextBlock
                {
                    Text = "عدد الأصناف التي تحتاج إلى مراجعة: "
                           + products.Count,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(183, 121, 31)),
                    Margin = new Thickness(0, 12, 0, 0)
                };

                Grid.SetRow(footer, 2);
                root.Children.Add(footer);

                window.Content = root;

                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء عرض نواقص المخزون:\n\n"
                    + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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
