
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public class ExpiryAlertsWindow : Window
    {
        private readonly DataGrid alertsGrid;
        private readonly TextBlock summaryText;

        public ExpiryAlertsWindow(int userId)
        {
            Title = "تنبيهات صلاحية المخزون";
            Width = 1100;
            Height = 650;
            MinWidth = 850;
            MinHeight = 450;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FlowDirection = FlowDirection.RightToLeft;
            Background = new SolidColorBrush(
                Color.FromRgb(243, 246, 248));

            var root = new Grid
            {
                Margin = new Thickness(18)
            };

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            root.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var headerStack = new StackPanel();

            headerStack.Children.Add(new TextBlock
            {
                Text = "تنبيهات صلاحية المخزون",
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(24, 63, 54))
            });

            summaryText = new TextBlock
            {
                Text = "جاري تحميل التشغيلات...",
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = new SolidColorBrush(
                    Color.FromRgb(100, 115, 111))
            };

            headerStack.Children.Add(summaryText);
            header.Child = headerStack;

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            alertsGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = true,
                CanUserResizeRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeaderWidth = 0,
                FontSize = 14,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(
                    Color.FromRgb(220, 232, 227)),
                BorderThickness = new Thickness(1),
                RowBackground = Brushes.White,
                AlternatingRowBackground = new SolidColorBrush(
                    Color.FromRgb(248, 250, 249))
            };

            alertsGrid.Columns.Add(CreateColumn(
                "اسم الصنف",
                "ProductName",
                2.0));

            alertsGrid.Columns.Add(CreateColumn(
                "كود الصنف",
                "ProductCode",
                1.0));

            alertsGrid.Columns.Add(CreateColumn(
                "رقم التشغيلة",
                "BatchNumber",
                1.2));

            alertsGrid.Columns.Add(CreateColumn(
                "تاريخ الصلاحية",
                "ExpiryDateText",
                1.0));

            alertsGrid.Columns.Add(CreateColumn(
                "الحالة",
                "StatusText",
                1.2));

            alertsGrid.Columns.Add(CreateColumn(
                "الأيام المتبقية",
                "DaysRemainingText",
                1.3));

            alertsGrid.Columns.Add(CreateColumn(
                "الكمية المتبقية",
                "Quantity",
                1.0,
                "N2"));

            alertsGrid.LoadingRow += AlertsGrid_LoadingRow;

            Grid.SetRow(alertsGrid, 1);
            root.Children.Add(alertsGrid);

            Content = root;

            LoadAlerts(userId);
        }

        private DataGridTextColumn CreateColumn(
            string header,
            string bindingPath,
            double width,
            string stringFormat = null)
        {
            var binding = new Binding(bindingPath);

            if (!string.IsNullOrWhiteSpace(stringFormat))
                binding.StringFormat = stringFormat;

            return new DataGridTextColumn
            {
                Header = header,
                Binding = binding,
                Width = new DataGridLength(
                    width,
                    DataGridLengthUnitType.Star)
            };
        }

        private void LoadAlerts(int userId)
        {
            try
            {
                var service = new ExpiryAlertService();

                var alerts = service.GetExpiryAlerts(userId);

                alertsGrid.ItemsSource = alerts;

                summaryText.Text =
                    "إجمالي التشغيلات التي تحتاج إلى متابعة: " +
                    alerts.Count.ToString("N0");

                if (alerts.Count == 0)
                {
                    summaryText.Text =
                        "لا توجد تشغيلات منتهية أو قاربت على انتهاء الصلاحية خلال 90 يومًا.";
                }
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
                Ocore.desktop.ErrorDialog.Show("تحميل تنبيهات الصلاحية", ex);
                Close();
            }
        }

        private void AlertsGrid_LoadingRow(
            object sender,
            DataGridRowEventArgs e)
        {
            var item = e.Row.Item as ExpiryAlertBatch;

            if (item == null)
                return;

            if (item.DaysRemaining < 0)
            {
                e.Row.Background = new SolidColorBrush(
                    Color.FromRgb(255, 225, 225));

                e.Row.Foreground = new SolidColorBrush(
                    Color.FromRgb(150, 35, 35));
            }
            else if (item.DaysRemaining <= 30)
            {
                e.Row.Background = new SolidColorBrush(
                    Color.FromRgb(255, 242, 215));

                e.Row.Foreground = new SolidColorBrush(
                    Color.FromRgb(145, 90, 15));
            }
        }
    }
}
