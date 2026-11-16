
using Ocore.desktop.Models;
using Ocore.desktop.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Ocore.desktop.Views
{
    public partial class SalesReturnsWindow : Window
    {
        private readonly int currentUserId;
        private readonly SalesReturnsService returnsService;

        private bool isLoadingInvoice;
        private bool isOpeningInvoice;

        public SalesReturnsWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            returnsService = new SalesReturnsService();
        }

        // ==========================================
        // البحث أثناء الكتابة
        // ==========================================

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (IsLoaded)
                SearchReturnableItems();
        }

        // ==========================================
        // زر البحث
        // ==========================================

        private void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SearchReturnableItems();
        }

        // ==========================================
        // تحميل الفواتير المطابقة
        // ==========================================

        private void SearchReturnableItems()
        {
            try
            {
                isLoadingInvoice = true;

                InvoicesDataGrid.SelectedItem = null;
                InvoicesDataGrid.ItemsSource = null;

                string searchText =
                    (SearchTextBox.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(searchText))
                {
                    ItemsStatusText.Text =
                        "اكتب رقم الفاتورة أو اسم الصنف أو كوده للبحث.";

                    return;
                }

                List<SalesReturnDetails> invoices =
                    returnsService.GetInvoicesForReturn(
                        currentUserId,
                        searchText);

                InvoicesDataGrid.ItemsSource = invoices;

                if (invoices.Count == 0)
                {
                    ItemsStatusText.Text =
                        "لا توجد فواتير مكتملة مطابقة للبحث وبها كميات متاحة للمرتجع.";

                    return;
                }

                ItemsStatusText.Text =
                    "عدد الفواتير المطابقة: " +
                    invoices.Count +
                    " | انقر مرتين على الفاتورة لفتح تفاصيلها وتنفيذ المرتجع.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "خطأ في البحث",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                isLoadingInvoice = false;
            }
        }

        // ==========================================
        // فتح نافذة تفاصيل الفاتورة بالنقر المزدوج
        // ==========================================

        private void InvoicesDataGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (isLoadingInvoice || isOpeningInvoice)
                return;

            // التأكد أن النقر المزدوج حدث على صف فاتورة،
            // وليس على مساحة فارغة داخل الجدول.
            DependencyObject source =
                e.OriginalSource as DependencyObject;

            DataGridRow clickedRow =
                FindVisualParent<DataGridRow>(source);

            if (clickedRow == null)
                return;

            SalesReturnDetails invoice =
                clickedRow.Item as SalesReturnDetails;

            if (invoice == null)
                return;

            isOpeningInvoice = true;

            try
            {
                InvoicesDataGrid.SelectedItem = invoice;

                SalesReturnInvoiceWindow invoiceWindow =
                    new SalesReturnInvoiceWindow(
                        currentUserId,
                        invoice);

                invoiceWindow.Owner = this;

                bool? result = invoiceWindow.ShowDialog();

                // إزالة التحديد حتى يمكن فتح نفس الفاتورة مرة أخرى.
                InvoicesDataGrid.SelectedItem = null;

                // تحديث نتائج البحث بعد نجاح حفظ المرتجع فقط.
                if (result == true)
                {
                    SearchReturnableItems();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "خطأ في فتح الفاتورة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                isOpeningInvoice = false;
            }
        }

        // ==========================================
        // البحث عن العنصر الأب في شجرة عناصر WPF
        // ==========================================

        private T FindVisualParent<T>(
            DependencyObject child)
            where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T)
                    return (T)child;

                child =
                    VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        // ==========================================
        // إغلاق نافذة البحث
        // ==========================================

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
