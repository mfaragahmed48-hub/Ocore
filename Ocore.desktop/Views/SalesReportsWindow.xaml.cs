
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Ocore.desktop.Services;
using Ocore.desktop.Models;

namespace Ocore.desktop.Views
{
    public partial class SalesReportsWindow : Window
    {
        private readonly int currentUserId;
        private readonly SalesReportsService reportsService;

        private List<SalesDailySales> currentDailySales;

        public SalesReportsWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            reportsService = new SalesReportsService();

            currentDailySales = new List<SalesDailySales>();

            FromDatePicker.SelectedDate = DateTime.Today;
            ToDatePicker.SelectedDate = DateTime.Today;

            Loaded += SalesReportsWindow_Loaded;
            DailySalesChartCanvas.SizeChanged +=
                DailySalesChartCanvas_SizeChanged;
        }

        private void SalesReportsWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            LoadReport();
        }

        private void LoadReportButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadReport();
        }

        private void LoadReport()
        {
            try
            {
                if (!FromDatePicker.SelectedDate.HasValue ||
                    !ToDatePicker.SelectedDate.HasValue)
                {
                    MessageBox.Show(
                        "من فضلك اختر تاريخ البداية والنهاية.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                DateTime fromDate =
                    FromDatePicker.SelectedDate.Value;

                DateTime toDate =
                    ToDatePicker.SelectedDate.Value;

                if (toDate.Date < fromDate.Date)
                {
                    MessageBox.Show(
                        "تاريخ النهاية يجب ألا يسبق تاريخ البداية.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var summary = reportsService.GetSummary(
                    currentUserId,
                    fromDate,
                    toDate);

                var topProducts =
                    reportsService.GetTopSellingProducts(
                        currentUserId,
                        fromDate,
                        toDate);

                var employees =
                    reportsService.GetEmployeePerformance(
                        currentUserId,
                        fromDate,
                        toDate);

                var dailySales =
                    reportsService.GetDailySales(
                        currentUserId,
                        fromDate,
                        toDate);

                TotalSalesText.Text =
                    summary.TotalSales.ToString("N2") + " ج.م";

                InvoiceCountText.Text =
                    summary.InvoiceCount.ToString("N0");

                TotalCostText.Text =
                    summary.IsCostComplete
                    ? summary.TotalCost.ToString("N2") + " ج.م"
                    : "غير مكتملة";

                NetProfitText.Text =
                    summary.IsCostComplete
                    ? summary.NetProfit.ToString("N2") + " ج.م"
                    : "غير متاح";

                NetProfitText.Foreground =
                    summary.IsCostComplete
                    ? new SolidColorBrush(
                        Color.FromRgb(22, 163, 74))
                    : new SolidColorBrush(
                        Color.FromRgb(220, 38, 38));

                TopProductsDataGrid.ItemsSource = topProducts;
                EmployeesDataGrid.ItemsSource = employees;

                currentDailySales = dailySales ??
                    new List<SalesDailySales>();

                DrawDailySalesChart(currentDailySales);

                if (summary.IsCostComplete)
                {
                    ReportStatusText.Text =
                        "تم تحميل التقرير بنجاح.";

                    ReportStatusText.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(22, 163, 74));
                }
                else
                {
                    ReportStatusText.Text =
                        "تنبيه: توجد أصناف لم تكتمل بيانات تكلفة تشغيلاتها؛ " +
                        "لذلك لم يتم احتساب صافي الربح.";

                    ReportStatusText.Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(220, 38, 38));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "صلاحيات غير كافية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل تقرير المبيعات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // رسم المبيعات اليومية
        private void DrawDailySalesChart(
            List<SalesDailySales> dailySales)
        {
            DailySalesChartCanvas.Children.Clear();

            if (dailySales == null || dailySales.Count == 0)
            {
                DailySalesEmptyText.Visibility = Visibility.Visible;
                return;
            }

            double chartWidth = DailySalesChartCanvas.ActualWidth;
            double chartHeight = DailySalesChartCanvas.ActualHeight;

            if (chartWidth <= 0 || chartHeight <= 0)
            {
                DailySalesEmptyText.Visibility = Visibility.Visible;
                return;
            }

            DailySalesEmptyText.Visibility = Visibility.Collapsed;

            double leftMargin = 55;
            double rightMargin = 15;
            double topMargin = 35;
            double bottomMargin = 40;

            double drawingWidth =
                chartWidth - leftMargin - rightMargin;

            double drawingHeight =
                chartHeight - topMargin - bottomMargin;

            if (drawingWidth <= 0 || drawingHeight <= 0)
                return;

            double maxSales = 0;

            foreach (var item in dailySales)
            {
                if (item.TotalSales > maxSales)
                    maxSales = item.TotalSales;
            }

            if (maxSales <= 0)
                maxSales = 1;

            maxSales = maxSales * 1.2;

            // خطوط الشبكة والقيم
            for (int i = 0; i <= 4; i++)
            {
                double y =
                    topMargin + (drawingHeight / 4) * i;

                double value =
                    maxSales - (maxSales / 4) * i;

                Line gridLine = new Line
                {
                    X1 = leftMargin,
                    Y1 = y,
                    X2 = chartWidth - rightMargin,
                    Y2 = y,
                    Stroke = new SolidColorBrush(
                        Color.FromRgb(226, 232, 240)),
                    StrokeThickness = 1
                };

                DailySalesChartCanvas.Children.Add(gridLine);

                TextBlock valueText = new TextBlock
                {
                    Text = value.ToString("N0"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(100, 116, 139)),
                    Width = leftMargin - 8,
                    TextAlignment = TextAlignment.Right
                };

                Canvas.SetLeft(valueText, 0);
                Canvas.SetTop(valueText, y - 7);

                DailySalesChartCanvas.Children.Add(valueText);
            }

            double slotWidth =
                drawingWidth / dailySales.Count;

            double barWidth =
                Math.Max(5, Math.Min(35, slotWidth * 0.55));

            for (int i = 0; i < dailySales.Count; i++)
            {
                var item = dailySales[i];

                double barHeight =
                    (item.TotalSales / maxSales) * drawingHeight;

                double x =
                    leftMargin +
                    (slotWidth * i) +
                    ((slotWidth - barWidth) / 2);

                double y =
                    topMargin + drawingHeight - barHeight;

                Rectangle bar = new Rectangle
                {
                    Width = barWidth,
                    Height = Math.Max(2, barHeight),
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = new SolidColorBrush(
                        Color.FromRgb(37, 99, 235)),
                    ToolTip =
                        item.SaleDate + "\n" +
                        item.TotalSales.ToString("N2") + " ج.م"
                };

                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);

                DailySalesChartCanvas.Children.Add(bar);

                // قيمة المبيعات أعلى العمود
                if (slotWidth >= 45)
                {
                    TextBlock salesValue = new TextBlock
                    {
                        Text = item.TotalSales.ToString("N0"),
                        FontSize = 9,
                        Foreground = new SolidColorBrush(
                            Color.FromRgb(51, 65, 85)),
                        Width = slotWidth,
                        TextAlignment = TextAlignment.Center
                    };

                    Canvas.SetLeft(
                        salesValue,
                        leftMargin + (slotWidth * i));

                    Canvas.SetTop(
                        salesValue,
                        Math.Max(0, y - 18));

                    DailySalesChartCanvas.Children.Add(salesValue);
                }

                // تاريخ اليوم أسفل العمود
                DateTime parsedDate;

                string dateLabel = item.SaleDate;

                if (DateTime.TryParseExact(
                    item.SaleDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out parsedDate))
                {
                    dateLabel = parsedDate.ToString("MM/dd");
                }

                TextBlock dateText = new TextBlock
                {
                    Text = dateLabel,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(100, 116, 139)),
                    Width = slotWidth,
                    TextAlignment = TextAlignment.Center
                };

                Canvas.SetLeft(
                    dateText,
                    leftMargin + (slotWidth * i));

                Canvas.SetTop(
                    dateText,
                    topMargin + drawingHeight + 8);

                DailySalesChartCanvas.Children.Add(dateText);
            }
        }

        // إعادة رسم الرسم البياني عند تغيير حجمه
        private void DailySalesChartCanvas_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            if (currentDailySales == null ||
                currentDailySales.Count == 0)
            {
                return;
            }

            DrawDailySalesChart(currentDailySales);
        }
    }
}
