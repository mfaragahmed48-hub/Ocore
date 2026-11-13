using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class PurchaseInvoicesWindow : Window
    {
        private readonly int currentUserId;
        private readonly PurchaseService purchaseService;

        private bool isResetting;

        public PurchaseInvoicesWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            purchaseService = new PurchaseService();

            Loaded += PurchaseInvoicesWindow_Loaded;
        }

        private void PurchaseInvoicesWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            LoadInvoices();
        }

        // تحميل الفواتير وحساب الإجمالي
        private void LoadInvoices()
        {
            try
            {
                DateTime? fromDate = FromDatePicker.SelectedDate;
                DateTime? toDate = ToDatePicker.SelectedDate;

                var invoices = purchaseService.GetInvoices(
                    currentUserId,
                    SearchTextBox.Text.Trim(),
                    fromDate,
                    toDate);

                InvoicesDataGrid.ItemsSource = invoices;

                double totalAmount = 0;

                foreach (var invoice in invoices)
                {
                    totalAmount += invoice.Total;
                }

                InvoicesCountText.Text =
                    "عدد الفواتير: " + invoices.Count;

                InvoicesTotalText.Text =
                    "إجمالي القيمة: " + totalAmount.ToString("N2");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل فواتير الشراء:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // البحث أثناء الكتابة
        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (IsLoaded && !isResetting)
            {
                LoadInvoices();
            }
        }

        // البحث
        private void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadInvoices();
        }

        // تغيير التاريخ
        private void DatePicker_SelectedDateChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (IsLoaded && !isResetting)
            {
                LoadInvoices();
            }
        }

        // إعادة ضبط البحث والتواريخ
        private void ResetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            isResetting = true;

            SearchTextBox.Clear();
            FromDatePicker.SelectedDate = null;
            ToDatePicker.SelectedDate = null;

            isResetting = false;

            LoadInvoices();
        }

        // تحديث البيانات
        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadInvoices();
        }

        // فتح تفاصيل الفاتورة بالنقر المزدوج
        private void InvoicesDataGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            OpenSelectedInvoice();
        }

        // فتح التفاصيل من الزر
        private void ViewInvoiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenSelectedInvoice();
        }

        // التحقق من الفاتورة المختارة وفتح التفاصيل
        private void OpenSelectedInvoice()
        {
            try
            {
                var selectedInvoice =
                    InvoicesDataGrid.SelectedItem as PurchaseInvoice;

                if (selectedInvoice == null)
                {
                    MessageBox.Show(
                        "من فضلك اختر فاتورة أولاً.",
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                var window = new PurchaseInvoiceDetailsWindow(
                    currentUserId,
                    selectedInvoice.Id);

                window.Owner = this;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح الفاتورة:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}