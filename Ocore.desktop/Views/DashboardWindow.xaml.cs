
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
        private readonly ShiftService shiftService;

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
            shiftService = new ShiftService();

            CloseShiftButton.Click += CloseShiftButton_Click;
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

            // سجل الشيفتات والإغلاقات - المدير فقط
            ShiftHistoryButton.Visibility =
                string.Equals(
                    currentRole,
                    "Manager",
                    StringComparison.OrdinalIgnoreCase)
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

            RefreshProductCount();
            RefreshLowStockSummary();
            RefreshShiftSummary();
        }

        // ==========================================
        // تحديث لوحة التحكم عند العودة إليها
        // ==========================================

        private void DashboardWindow_Activated(
            object sender,
            EventArgs e)
        {
            RefreshProductCount();
            RefreshLowStockSummary();
            RefreshShiftSummary();
        }

        // ==========================================
        // عدد الأصناف النشطة
        // ==========================================

        private void RefreshProductCount()
        {
            if (!permissionService.HasPermission(
                currentRole,
                "Products.View"))
            {
                ProductCountText.Text = "—";
                return;
            }

            try
            {
                int count = productService.GetActiveProductCount(
                    currentUserId);

                ProductCountText.Text = count.ToString("N0");
            }
            catch (Exception ex)
            {
                ProductCountText.Text = "!";

                System.Diagnostics.Debug.WriteLine(
                    "RefreshProductCount error: " + ex);
            }
        }

        // ==========================================
        // الشيفت الحالي وإغلاقه
        // ==========================================

        private bool CanCloseShift()
        {
            return string.Equals(
                       currentRole,
                       "Pharmacist",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       currentRole,
                       "Manager",
                       StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshShiftSummary()
        {
            try
            {
                var shift = shiftService.GetCurrentShift();

                if (shift == null)
                {
                    ShiftStatusText.Text = "لا يوجد شيفت مفتوح";

                    ShiftStatusText.Foreground = new SolidColorBrush(
                        Color.FromRgb(100, 115, 111));

                    ShiftNumberText.Text = "—";
                    ShiftOpenedByText.Text = "—";
                    ShiftOpenedAtText.Text = "—";

                    CloseShiftButton.IsEnabled = false;

                    return;
                }

                ShiftStatusText.Text = "مفتوح";

                ShiftStatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(22, 134, 93));

                ShiftNumberText.Text =
                    string.IsNullOrWhiteSpace(shift.ShiftNumber)
                        ? shift.Id.ToString()
                        : shift.ShiftNumber;

                ShiftOpenedByText.Text =
                    string.IsNullOrWhiteSpace(shift.OpenedByName)
                        ? "غير محدد"
                        : shift.OpenedByName;

                DateTime openedAt;

                if (DateTime.TryParse(
                    shift.OpenedAt,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out openedAt))
                {
                    ShiftOpenedAtText.Text =
                        openedAt.ToString("yyyy/MM/dd hh:mm tt");
                }
                else
                {
                    ShiftOpenedAtText.Text =
                        shift.OpenedAt ?? "—";
                }

                CloseShiftButton.IsEnabled = CanCloseShift();
            }
            catch (Exception ex)
            {
                ShiftStatusText.Text = "تعذر تحميل الحالة";

                ShiftStatusText.Foreground = new SolidColorBrush(
                    Color.FromRgb(168, 50, 50));

                ShiftNumberText.Text = "—";
                ShiftOpenedByText.Text = "—";
                ShiftOpenedAtText.Text = "—";

                CloseShiftButton.IsEnabled = false;

                System.Diagnostics.Debug.WriteLine(
                    "RefreshShiftSummary error: " + ex);
            }
        }

        // ==========================================
        // إغلاق الشيفت بالمبالغ الفعلية
        // ==========================================

        private async void CloseShiftButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!CanCloseShift())
            {
                MessageBox.Show(
                    "إغلاق الشيفت متاح للصيادلة ومدير النظام فقط.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                var shift = shiftService.GetCurrentShift();

                if (shift == null)
                {
                    MessageBox.Show(
                        "لا يوجد شيفت مفتوح لإغلاقه.",
                        "إغلاق الشيفت",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    RefreshShiftSummary();
                    return;
                }

                var cashBox = CreateActualAmountTextBox();
                var cardBox = CreateActualAmountTextBox();
                var instaPayBox = CreateActualAmountTextBox();
                var transferBox = CreateActualAmountTextBox();

                var dialog = new Window
                {
                    Title = "إغلاق الشيفت - المبالغ الفعلية",
                    Width = 620,
                    Height = 510,
                    MinWidth = 560,
                    MinHeight = 470,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    FlowDirection = FlowDirection.RightToLeft,
                    Background = new SolidColorBrush(
                        Color.FromRgb(243, 246, 248))
                };

                var root = new StackPanel
                {
                    Margin = new Thickness(22)
                };

                root.Children.Add(new TextBlock
                {
                    Text = "أدخل المبالغ الموجودة فعليًا بكل وسيلة دفع",
                    FontSize = 19,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(24, 63, 54)),
                    Margin = new Thickness(0, 0, 0, 8)
                });

                root.Children.Add(new TextBlock
                {
                    Text = "أدخل الموجود الفعلي فقط لكل وسيلة دفع. اكتب 0 إذا لم يوجد رصيد.",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(100, 115, 111)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                });

                var table = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 12)
                };

                table.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = new GridLength(1.2, GridUnitType.Star)
                    });

                table.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = new GridLength(1, GridUnitType.Star)
                    });

                for (int i = 0; i < 5; i++)
                {
                    table.RowDefinitions.Add(
                        new RowDefinition
                        {
                            Height = GridLength.Auto
                        });
                }

                AddShiftTableCell(
                    table, "وسيلة الدفع", 0, 0, true);

                AddShiftTableCell(
                    table, "الموجود فعليًا", 0, 1, true);

                AddShiftActualPaymentRow(
                    table, 1, "النقدية", cashBox);

                AddShiftActualPaymentRow(
                    table, 2, "الفيزا", cardBox);

                AddShiftActualPaymentRow(
                    table, 3, "إنستا باي", instaPayBox);

                AddShiftActualPaymentRow(
                    table, 4, "التحويل البنكي", transferBox);

                root.Children.Add(table);

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                var confirmButton = new Button
                {
                    Content = "تأكيد إغلاق الشيفت",
                    Width = 175,
                    Height = 42,
                    Margin = new Thickness(0, 0, 10, 0),
                    Background = new SolidColorBrush(
                        Color.FromRgb(168, 50, 50)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    BorderThickness = new Thickness(0)
                };

                var cancelButton = new Button
                {
                    Content = "إلغاء",
                    Width = 105,
                    Height = 42,
                    Background = new SolidColorBrush(
                        Color.FromRgb(225, 231, 228)),
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(24, 63, 54)),
                    BorderThickness = new Thickness(0)
                };

                int closedShiftId = 0;

                confirmButton.Click += delegate
                {
                    double actualCash;
                    double actualCard;
                    double actualInstaPay;
                    double actualTransfer;

                    if (!TryReadActualAmount(
                            cashBox.Text, out actualCash) ||
                        !TryReadActualAmount(
                            cardBox.Text, out actualCard) ||
                        !TryReadActualAmount(
                            instaPayBox.Text, out actualInstaPay) ||
                        !TryReadActualAmount(
                            transferBox.Text, out actualTransfer))
                    {
                        MessageBox.Show(
                            "أدخل مبلغًا صحيحًا غير سالب في الخانات الأربع. استخدم 0 إذا لم يوجد مبلغ.",
                            "بيانات غير صحيحة",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    var confirm = MessageBox.Show(
                        "هل تريد تأكيد إغلاق الشيفت بالمبالغ الفعلية التي أدخلتها؟\n" +
                        "سيتم تسجيل اسمك ووقت الإغلاق والفروق لكل وسيلة دفع.",
                        "تأكيد إغلاق الشيفت",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirm != MessageBoxResult.Yes)
                        return;

                    try
                    {
                        bool closed =
                            shiftService.CloseCurrentShift(
                                currentUserId,
                                actualCash,
                                actualCard,
                                actualInstaPay,
                                actualTransfer);

                        if (!closed)
                        {
                            MessageBox.Show(
                                "لم يتم العثور على شيفت مفتوح لإغلاقه.",
                                "إغلاق الشيفت",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return;
                        }

                        closedShiftId = shift.Id;
                        dialog.DialogResult = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "تعذر إغلاق الشيفت:\n\n" + ex.Message,
                            "O CORE",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                };

                cancelButton.Click += delegate
                {
                    dialog.DialogResult = false;
                };

                buttons.Children.Add(confirmButton);
                buttons.Children.Add(cancelButton);
                root.Children.Add(buttons);

                dialog.Content = root;

                bool? result = dialog.ShowDialog();

                if (result == true)
                {
                    try
                    {
                        await new ShiftClosurePrintingService()
                            .PrintClosureAsync(closedShiftId);

                        MessageBox.Show(
                            "تم إغلاق الشيفت بنجاح وحفظ المبالغ الفعلية والفروق.",
                            "إغلاق الشيفت",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    catch (Exception printException)
                    {
                        MessageBox.Show(
                            "تم إغلاق الشيفت بنجاح وحفظ المبالغ الفعلية والفروق، " +
                            "لكن تعذرت طباعة تقرير الإغلاق على طابعة الكاشير.\n\n" +
                            printException.Message,
                            "تعذر طباعة تقرير الإغلاق",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }

                RefreshShiftSummary();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                RefreshShiftSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تجهيز شاشة إغلاق الشيفت:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                RefreshShiftSummary();
            }
        }

        // ==========================================
        // عناصر شاشة إغلاق الشيفت
        // ==========================================

        private TextBox CreateActualAmountTextBox()
        {
            return new TextBox
            {
                Height = 34,
                FontSize = 15,
                Padding = new Thickness(8),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right
            };
        }

        private void AddShiftActualPaymentRow(
            Grid grid,
            int row,
            string paymentName,
            TextBox actualTextBox)
        {
            AddShiftTableCell(
                grid, paymentName, row, 0, false);

            Grid.SetRow(actualTextBox, row);
            Grid.SetColumn(actualTextBox, 1);

            actualTextBox.Margin = new Thickness(5);

            grid.Children.Add(actualTextBox);
        }

        private void AddShiftTableCell(
            Grid grid,
            string value,
            int row,
            int column,
            bool isHeader)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(
                    Color.FromRgb(220, 232, 227)),
                BorderThickness = new Thickness(1),

                Background = isHeader
                    ? new SolidColorBrush(
                        Color.FromRgb(232, 241, 237))
                    : Brushes.White,

                Padding = new Thickness(8),
                Margin = new Thickness(1)
            };

            border.Child = new TextBlock
            {
                Text = value,
                FontSize = isHeader ? 13 : 14,

                FontWeight = isHeader
                    ? FontWeights.Bold
                    : FontWeights.Normal,

                Foreground = new SolidColorBrush(
                    Color.FromRgb(24, 63, 54)),

                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            Grid.SetRow(border, row);
            Grid.SetColumn(border, column);

            grid.Children.Add(border);
        }

        private bool TryReadActualAmount(
            string text,
            out double amount)
        {
            amount = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool parsed =
                double.TryParse(
                    text.Trim(),
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.CurrentCulture,
                    out amount) ||

                double.TryParse(
                    text.Trim(),
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out amount);

            return parsed &&
                   !double.IsNaN(amount) &&
                   !double.IsInfinity(amount) &&
                   amount >= 0;
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
                    Width = new DataGridLength(
                        1,
                        DataGridLengthUnitType.Star)
                });

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "اسم الصنف",
                    Binding = new Binding("Name"),
                    Width = new DataGridLength(
                        2,
                        DataGridLengthUnitType.Star)
                });

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "الرصيد الصالح للبيع",
                    Binding = new Binding("Quantity")
                    {
                        StringFormat = "N3"
                    },
                    Width = new DataGridLength(
                        1.2,
                        DataGridLengthUnitType.Star)
                });

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "الحد الأدنى",
                    Binding = new Binding("MinimumStockLevel")
                    {
                        StringFormat = "N3"
                    },
                    Width = new DataGridLength(
                        1.2,
                        DataGridLengthUnitType.Star)
                });

                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "الوحدة",
                    Binding = new Binding("SmallUnitName"),
                    Width = new DataGridLength(
                        1,
                        DataGridLengthUnitType.Star)
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
        // سجل الشيفتات والإغلاقات
        // ==========================================

        private void ShiftHistoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!string.Equals(
                currentRole,
                "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "سجل الشيفتات والإغلاقات مخصص لمدير النظام فقط.",
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var window = new ShiftHistoryWindow(currentRole);

                OpenIndependentWindow(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح سجل الشيفتات:\n\n" + ex.Message,
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
