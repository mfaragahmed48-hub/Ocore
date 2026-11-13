
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class SuppliersManagementWindow : Window
    {
        private readonly int currentUserId;
        private readonly SupplierService supplierService;

        private List<Supplier> allSuppliers;
        private int editingSupplierId = 0;

        public SuppliersManagementWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            supplierService = new SupplierService();

            allSuppliers = new List<Supplier>();

            LoadSuppliers();
        }

        private void LoadSuppliers()
        {
            try
            {
                bool includeInactive =
                    SupplierFilterComboBox.SelectedIndex == 1;

                allSuppliers = supplierService.GetSuppliers(
                    currentUserId,
                    includeInactive);

                ApplySearchFilter();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل الموردين:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ApplySearchFilter()
        {
            if (allSuppliers == null)
                return;

            string search = SearchTextBox.Text.Trim();

            var filtered = allSuppliers
                .Where(supplier =>
                    string.IsNullOrWhiteSpace(search) ||
                    (supplier.Name ?? "").IndexOf(
                        search,
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (supplier.Phone ?? "").IndexOf(
                        search,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            SuppliersDataGrid.ItemsSource = null;
            SuppliersDataGrid.ItemsSource = filtered;
        }

        private void SaveSupplier_Click(
            object sender,
            RoutedEventArgs e)
        {
            string name = SupplierNameTextBox.Text.Trim();
            string phone = SupplierPhoneTextBox.Text.Trim();
            string address = SupplierAddressTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(
                    "يرجى إدخال اسم المورد.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                SupplierNameTextBox.Focus();
                return;
            }

            try
            {
                if (editingSupplierId == 0)
                {
                    supplierService.AddSupplier(
                        currentUserId,
                        name,
                        phone,
                        address);

                    MessageBox.Show(
                        "تمت إضافة المورد بنجاح.",
                        "نجاح",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    supplierService.UpdateSupplier(
                        currentUserId,
                        editingSupplierId,
                        name,
                        phone,
                        address);

                    MessageBox.Show(
                        "تم تعديل بيانات المورد بنجاح.",
                        "نجاح",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                ResetForm();
                LoadSuppliers();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر حفظ بيانات المورد:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void EditSupplier_Click(
            object sender,
            RoutedEventArgs e)
        {
            var button = sender as Button;
            var supplier = button?.DataContext as Supplier;

            if (supplier == null)
                return;

            editingSupplierId = supplier.Id;

            SupplierNameTextBox.Text = supplier.Name;
            SupplierPhoneTextBox.Text = supplier.Phone;
            SupplierAddressTextBox.Text = supplier.Address;

            FormTitleTextBlock.Text = "تعديل بيانات المورد";
            SaveSupplierButton.Content = "حفظ التعديلات";
            CancelEditButton.Visibility = Visibility.Visible;

            SupplierNameTextBox.Focus();
        }

        private void ChangeSupplierStatus_Click(
            object sender,
            RoutedEventArgs e)
        {
            var button = sender as Button;
            var supplier = button?.DataContext as Supplier;

            if (supplier == null)
                return;

            bool newStatus = !supplier.IsActive;

            string message = newStatus
                ? "هل تريد إعادة تفعيل هذا المورد؟"
                : "هل تريد إيقاف هذا المورد؟";

            if (MessageBox.Show(
                message,
                "تأكيد العملية",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                supplierService.SetSupplierActive(
                    currentUserId,
                    supplier.Id,
                    newStatus);

                MessageBox.Show(
                    "تم تغيير حالة المورد بنجاح.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ResetForm();
                LoadSuppliers();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تغيير حالة المورد:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void SupplierFilterComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                LoadSuppliers();
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadSuppliers();
        }

        private void CancelEdit_Click(
            object sender,
            RoutedEventArgs e)
        {
            ResetForm();
        }

        private void ResetForm()
        {
            editingSupplierId = 0;

            SupplierNameTextBox.Clear();
            SupplierPhoneTextBox.Clear();
            SupplierAddressTextBox.Clear();

            FormTitleTextBlock.Text = "إضافة مورد جديد";
            SaveSupplierButton.Content = "➕ إضافة المورد";

            CancelEditButton.Visibility = Visibility.Collapsed;
        }
    }
}
