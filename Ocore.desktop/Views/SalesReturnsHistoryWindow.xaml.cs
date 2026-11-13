
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class SalesReturnsHistoryWindow : Window
    {
        private readonly int currentUserId;
        private readonly SalesReturnsHistoryService historyService;

        private List<SalesReturnHistoryItem> currentReturns;

        public SalesReturnsHistoryWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;

            historyService = new SalesReturnsHistoryService();

            currentReturns = new List<SalesReturnHistoryItem>();

            LoadReturns();
        }

        // ==========================================
        // تحميل سجل المرتجعات
        // ==========================================

        private void LoadReturns()
        {
            try
            {
                DateTime? fromDate = FromDatePicker.SelectedDate;
                DateTime? toDate = ToDatePicker.SelectedDate;

                if (fromDate.HasValue &&
                    toDate.HasValue &&
                    fromDate.Value.Date > toDate.Value.Date)
                {
                    MessageBox.Show(
                        "تاريخ البداية يجب أن يكون قبل تاريخ النهاية.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                currentReturns = historyService.GetReturns(
                    currentUserId,
                    SearchTextBox.Text,
                    fromDate,
                    toDate);

                ReturnsDataGrid.ItemsSource = null;
                ReturnsDataGrid.ItemsSource = currentReturns;

                UpdateSummary();

                StatusText.Text =
                    "عدد النتائج: " + currentReturns.Count;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل سجل المرتجعات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text = "تعذر تحميل البيانات.";
            }
        }

        // ==========================================
        // تحديث الملخص
        // ==========================================

        private void UpdateSummary()
        {
            ReturnsCountText.Text =
                currentReturns.Count.ToString();

            double total = currentReturns.Sum(x => x.Total);

            ReturnsTotalText.Text =
                total.ToString("N2") + " ج.م";
        }

        // ==========================================
        // البحث
        // ==========================================

        private void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadReturns();
        }

        // ==========================================
        // إعادة ضبط البحث
        // ==========================================

        private void ResetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SearchTextBox.Clear();

            FromDatePicker.SelectedDate = null;
            ToDatePicker.SelectedDate = null;

            LoadReturns();
        }

        // ==========================================
        // فتح تفاصيل المرتجع
        // ==========================================

        private void ReturnsDataGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            SalesReturnHistoryItem selected =
                ReturnsDataGrid.SelectedItem as SalesReturnHistoryItem;

            if (selected == null)
                return;

            OpenReturnDetails(selected.Id);
        }

        private void OpenReturnDetails(int returnId)
        {
            try
            {
                SalesReturnDetails details =
                    historyService.GetReturnDetails(
                        currentUserId,
                        returnId);

                if (details == null)
                {
                    MessageBox.Show(
                        "لم يتم العثور على تفاصيل المرتجع.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                SalesReturnDetailsWindow window =
                    new SalesReturnDetailsWindow(details);

                window.Owner = this;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر فتح تفاصيل المرتجع:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================================
        // إغلاق الشاشة
        // ==========================================

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
