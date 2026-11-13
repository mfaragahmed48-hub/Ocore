
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class ExpensesWindow : Window
    {
        private readonly int currentUserId;
        private readonly string currentRole;

        private readonly ExpenseService expenseService;
        private readonly ShiftService shiftService;

        private int currentShiftId;
        private bool isLoading;

        public ExpensesWindow(int userId, string role)
        {
            InitializeComponent();

            if (userId <= 0)
                throw new ArgumentException("رقم المستخدم غير صحيح.");

            currentUserId = userId;
            currentRole = role ?? string.Empty;

            expenseService = new ExpenseService();
            shiftService = new ShiftService();

            ConfigurePaymentMethods();
            ConfigureCategories();
            ConfigurePermissions();
            ConnectEvents();

            LoadCurrentShift();
        }

        private void ConfigureCategories()
        {
            CategoryComboBox.ItemsSource =
                ExpenseService.GetCategories();

            if (CategoryComboBox.Items.Count > 0)
                CategoryComboBox.SelectedIndex = 0;
        }

        private void ConfigurePaymentMethods()
        {
            PaymentMethodComboBox.Items.Clear();

            AddPaymentMethod("نقدية", "Cash");
            AddPaymentMethod("فيزا / بطاقة", "Card");
            AddPaymentMethod("إنستا باي", "InstaPay");
            AddPaymentMethod("تحويل بنكي", "Transfer");

            PaymentMethodComboBox.SelectedIndex = 0;
        }

        private void AddPaymentMethod(string title, string value)
        {
            var item = new ComboBoxItem
            {
                Content = title,
                Tag = value
            };

            PaymentMethodComboBox.Items.Add(item);
        }

        private void ConfigurePermissions()
        {
            bool isManager = string.Equals(
                currentRole,
                "Manager",
                StringComparison.OrdinalIgnoreCase);

            CancelExpenseButton.Visibility = isManager
                ? Visibility.Visible
                : Visibility.Collapsed;

            CancelExpenseButton.IsEnabled = false;
        }

        private void ConnectEvents()
        {
            AddExpenseButton.Click += AddExpenseButton_Click;
            RefreshButton.Click += RefreshButton_Click;
            CancelExpenseButton.Click += CancelExpenseButton_Click;

            ExpensesDataGrid.SelectionChanged +=
                ExpensesDataGrid_SelectionChanged;
        }

        private void LoadCurrentShift()
        {
            if (isLoading)
                return;

            isLoading = true;

            try
            {
                var shift = shiftService.GetCurrentShift();

                if (shift == null)
                {
                    currentShiftId = 0;

                    ShiftStatusText.Text =
                        "لا يوجد شيفت مفتوح حاليًا.";

                    ShiftStatusText.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(220, 38, 38));

                    AddExpenseButton.IsEnabled = false;

                    ExpensesDataGrid.ItemsSource = null;

                    ExpensesSummaryText.Text =
                        "لا توجد مصروفات لعرضها.";

                    CancelExpenseButton.IsEnabled = false;

                    StatusText.Text =
                        "يجب فتح شيفت قبل تسجيل المصروفات.";

                    return;
                }

                currentShiftId = shift.Id;

                ShiftStatusText.Text =
                    "الشيفت المفتوح رقم: " +
                    shift.ShiftNumber +
                    " | بدأ بواسطة: " +
                    shift.OpenedByName +
                    " | تاريخ الفتح: " +
                    shift.OpenedAt;

                ShiftStatusText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(37, 99, 235));

                AddExpenseButton.IsEnabled = true;

                LoadExpenses();
            }
            catch (Exception ex)
            {
                currentShiftId = 0;

                ShiftStatusText.Text =
                    "تعذر قراءة حالة الشيفت.";

                AddExpenseButton.IsEnabled = false;

                StatusText.Text = ex.Message;

                MessageBox.Show(
                    "حدث خطأ أثناء تحميل بيانات الشيفت:\n" +
                    ex.Message,
                    "مصروفات الصيدلية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                isLoading = false;
            }
        }

        private void LoadExpenses()
        {
            if (currentShiftId <= 0)
                return;

            var expenses =
                expenseService.GetExpensesByShift(currentShiftId);

            ExpensesDataGrid.ItemsSource = expenses;

            int totalCount = expenses.Count;
            int activeCount = 0;
            double totalAmount = 0;

            foreach (var expense in expenses)
            {
                if (!expense.IsVoided)
                {
                    activeCount++;
                    totalAmount += expense.Amount;
                }
            }

            ExpensesSummaryText.Text =
                "إجمالي السجلات: " +
                totalCount.ToString("N0") +
                " | المصروفات الفعالة: " +
                activeCount.ToString("N0") +
                " | إجمالي المصروفات: " +
                totalAmount.ToString("N2") +
                " جنيه";

            CancelExpenseButton.IsEnabled = false;

            StatusText.Text =
                "تم تحميل سجل المصروفات بنجاح.";
        }

        private void AddExpenseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (currentShiftId <= 0)
                {
                    MessageBox.Show(
                        "لا يوجد شيفت مفتوح. افتح الشيفت أولًا.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var shift = shiftService.GetCurrentShift();

                if (shift == null || shift.Id != currentShiftId)
                {
                    MessageBox.Show(
                        "تغيرت حالة الشيفت. سيتم تحديث الشاشة.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    LoadCurrentShift();
                    return;
                }

                double amount;

                string amountText =
                    AmountTextBox.Text.Trim();

                bool validAmount =
                    double.TryParse(
                        amountText,
                        NumberStyles.Number,
                        CultureInfo.CurrentCulture,
                        out amount);

                if (!validAmount)
                {
                    validAmount =
                        double.TryParse(
                            amountText,
                            NumberStyles.Number,
                            CultureInfo.InvariantCulture,
                            out amount);
                }

                if (!validAmount || amount <= 0 ||
                    double.IsNaN(amount) ||
                    double.IsInfinity(amount))
                {
                    MessageBox.Show(
                        "أدخل قيمة مصروف صحيحة وأكبر من صفر.",
                        "قيمة غير صحيحة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    AmountTextBox.Focus();
                    return;
                }

                if (CategoryComboBox.SelectedItem == null)
                {
                    MessageBox.Show(
                        "اختر بند المصروف.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var paymentItem =
                    PaymentMethodComboBox.SelectedItem
                    as ComboBoxItem;

                if (paymentItem == null ||
                    paymentItem.Tag == null)
                {
                    MessageBox.Show(
                        "اختر وسيلة الدفع.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                string category =
                    Convert.ToString(
                        CategoryComboBox.SelectedItem);

                string paymentMethod =
                    Convert.ToString(paymentItem.Tag);

                string description =
                    DescriptionTextBox.Text.Trim();

                AddExpenseButton.IsEnabled = false;

                int expenseId = expenseService.AddExpense(
                    currentUserId,
                    amount,
                    category,
                    description,
                    paymentMethod);

                AmountTextBox.Clear();
                DescriptionTextBox.Clear();

                LoadCurrentShift();

                StatusText.Text =
                    "تم تسجيل المصروف رقم " +
                    expenseId +
                    " بنجاح.";

                MessageBox.Show(
                    "تم تسجيل المصروف بنجاح.",
                    "تم الحفظ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تسجيل المصروف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (currentShiftId > 0)
                    AddExpenseButton.IsEnabled = true;
            }
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadCurrentShift();
        }

        private void ExpensesDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!string.Equals(
                currentRole,
                "Manager",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var selected =
                ExpensesDataGrid.SelectedItem
                as ExpenseService.ExpenseInfo;

            CancelExpenseButton.IsEnabled =
                selected != null &&
                !selected.IsVoided &&
                selected.ShiftId == currentShiftId &&
                currentShiftId > 0;
        }

        private void CancelExpenseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (!string.Equals(
                    currentRole,
                    "Manager",
                    StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "إلغاء المصروفات متاح لمدير النظام فقط.",
                        "غير مسموح",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var selected =
                    ExpensesDataGrid.SelectedItem
                    as ExpenseService.ExpenseInfo;

                if (selected == null)
                {
                    MessageBox.Show(
                        "حدد المصروف الذي تريد إلغاءه.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                if (selected.IsVoided)
                {
                    MessageBox.Show(
                        "هذا المصروف ملغي بالفعل.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                if (selected.ShiftId != currentShiftId ||
                    currentShiftId <= 0)
                {
                    MessageBox.Show(
                        "لا يمكن إلغاء مصروف تابع لشيفت غير مفتوح.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                string reason = AskForCancellationReason();

                if (reason == null)
                    return;

                bool result = expenseService.CancelExpense(
                    currentUserId,
                    selected.Id,
                    reason);

                if (result)
                {
                    LoadCurrentShift();

                    MessageBox.Show(
                        "تم إلغاء المصروف مع الاحتفاظ بسجله الأصلي.",
                        "تم الإلغاء",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر إلغاء المصروف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                LoadCurrentShift();
            }
        }

        private string AskForCancellationReason()
        {
            var dialog = new Window
            {
                Title = "سبب إلغاء المصروف",
                Width = 430,
                Height = 260,
                MinWidth = 430,
                MinHeight = 260,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                Owner = this,
                FlowDirection = FlowDirection.RightToLeft,
                Background = Brushes.White
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(18)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "اكتب سبب إلغاء المصروف رقم " +
                       selectedExpenseNumber() + ":",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var reasonBox = new TextBox
            {
                Height = 90,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                Padding = new Thickness(8)
            };

            panel.Children.Add(reasonBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 15, 0, 0)
            };

            var confirmButton = new Button
            {
                Content = "تأكيد الإلغاء",
                Width = 115,
                Height = 35,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(
                    Color.FromRgb(220, 38, 38)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };

            var cancelButton = new Button
            {
                Content = "رجوع",
                Width = 85,
                Height = 35
            };

            confirmButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(reasonBox.Text))
                {
                    MessageBox.Show(
                        "يجب كتابة سبب الإلغاء.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    reasonBox.Focus();
                    return;
                }

                dialog.DialogResult = true;
            };

            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = false;
            };

            buttons.Children.Add(confirmButton);
            buttons.Children.Add(cancelButton);

            panel.Children.Add(buttons);

            dialog.Content = panel;

            bool? answer = dialog.ShowDialog();

            if (answer != true)
                return null;

            return reasonBox.Text.Trim();
        }

        private int selectedExpenseNumber()
        {
            var selected =
                ExpensesDataGrid.SelectedItem
                as ExpenseService.ExpenseInfo;

            return selected == null ? 0 : selected.Id;
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
