
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class ShiftHistoryWindow : Window
    {
        private readonly ShiftService shiftService;
        private readonly ExpenseService expenseService;

        public ShiftHistoryWindow(string currentRole)
        {
            if (!string.Equals(
                currentRole,
                "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    "سجل الشيفتات والإغلاقات مخصص لمدير النظام فقط.");
            }

            InitializeComponent();

            shiftService = new ShiftService();
            expenseService = new ExpenseService();

            RefreshButton.Click += RefreshButton_Click;
            CloseButton.Click += CloseButton_Click;

            LoadShifts();
        }

        private void LoadShifts()
        {
            try
            {
                ShiftsGrid.SelectedItem = null;

                ClearDetails();

                var shifts = shiftService.GetAllShifts();

                var rows = shifts.Select(shift => new ShiftRow
                {
                    Shift = shift,
                    ShiftNumber = string.IsNullOrWhiteSpace(shift.ShiftNumber)
                        ? shift.Id.ToString()
                        : shift.ShiftNumber,

                    StatusDisplay = shift.Status == "Open"
                        ? "مفتوح"
                        : "مغلق",

                    OpenedAtDisplay = FormatDate(shift.OpenedAt),
                    OpenedByName = EmptyAsDash(shift.OpenedByName),
                    ClosedAtDisplay = FormatDate(shift.ClosedAt),
                    ClosedByName = EmptyAsDash(shift.ClosedByName)
                }).ToList();

                ShiftsGrid.ItemsSource = rows;

                RecordCountText.Text =
                    "عدد الشيفتات: " + rows.Count;

                if (rows.Count == 0)
                {
                    ClosureStatusText.Text =
                        "لا توجد شيفتات مسجلة حتى الآن.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحميل سجل الشيفتات:\n\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShiftsGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            var selected = ShiftsGrid.SelectedItem as ShiftRow;

            if (selected == null)
            {
                ClearDetails();
                return;
            }

            LoadShiftDetails(selected.Shift);
        }

        private void LoadShiftDetails(ShiftService.ShiftInfo shift)
        {
            try
            {
                SelectedShiftNumberText.Text =
                    EmptyAsDash(shift.ShiftNumber);

                SelectedShiftStatusText.Text =
                    shift.Status == "Open" ? "مفتوح" : "مغلق";

                SelectedOpenedByText.Text =
                    EmptyAsDash(shift.OpenedByName);

                SelectedClosedByText.Text =
                    shift.Status == "Open"
                        ? "لم يغلق بعد"
                        : EmptyAsDash(shift.ClosedByName);

                LoadFinancialDetails(shift.Id);
                LoadClosureDetails(shift);
                LoadExpenseDetails(shift.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحميل تفاصيل الشيفت:\n\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadFinancialDetails(int shiftId)
        {
            var summary =
                shiftService.GetShiftFinancialSummary(shiftId);

            var rows = new List<FinancialRow>
            {
                new FinancialRow
                {
                    Name = "المبيعات",
                    Cash = summary.SalesCash,
                    Card = summary.SalesCard,
                    InstaPay = summary.SalesInstaPay,
                    Transfer = summary.SalesTransfer
                },

                new FinancialRow
                {
                    Name = "التحصيلات",
                    Cash = summary.CollectionsCash,
                    Card = summary.CollectionsCard,
                    InstaPay = summary.CollectionsInstaPay,
                    Transfer = summary.CollectionsTransfer
                },

                new FinancialRow
                {
                    Name = "المرتجعات الفعلية",
                    Cash = summary.RefundsCash,
                    Card = summary.RefundsCard,
                    InstaPay = summary.RefundsInstaPay,
                    Transfer = summary.RefundsTransfer
                },

                new FinancialRow
                {
                    Name = "المصروفات الفعالة",
                    Cash = summary.ExpensesCash,
                    Card = summary.ExpensesCard,
                    InstaPay = summary.ExpensesInstaPay,
                    Transfer = summary.ExpensesTransfer
                },

                new FinancialRow
                {
                    Name = "المتوقع النهائي",
                    Cash = summary.ExpectedCash,
                    Card = summary.ExpectedCard,
                    InstaPay = summary.ExpectedInstaPay,
                    Transfer = summary.ExpectedTransfer
                }
            };

            FinancialGrid.ItemsSource = rows;
        }

        private void LoadClosureDetails(ShiftService.ShiftInfo shift)
        {
            var closure = shiftService.GetShiftClosure(shift.Id);

            if (closure == null)
            {
                ClosureGrid.ItemsSource = null;

                ClosureStatusText.Text =
                    shift.Status == "Open"
                        ? "الشيفت مفتوح ولم يتم إنشاء تقرير إغلاق له بعد."
                        : "لا يوجد تقرير إغلاق مسجل لهذا الشيفت.";

                return;
            }

            ClosureStatusText.Text =
                "أغلق الشيفت: " +
                EmptyAsDash(closure.ClosedByName) +
                "     |     وقت الإغلاق: " +
                FormatDate(closure.ClosedAt) +
                "     |     إجمالي الفرق: " +
                closure.TotalDifference.ToString("N2") +
                " جنيه";

            var rows = new List<ClosureRow>
            {
                new ClosureRow
                {
                    PaymentMethod = "النقدية",
                    Expected = closure.ExpectedCash,
                    Actual = closure.ActualCash,
                    Difference = closure.DifferenceCash
                },

                new ClosureRow
                {
                    PaymentMethod = "الفيزا",
                    Expected = closure.ExpectedCard,
                    Actual = closure.ActualCard,
                    Difference = closure.DifferenceCard
                },

                new ClosureRow
                {
                    PaymentMethod = "إنستا باي",
                    Expected = closure.ExpectedInstaPay,
                    Actual = closure.ActualInstaPay,
                    Difference = closure.DifferenceInstaPay
                },

                new ClosureRow
                {
                    PaymentMethod = "التحويل البنكي",
                    Expected = closure.ExpectedTransfer,
                    Actual = closure.ActualTransfer,
                    Difference = closure.DifferenceTransfer
                },

                new ClosureRow
                {
                    PaymentMethod = "الإجمالي",
                    Expected = closure.TotalExpected,
                    Actual = closure.TotalActual,
                    Difference = closure.TotalDifference
                }
            };

            ClosureGrid.ItemsSource = rows;
        }

        private void LoadExpenseDetails(int shiftId)
        {
            var expenses = expenseService.GetExpensesByShift(shiftId);

            var rows = expenses.Select(expense => new ExpenseRow
            {
                CreatedAt = FormatDate(expense.CreatedAt),
                Category = expense.Category,
                Description = expense.Description,
                Amount = expense.Amount,

                PaymentMethodDisplay =
                    GetPaymentMethodName(expense.PaymentMethod),

                CreatedByName = EmptyAsDash(expense.CreatedByName),

                StatusDisplay = expense.IsVoided
                    ? "ملغي"
                    : "فعال",

                VoidedByName = expense.IsVoided
                    ? EmptyAsDash(expense.VoidedByName)
                    : "—",

                VoidedAt = expense.IsVoided
                    ? FormatDate(expense.VoidedAt)
                    : "—",

                VoidReason = expense.IsVoided
                    ? EmptyAsDash(expense.VoidReason)
                    : "—"
            }).ToList();

            ExpensesGrid.ItemsSource = rows;
        }

        private void ClearDetails()
        {
            SelectedShiftNumberText.Text = "—";
            SelectedShiftStatusText.Text = "—";
            SelectedOpenedByText.Text = "—";
            SelectedClosedByText.Text = "—";

            FinancialGrid.ItemsSource = null;
            ClosureGrid.ItemsSource = null;
            ExpensesGrid.ItemsSource = null;

            ClosureStatusText.Text =
                "اختر شيفتًا لعرض تقرير الإغلاق.";
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadShifts();
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private static string GetPaymentMethodName(string method)
        {
            switch (method)
            {
                case "Cash":
                    return "نقدية";

                case "Card":
                    return "فيزا";

                case "InstaPay":
                    return "إنستا باي";

                case "Transfer":
                    return "تحويل بنكي";

                default:
                    return EmptyAsDash(method);
            }
        }

        private static string FormatDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";

            DateTime date;

            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
            {
                return date.ToString("yyyy/MM/dd hh:mm tt");
            }

            return value;
        }

        private static string EmptyAsDash(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "—"
                : value;
        }

        private class ShiftRow
        {
            public ShiftService.ShiftInfo Shift { get; set; }
            public string ShiftNumber { get; set; }
            public string StatusDisplay { get; set; }
            public string OpenedAtDisplay { get; set; }
            public string OpenedByName { get; set; }
            public string ClosedAtDisplay { get; set; }
            public string ClosedByName { get; set; }
        }

        private class FinancialRow
        {
            public string Name { get; set; }
            public double Cash { get; set; }
            public double Card { get; set; }
            public double InstaPay { get; set; }
            public double Transfer { get; set; }

            public double Total
            {
                get
                {
                    return Cash + Card + InstaPay + Transfer;
                }
            }
        }

        private class ClosureRow
        {
            public string PaymentMethod { get; set; }
            public double Expected { get; set; }
            public double Actual { get; set; }
            public double Difference { get; set; }
        }

        private class ExpenseRow
        {
            public string CreatedAt { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
            public double Amount { get; set; }
            public string PaymentMethodDisplay { get; set; }
            public string CreatedByName { get; set; }
            public string StatusDisplay { get; set; }
            public string VoidedByName { get; set; }
            public string VoidedAt { get; set; }
            public string VoidReason { get; set; }
        }
    }
}
