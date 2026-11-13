
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class CustomersManagementWindow : Window
    {
        private readonly int currentUserId;
        private readonly CustomerService customerService;

        private List<Customer> customers;

        public CustomersManagementWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            customerService = new CustomerService();

            customers = new List<Customer>();

            LoadCustomers();
            ClearForm();
        }

        #region Load Customers

        private void LoadCustomers()
        {
            try
            {
                customers = customerService.GetCustomers(
                    currentUserId,
                    true,
                    SearchTextBox.Text);

                CustomersGrid.ItemsSource = null;
                CustomersGrid.ItemsSource = customers;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحميل قائمة العملاء:\n\n" + ex.Message,
                    "إدارة العملاء",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Search

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (customerService == null)
                return;

            LoadCustomers();
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadCustomers();
        }

        #endregion

        #region Selection

        private void CustomersGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            var customer = CustomersGrid.SelectedItem as Customer;

            if (customer == null)
                return;

            CustomerCodeTextBox.Text = customer.CustomerCode;
            CustomerNameTextBox.Text = customer.Name;
            CustomerPhoneTextBox.Text = customer.Phone;
            CustomerAddressTextBox.Text = customer.Address;
            CustomerNotesTextBox.Text = customer.Notes;

            ToggleCustomerButton.Content = customer.IsActive
                ? "إيقاف العميل"
                : "تنشيط العميل";
        }

        #endregion

        #region New Customer

        private void NewCustomerButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CustomersGrid.SelectedItem = null;
            ClearForm();
        }

        private void ClearForm()
        {
            CustomerCodeTextBox.Text = "يتم توليده تلقائيًا";
            CustomerNameTextBox.Clear();
            CustomerPhoneTextBox.Clear();
            CustomerAddressTextBox.Clear();
            CustomerNotesTextBox.Clear();

            ToggleCustomerButton.Content = "إيقاف / تنشيط العميل";

            CustomerNameTextBox.Focus();
        }

        #endregion

        #region Add Customer

        private void AddCustomerButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CustomerNameTextBox.Text))
            {
                MessageBox.Show(
                    "يرجى إدخال اسم العميل.",
                    "بيانات العميل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                CustomerNameTextBox.Focus();
                return;
            }

            try
            {
                var customer = new Customer
                {
                    Name = CustomerNameTextBox.Text.Trim(),
                    Phone = CustomerPhoneTextBox.Text.Trim(),
                    Address = CustomerAddressTextBox.Text.Trim(),
                    Notes = CustomerNotesTextBox.Text.Trim()
                };

                Customer addedCustomer =
                    customerService.AddCustomer(
                        currentUserId,
                        customer);

                LoadCustomers();

                SelectCustomer(addedCustomer.Id);

                MessageBox.Show(
                    "تم إضافة العميل بنجاح.\n\n"
                    + "كود العميل: " + addedCustomer.CustomerCode,
                    "إدارة العملاء",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر إضافة العميل:\n\n" + ex.Message,
                    "إدارة العملاء",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Update Customer

        private void UpdateCustomerButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedCustomer =
                CustomersGrid.SelectedItem as Customer;

            if (selectedCustomer == null)
            {
                MessageBox.Show(
                    "اختر عميلًا من الجدول أولًا.",
                    "تعديل العميل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrWhiteSpace(CustomerNameTextBox.Text))
            {
                MessageBox.Show(
                    "اسم العميل مطلوب.",
                    "بيانات العميل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                selectedCustomer.Name =
                    CustomerNameTextBox.Text.Trim();

                selectedCustomer.Phone =
                    CustomerPhoneTextBox.Text.Trim();

                selectedCustomer.Address =
                    CustomerAddressTextBox.Text.Trim();

                selectedCustomer.Notes =
                    CustomerNotesTextBox.Text.Trim();

                customerService.UpdateCustomer(
                    currentUserId,
                    selectedCustomer);

                LoadCustomers();
                SelectCustomer(selectedCustomer.Id);

                MessageBox.Show(
                    "تم حفظ تعديلات العميل بنجاح.",
                    "إدارة العملاء",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تعديل بيانات العميل:\n\n" + ex.Message,
                    "إدارة العملاء",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Activate / Deactivate

        private void ToggleCustomerButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var customer =
                CustomersGrid.SelectedItem as Customer;

            if (customer == null)
            {
                MessageBox.Show(
                    "اختر عميلًا من الجدول أولًا.",
                    "إدارة العملاء",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            bool newStatus = !customer.IsActive;

            string actionText = newStatus
                ? "تنشيط"
                : "إيقاف";

            var result = MessageBox.Show(
                "هل تريد " + actionText + " العميل:\n\n"
                + customer.Name + " ؟",
                "تأكيد العملية",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                customerService.SetCustomerActive(
                    currentUserId,
                    customer.Id,
                    newStatus);

                LoadCustomers();
                SelectCustomer(customer.Id);

                MessageBox.Show(
                    newStatus
                        ? "تم تنشيط العميل بنجاح."
                        : "تم إيقاف العميل بنجاح.",
                    "إدارة العملاء",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تغيير حالة العميل:\n\n" + ex.Message,
                    "إدارة العملاء",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Customer Account

        private void CustomerAccountButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var customer =
                CustomersGrid.SelectedItem as Customer;

            if (customer == null)
            {
                MessageBox.Show(
                    "اختر العميل أولًا لفتح كشف حسابه.",
                    "حساب العميل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var window = new CustomerAccountWindow(
                    currentUserId,
                    customer);

                window.Owner = this;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر فتح حساب العميل:\n\n" + ex.Message,
                    "حساب العميل",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Helpers

        private void SelectCustomer(int customerId)
        {
            foreach (var customer in customers)
            {
                if (customer.Id == customerId)
                {
                    CustomersGrid.SelectedItem = customer;
                    CustomersGrid.ScrollIntoView(customer);
                    break;
                }
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
