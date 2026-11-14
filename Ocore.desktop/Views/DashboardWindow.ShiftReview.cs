
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class DashboardWindow : Window
    {
        private void ReviewCurrentShiftButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!string.Equals(
                currentRole,
                "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "هذه الشاشة متاحة للمدير فقط.",
                    "صلاحيات غير كافية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var window = new CurrentShiftReviewWindow(
                    shiftService,
                    new ExpenseService());

                window.Owner = this;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر فتح مراجعة الشيفت:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    public class CurrentShiftReviewWindow : Window
    {
        private readonly ShiftService shiftService;
        private readonly ExpenseService expenseService;

        private TextBlock shiftInfoText;
        private TextBlock lastUpdateText;
        private DataGrid financialGrid;
        private DataGrid expensesGrid;

        public CurrentShiftReviewWindow(
            ShiftService shiftService,
            ExpenseService expenseService)
        {
            this.shiftService = shiftService;
            this.expenseService = expenseService;

            Title = "O CORE - مراجعة الشيفت الحالي";
            Width = 1100;
            Height = 760;
            MinWidth = 850;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FlowDirection = FlowDirection.RightToLeft;
            Background = new SolidColorBrush(
                Color.FromRgb(243, 246, 248));

            BuildInterface();
            LoadShiftData();
        }

        private void BuildInterface()
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
                new RowDefinition { Height = new GridLength(230) });

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            root.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "مراجعة الشيفت الحالي",
                FontSize = 23,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(31, 76, 65)),
                Margin = new Thickness(0, 0, 0, 12)
            };

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            shiftInfoText = new TextBlock
            {
                FontSize = 14,
                Padding = new Thickness(12),
                Background = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };

            Grid.SetRow(shiftInfoText, 1);
            root.Children.Add(shiftInfoText);

            financialGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                FlowDirection = FlowDirection.RightToLeft,
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12),
                ItemsSource = new List<PaymentSummaryRow>()
            };

            AddColumn(financialGrid, "وسيلة الدفع", "PaymentMethod", 1.2);
            AddColumn(financialGrid, "المبيعات", "Sales", 1);
            AddColumn(financialGrid, "التحصيلات", "Collections", 1);
            AddColumn(financialGrid, "المرتجعات", "Refunds", 1);
            AddColumn(financialGrid, "المصروفات", "Expenses", 1);
            AddColumn(financialGrid, "المتوقع", "Expected", 1);

            Grid.SetRow(financialGrid, 2);
            root.Children.Add(financialGrid);

            var expensesHeader = new TextBlock
            {
                Text = "تفاصيل المصروفات",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            };

            Grid.SetRow(expensesHeader, 3);
            root.Children.Add(expensesHeader);

            expensesGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                FlowDirection = FlowDirection.RightToLeft,
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12)
            };

            AddColumn(expensesGrid, "التاريخ", "CreatedAt", 1.1);
            AddColumn(expensesGrid, "التصنيف", "Category", 1);
            AddColumn(expensesGrid, "الوصف", "Description", 1.5);
            AddColumn(expensesGrid, "وسيلة الدفع", "PaymentMethod", 1);
            AddColumn(expensesGrid, "المبلغ", "Amount", 0.8);
            AddColumn(expensesGrid, "الحالة", "Status", 0.8);
            AddColumn(expensesGrid, "ألغاه", "VoidedByName", 1);
            AddColumn(expensesGrid, "وقت الإلغاء", "VoidedAt", 1);
            AddColumn(expensesGrid, "سبب الإلغاء", "VoidReason", 1.5);

            Grid.SetRow(expensesGrid, 4);
            root.Children.Add(expensesGrid);

            var footer = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(0, 5, 0, 0)
            };

            lastUpdateText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.DimGray
            };

            DockPanel.SetDock(lastUpdateText, Dock.Right);
            footer.Children.Add(lastUpdateText);

            var closeButton = new Button
            {
                Content = "إغلاق",
                Width = 100,
                Height = 35,
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(8),
                IsCancel = true
            };

            DockPanel.SetDock(closeButton, Dock.Left);
            footer.Children.Add(closeButton);

            var refreshButton = new Button
            {
                Content = "تحديث البيانات",
                Width = 125,
                Height = 35,
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(8),
                Background = new SolidColorBrush(
                    Color.FromRgb(36, 107, 88)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };

            refreshButton.Click += (s, e) => LoadShiftData();

            DockPanel.SetDock(refreshButton, Dock.Left);
            footer.Children.Add(refreshButton);

            Grid.SetRow(footer, 5);
            root.Children.Add(footer);

            Content = root;
        }

        private void AddColumn(
            DataGrid grid,
            string header,
            string property,
            double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(property),
                Width = new DataGridLength(
                    width,
                    DataGridLengthUnitType.Star)
            });
        }

        private void LoadShiftData()
        {
            try
            {
                var shift = shiftService.GetCurrentShift();

                if (shift == null)
                {
                    shiftInfoText.Text =
                        "لا يوجد شيفت مفتوح حاليًا.";

                    financialGrid.ItemsSource =
                        new List<PaymentSummaryRow>();

                    expensesGrid.ItemsSource =
                        new List<ExpenseReviewRow>();

                    lastUpdateText.Text = string.Empty;
                    return;
                }

                var summary =
                    shiftService.GetShiftFinancialSummary(shift.Id);

                if (summary == null)
                    throw new InvalidOperationException(
                        "تعذر قراءة الملخص المالي للشيفت.");

                shiftInfoText.Text =
                    "رقم الشيفت: " + shift.ShiftNumber +
                    "    |    الحالة: مفتوح" +
                    "\nفتح الشيفت بواسطة: " + shift.OpenedByName +
                    "    |    وقت الفتح: " + shift.OpenedAt;

                var rows = new List<PaymentSummaryRow>();

                rows.Add(CreatePaymentRow(
                    "نقدي",
                    summary.SalesCash,
                    summary.CollectionsCash,
                    summary.RefundsCash,
                    summary.ExpensesCash,
                    summary.ExpectedCash));

                rows.Add(CreatePaymentRow(
                    "بطاقة",
                    summary.SalesCard,
                    summary.CollectionsCard,
                    summary.RefundsCard,
                    summary.ExpensesCard,
                    summary.ExpectedCard));

                rows.Add(CreatePaymentRow(
                    "InstaPay",
                    summary.SalesInstaPay,
                    summary.CollectionsInstaPay,
                    summary.RefundsInstaPay,
                    summary.ExpensesInstaPay,
                    summary.ExpectedInstaPay));

                rows.Add(CreatePaymentRow(
                    "تحويل بنكي",
                    summary.SalesTransfer,
                    summary.CollectionsTransfer,
                    summary.RefundsTransfer,
                    summary.ExpensesTransfer,
                    summary.ExpectedTransfer));

                rows.Add(CreatePaymentRow(
                    "الإجمالي",
                    summary.SalesCash +
                    summary.SalesCard +
                    summary.SalesInstaPay +
                    summary.SalesTransfer,
                    summary.CollectionsCash +
                    summary.CollectionsCard +
                    summary.CollectionsInstaPay +
                    summary.CollectionsTransfer,
                    summary.RefundsCash +
                    summary.RefundsCard +
                    summary.RefundsInstaPay +
                    summary.RefundsTransfer,
                    summary.ExpensesCash +
                    summary.ExpensesCard +
                    summary.ExpensesInstaPay +
                    summary.ExpensesTransfer,
                    summary.TotalExpected));

                financialGrid.ItemsSource = rows;

                var expenses =
                    expenseService.GetExpensesByShift(shift.Id);

                var expenseRows = new List<ExpenseReviewRow>();

                foreach (var expense in expenses)
                {
                    expenseRows.Add(new ExpenseReviewRow
                    {
                        CreatedAt = expense.CreatedAt,
                        Category = expense.Category,
                        Description = expense.Description,
                        PaymentMethod = expense.PaymentMethod,
                        Amount = expense.Amount.ToString("N2"),
                        Status = expense.IsVoided
                            ? "ملغي"
                            : "فعال",
                        VoidedByName = expense.IsVoided
                            ? expense.VoidedByName
                            : string.Empty,
                        VoidedAt = expense.IsVoided
                            ? expense.VoidedAt
                            : string.Empty,
                        VoidReason = expense.IsVoided
                            ? expense.VoidReason
                            : string.Empty
                    });
                }

                expensesGrid.ItemsSource = expenseRows;

                lastUpdateText.Text =
                    "آخر تحديث: " +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحديث بيانات الشيفت:\n" +
                    ex.Message,
                    "مراجعة الشيفت",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private PaymentSummaryRow CreatePaymentRow(
            string method,
            double sales,
            double collections,
            double refunds,
            double expenses,
            double expected)
        {
            return new PaymentSummaryRow
            {
                PaymentMethod = method,
                Sales = sales.ToString("N2"),
                Collections = collections.ToString("N2"),
                Refunds = refunds.ToString("N2"),
                Expenses = expenses.ToString("N2"),
                Expected = expected.ToString("N2")
            };
        }

        private class PaymentSummaryRow
        {
            public string PaymentMethod { get; set; }
            public string Sales { get; set; }
            public string Collections { get; set; }
            public string Refunds { get; set; }
            public string Expenses { get; set; }
            public string Expected { get; set; }
        }

        private class ExpenseReviewRow
        {
            public string CreatedAt { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
            public string PaymentMethod { get; set; }
            public string Amount { get; set; }
            public string Status { get; set; }
            public string VoidedByName { get; set; }
            public string VoidedAt { get; set; }
            public string VoidReason { get; set; }
        }
    }
}
