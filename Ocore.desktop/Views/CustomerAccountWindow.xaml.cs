
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class CustomerAccountWindow : Window
    {
        private readonly int currentUserId;
        private readonly Customer customer;
        private readonly CustomerAccountService accountService;

        private bool isCollecting;

        public CustomerAccountWindow(
            int userId,
            Customer selectedCustomer)
        {
            InitializeComponent();

            if (selectedCustomer == null)
                throw new ArgumentNullException("selectedCustomer");

            currentUserId = userId;
            customer = selectedCustomer;

            accountService = new CustomerAccountService();

            LoadCustomerInformation();
            LoadAccountData();
        }

        #region Customer Information

        private void LoadCustomerInformation()
        {
            CustomerNameTextBlock.Text = customer.Name;
            CustomerCodeTextBlock.Text = customer.CustomerCode;

            CustomerPhoneTextBlock.Text =
                string.IsNullOrWhiteSpace(customer.Phone)
                    ? "لا يوجد رقم هاتف مسجل"
                    : "الهاتف: " + customer.Phone;
        }

        #endregion

        #region Load Account

        private void LoadAccountData()
        {
            try
            {
                double balance =
                    accountService.GetCustomerBalance(
                        currentUserId,
                        customer.Id);

                List<CustomerAccountService.CustomerAccountTransaction>
                    transactions =
                    accountService.GetCustomerTransactions(
                        currentUserId,
                        customer.Id);

                TransactionsGrid.ItemsSource = null;
                TransactionsGrid.ItemsSource = transactions;

                CustomerBalanceTextBlock.Text =
                    balance.ToString(
                        "N2",
                        CultureInfo.CurrentCulture);

                // الرصيد الموجب يعني أن العميل مدين.
                // الرصيد السالب يعني وجود رصيد لصالح العميل.
                if (balance > 0.00000001)
                {
                    CustomerBalanceTextBlock.Foreground =
                        System.Windows.Media.Brushes.DarkRed;
                }
                else if (balance < -0.00000001)
                {
                    CustomerBalanceTextBlock.Foreground =
                        System.Windows.Media.Brushes.DarkBlue;
                }
                else
                {
                    CustomerBalanceTextBlock.Foreground =
                        System.Windows.Media.Brushes.DarkGreen;
                }

                CollectPaymentButton.IsEnabled =
                    balance > 0.00000001 && !isCollecting;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحميل حساب العميل:\n\n" + ex.Message,
                    "كشف حساب العميل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Refresh

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadAccountData();
        }

        #endregion

        #region Collect Payment

        private void CollectPaymentButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isCollecting)
                return;

            double amount;

            if (!TryParseAmount(AmountTextBox.Text, out amount) ||
                amount <= 0 ||
                double.IsNaN(amount) ||
                double.IsInfinity(amount))
            {
                MessageBox.Show(
                    "يرجى إدخال مبلغ تحصيل صحيح أكبر من صفر.",
                    "التحصيل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                AmountTextBox.Focus();
                AmountTextBox.SelectAll();
                return;
            }

            var selectedPaymentMethod =
                PaymentMethodComboBox.SelectedItem as ComboBoxItem;

            if (selectedPaymentMethod == null ||
                selectedPaymentMethod.Tag == null)
            {
                MessageBox.Show(
                    "يرجى اختيار طريقة التحصيل.",
                    "التحصيل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string paymentMethod =
                selectedPaymentMethod.Tag.ToString();

            amount = Math.Round(
                amount,
                2,
                MidpointRounding.AwayFromZero);

            string confirmation =
                "هل تريد تأكيد تحصيل المبلغ التالي؟\n\n" +
                "العميل: " + customer.Name + "\n" +
                "المبلغ: " +
                amount.ToString(
                    "N2",
                    CultureInfo.CurrentCulture) + " جنيه\n" +
                "طريقة التحصيل: " +
                selectedPaymentMethod.Content + "\n\n" +
                "سيتم تسجيل الحركة في حساب العميل.";

            MessageBoxResult result = MessageBox.Show(
                confirmation,
                "تأكيد التحصيل",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                isCollecting = true;
                CollectPaymentButton.IsEnabled = false;

                long transactionId =
                    accountService.CollectPayment(
                        currentUserId,
                        customer.Id,
                        amount,
                        paymentMethod,
                        ReferenceNumberTextBox.Text,
                        NotesTextBox.Text);

                AmountTextBox.Clear();
                ReferenceNumberTextBox.Clear();
                NotesTextBox.Clear();

                LoadAccountData();

                MessageBox.Show(
                    "تم تسجيل التحصيل بنجاح.\n\n" +
                    "رقم الحركة: " + transactionId,
                    "التحصيل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تسجيل التحصيل:\n\n" + ex.Message,
                    "التحصيل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                isCollecting = false;

                CollectPaymentButton.IsEnabled =
                    IsCustomerDebtOutstanding();
            }
        }

        private bool TryParseAmount(
            string value,
            out double amount)
        {
            amount = 0;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            const NumberStyles styles =
                NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowTrailingWhite |
                NumberStyles.AllowLeadingSign |
                NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowThousands;

            if (double.TryParse(
                value.Trim(),
                styles,
                CultureInfo.CurrentCulture,
                out amount))
            {
                return true;
            }

            return double.TryParse(
                value.Trim(),
                styles,
                CultureInfo.InvariantCulture,
                out amount);
        }

        private bool IsCustomerDebtOutstanding()
        {
            try
            {
                return accountService.GetCustomerBalance(
                    currentUserId,
                    customer.Id) > 0.00000001;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Close

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}
