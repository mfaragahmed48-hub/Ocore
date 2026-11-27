
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ocore.desktop.Data;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class DashboardWindow : Window
    {
        private DashboardSalesService salesSummaryService;

        private bool IsSalesSummaryManager()
        {
            return string.Equals(
                currentRole,
                "Manager",
                StringComparison.OrdinalIgnoreCase);
        }

        private void SalesTotalCard_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            bool isManager = IsSalesSummaryManager();

            SalesTotalCard.Visibility = isManager
                ? Visibility.Visible
                : Visibility.Collapsed;

            DashboardCardsGrid.Columns = isManager ? 5 : 4;

            if (isManager)
            {
                if (salesSummaryService == null)
                    salesSummaryService = new DashboardSalesService();

                Activated += SalesSummaryDashboard_Activated;

                RefreshSalesSummaryCard();
            }
        }

        private void SalesSummaryDashboard_Activated(
            object sender,
            EventArgs e)
        {
            RefreshSalesSummaryCard();
        }

        private void RefreshSalesSummaryCard()
        {
            if (!IsSalesSummaryManager())
            {
                SalesTotalCard.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                if (salesSummaryService == null)
                    salesSummaryService = new DashboardSalesService();

                ShiftSalesSummary summary =
                    salesSummaryService.GetCurrentShiftSummary();

                SalesTotalText.Text = summary.NetSales.ToString("N2");
            }
            catch (Exception ex)
            {
                SalesTotalText.Text = "—";

                System.Diagnostics.Debug.WriteLine(
                    "RefreshSalesSummaryCard error: " + ex);
            }
        }

        private void SalesTotalCard_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (!IsSalesSummaryManager())
                return;

            var window = new SalesShiftDetailsWindow(currentRole)
            {
                Owner = this
            };

            window.ShowDialog();

            RefreshSalesSummaryCard();
        }
    }

    // =====================================================
    // خدمة حساب مبيعات الشيفت الحالي
    // =====================================================

    public class DashboardSalesService
    {
        private readonly string connectionString;
        private readonly ShiftService shiftService;

        public DashboardSalesService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() +
                ";Version=3;";

            shiftService = new ShiftService();
        }

        public ShiftSalesSummary GetCurrentShiftSummary()
        {
            var summary = new ShiftSalesSummary();

            var shift = shiftService.GetCurrentShift();

            if (shift == null)
                return summary;

            summary.HasOpenShift = true;
            summary.ShiftId = shift.Id;

            summary.ShiftNumber =
                string.IsNullOrWhiteSpace(shift.ShiftNumber)
                    ? shift.Id.ToString()
                    : shift.ShiftNumber;

            summary.OpenedAt = shift.OpenedAt;

            using (var connection =
                new SQLiteConnection(connectionString))
            {
                connection.Open();

                // فواتير البيع المكتملة في الشيفت الحالي فقط.
                using (var command = new SQLiteCommand(@"
                    SELECT
                        InvoiceNumber,
                        CreatedAt,
                        COALESCE(Subtotal, Total) AS Subtotal,
                        COALESCE(DiscountAmount, 0) AS DiscountAmount,
                        COALESCE(Total, 0) AS NetTotal
                    FROM SalesInvoices
                    WHERE ShiftId = @ShiftId
                      AND Status = 'Completed'
                    ORDER BY CreatedAt DESC, Id DESC;",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@ShiftId", shift.Id);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            summary.Invoices.Add(
                                new ShiftSalesInvoiceRow
                                {
                                    InvoiceNumber =
                                        Convert.ToString(reader["InvoiceNumber"]),

                                    CreatedAt =
                                        Convert.ToString(reader["CreatedAt"]),

                                    Subtotal =
                                        ReadMoney(reader["Subtotal"]),

                                    DiscountAmount =
                                        ReadMoney(reader["DiscountAmount"]),

                                    NetTotal =
                                        ReadMoney(reader["NetTotal"])
                                });
                        }
                    }
                }

                // قيمة المرتجعات المسجلة في الشيفت الحالي.
                // نخصم نصيب الخصم الأصلي من كل بند مرتجع.
                using (var command = new SQLiteCommand(@"
                    SELECT
                        r.ReturnNumber,
                        r.CreatedAt,

                        COALESCE(
                            SUM(
                                COALESCE(ri.Total, 0)
                                -
                                CASE
                                    WHEN COALESCE(ii.Quantity, 0) > 0
                                    THEN
                                        COALESCE(ii.DiscountAmount, 0)
                                        * COALESCE(ri.Quantity, 0)
                                        / ii.Quantity
                                    ELSE 0
                                END
                            ),
                            0
                        ) AS ReturnTotal

                    FROM SalesReturns r

                    LEFT JOIN SalesReturnItems ri
                        ON ri.ReturnId = r.Id

                    LEFT JOIN SalesInvoiceItems ii
                        ON ii.Id = ri.InvoiceItemId

                    WHERE r.ShiftId = @ShiftId
                      AND r.Status = 'Completed'

                    GROUP BY
                        r.Id,
                        r.ReturnNumber,
                        r.CreatedAt

                    ORDER BY r.CreatedAt DESC, r.Id DESC;",
                    connection))
                {
                    command.Parameters.AddWithValue(
                        "@ShiftId", shift.Id);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            summary.Returns.Add(
                                new ShiftSalesReturnRow
                                {
                                    ReturnNumber =
                                        Convert.ToString(reader["ReturnNumber"]),

                                    CreatedAt =
                                        Convert.ToString(reader["CreatedAt"]),

                                    Total =
                                        ReadMoney(reader["ReturnTotal"])
                                });
                        }
                    }
                }
            }

            summary.GrossSales = Math.Round(
                summary.Invoices.Sum(x => x.Subtotal), 2);

            summary.Discounts = Math.Round(
                summary.Invoices.Sum(x => x.DiscountAmount), 2);

            summary.ReturnsTotal = Math.Round(
                summary.Returns.Sum(x => x.Total), 2);

            summary.NetSales = Math.Round(
                summary.GrossSales
                - summary.Discounts
                - summary.ReturnsTotal,
                2);

            return summary;
        }

        private static double ReadMoney(object value)
        {
            if (value == null || value == DBNull.Value)
                return 0;

            return Convert.ToDouble(
                value,
                CultureInfo.InvariantCulture);
        }
    }

    // =====================================================
    // بيانات ملخص الشيفت
    // =====================================================

    public class ShiftSalesSummary
    {
        public bool HasOpenShift { get; set; }

        public int ShiftId { get; set; }

        public string ShiftNumber { get; set; }

        public string OpenedAt { get; set; }

        public double GrossSales { get; set; }

        public double Discounts { get; set; }

        public double ReturnsTotal { get; set; }

        public double NetSales { get; set; }

        public List<ShiftSalesInvoiceRow> Invoices { get; set; }

        public List<ShiftSalesReturnRow> Returns { get; set; }

        public ShiftSalesSummary()
        {
            Invoices = new List<ShiftSalesInvoiceRow>();
            Returns = new List<ShiftSalesReturnRow>();
        }
    }

    public class ShiftSalesInvoiceRow
    {
        public string InvoiceNumber { get; set; }

        public string CreatedAt { get; set; }

        public double Subtotal { get; set; }

        public double DiscountAmount { get; set; }

        public double NetTotal { get; set; }
    }

    public class ShiftSalesReturnRow
    {
        public string ReturnNumber { get; set; }

        public string CreatedAt { get; set; }

        public double Total { get; set; }
    }

    // =====================================================
    // نافذة تفاصيل مبيعات الشيفت
    // =====================================================

    public class SalesShiftDetailsWindow : Window
    {
        private readonly string currentRole;
        private readonly DashboardSalesService salesService;

        private TextBlock shiftInfoText;
        private TextBlock grossSalesText;
        private TextBlock discountsText;
        private TextBlock returnsText;
        private TextBlock netSalesText;

        private DataGrid invoicesGrid;
        private DataGrid returnsGrid;

        private TextBlock statusText;

        public SalesShiftDetailsWindow(string role)
        {
            if (!string.Equals(
                role,
                "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "تفاصيل إجمالي المبيعات مخصصة لمدير النظام فقط.");
            }

            currentRole = role;

            salesService = new DashboardSalesService();

            Title = "تفاصيل مبيعات الشيفت";
            Width = 1050;
            Height = 720;
            MinWidth = 850;
            MinHeight = 600;

            WindowStartupLocation =
                WindowStartupLocation.CenterOwner;

            FlowDirection = FlowDirection.RightToLeft;

            Background = new SolidColorBrush(
                Color.FromRgb(243, 246, 248));

            BuildWindow();

            RefreshData();
        }

        private void BuildWindow()
        {
            var root = new Grid
            {
                Margin = new Thickness(18)
            };

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            root.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            // العنوان والأزرار
            var header = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            var title = new TextBlock
            {
                Text = "تفاصيل إجمالي مبيعات الشيفت",
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(24, 63, 54)),
                VerticalAlignment = VerticalAlignment.Center
            };

            DockPanel.SetDock(title, Dock.Right);
            header.Children.Add(title);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var refreshButton = new Button
            {
                Content = "تحديث",
                Width = 95,
                Height = 34,
                Margin = new Thickness(5, 0, 5, 0),
                Background = new SolidColorBrush(
                    Color.FromRgb(22, 134, 93)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.Bold
            };

            refreshButton.Click += RefreshButton_Click;

            var closeButton = new Button
            {
                Content = "إغلاق",
                Width = 85,
                Height = 34,
                Margin = new Thickness(5, 0, 0, 0)
            };

            closeButton.Click += (s, e) => Close();

            buttons.Children.Add(refreshButton);
            buttons.Children.Add(closeButton);

            DockPanel.SetDock(buttons, Dock.Left);
            header.Children.Add(buttons);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ملخص الأرقام
            var summaryGrid = new UniformGrid
            {
                Columns = 4,
                Margin = new Thickness(0, 0, 0, 12)
            };

            grossSalesText = AddSummaryCard(
                summaryGrid, "إجمالي البيع قبل الخصم");

            discountsText = AddSummaryCard(
                summaryGrid, "إجمالي الخصومات");

            returnsText = AddSummaryCard(
                summaryGrid, "إجمالي المرتجعات");

            netSalesText = AddSummaryCard(
                summaryGrid, "صافي المبيعات");

            Grid.SetRow(summaryGrid, 1);
            root.Children.Add(summaryGrid);

            // فواتير البيع
            invoicesGrid = CreateDataGrid();

            invoicesGrid.Columns.Add(
                CreateColumn("رقم الفاتورة", "InvoiceNumber", 1.3));

            invoicesGrid.Columns.Add(
                CreateColumn("التاريخ والوقت", "CreatedAt", 1.5));

            invoicesGrid.Columns.Add(
                CreateMoneyColumn("الإجمالي", "Subtotal"));

            invoicesGrid.Columns.Add(
                CreateMoneyColumn("الخصم", "DiscountAmount"));

            invoicesGrid.Columns.Add(
                CreateMoneyColumn("الصافي", "NetTotal"));

            var invoicesGroup = new GroupBox
            {
                Header = "فواتير البيع في الشيفت",
                Content = invoicesGrid,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8),
                FontWeight = FontWeights.Bold
            };

            Grid.SetRow(invoicesGroup, 2);
            root.Children.Add(invoicesGroup);

            // المرتجعات
            returnsGrid = CreateDataGrid();

            returnsGrid.Columns.Add(
                CreateColumn("رقم المرتجع", "ReturnNumber", 1.3));

            returnsGrid.Columns.Add(
                CreateColumn("التاريخ والوقت", "CreatedAt", 1.5));

            returnsGrid.Columns.Add(
                CreateMoneyColumn("قيمة المرتجع", "Total"));

            var returnsGroup = new GroupBox
            {
                Header = "المرتجعات المسجلة في هذا الشيفت",
                Content = returnsGrid,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8),
                FontWeight = FontWeights.Bold
            };

            Grid.SetRow(returnsGroup, 3);
            root.Children.Add(returnsGroup);

            statusText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(
                    Color.FromRgb(100, 115, 111)),
                Margin = new Thickness(3, 5, 3, 0),
                FontSize = 12
            };

            Grid.SetRow(statusText, 4);
            root.Children.Add(statusText);

            Content = root;
        }

        private TextBlock AddSummaryCard(
            UniformGrid grid,
            string caption)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 12,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(100, 115, 111))
            });

            var value = new TextBlock
            {
                Text = "0.00",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(22, 134, 93)),
                Margin = new Thickness(0, 7, 0, 0)
            };

            panel.Children.Add(value);

            var border = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(4),
                Child = panel
            };

            grid.Children.Add(border);

            return value;
        }

        private DataGrid CreateDataGrid()
        {
            return new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeight = 30,
                FontSize = 12,
                Background = Brushes.White,
                Margin = new Thickness(0, 5, 0, 0)
            };
        }

        private DataGridTextColumn CreateColumn(
            string header,
            string property,
            double width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property),
                Width = new DataGridLength(
                    width, DataGridLengthUnitType.Star)
            };
        }

        private DataGridTextColumn CreateMoneyColumn(
            string header,
            string property)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property)
                {
                    StringFormat = "N2"
                },
                Width = new DataGridLength(
                    1, DataGridLengthUnitType.Star)
            };
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            RefreshData();
        }

        private void RefreshData()
        {
            if (!string.Equals(
                currentRole,
                "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                Close();
                return;
            }

            try
            {
                ShiftSalesSummary summary =
                    salesService.GetCurrentShiftSummary();

                grossSalesText.Text =
                    summary.GrossSales.ToString("N2");

                discountsText.Text =
                    summary.Discounts.ToString("N2");

                returnsText.Text =
                    summary.ReturnsTotal.ToString("N2");

                netSalesText.Text =
                    summary.NetSales.ToString("N2");

                invoicesGrid.ItemsSource = summary.Invoices;
                returnsGrid.ItemsSource = summary.Returns;

                if (!summary.HasOpenShift)
                {
                    shiftInfoText = null;

                    statusText.Text =
                        "لا يوجد شيفت مفتوح حاليًا. لا يتم عرض بيانات الشيفتات السابقة.";

                    return;
                }

                string openedAt = summary.OpenedAt ?? "";

                DateTime parsedDate;

                if (DateTime.TryParse(
                    openedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out parsedDate))
                {
                    openedAt = parsedDate.ToString(
                        "yyyy/MM/dd hh:mm tt");
                }

                statusText.Text =
                    "رقم الشيفت: " + summary.ShiftNumber +
                    "     |     بداية الشيفت: " + openedAt;
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("تحديث تفاصيل مبيعات الشيفت", ex);
            }
        }
    }
}
