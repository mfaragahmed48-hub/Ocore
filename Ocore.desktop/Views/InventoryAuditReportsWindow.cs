using System;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.Win32;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public sealed class InventoryAuditReportsWindow : Window
    {
        private readonly InventoryAuditQueryService queryService;
        private readonly int sessionId;
        private readonly int managerId;

        private readonly TextBlock summaryText;
        private readonly DataGrid varianceGrid;
        private readonly DataGrid decisionsGrid;
        private readonly DataGrid comparisonGrid;
        private readonly DataGrid repeatedGrid;

        public InventoryAuditReportsWindow(int selectedSessionId, int currentManagerId)
        {
            if (selectedSessionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(selectedSessionId));

            if (currentManagerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(currentManagerId));

            sessionId = selectedSessionId;
            managerId = currentManagerId;
            queryService = new InventoryAuditQueryService();

            Title = "تقارير الجرد والفروق المالية";
            Width = 1180;
            Height = 760;
            MinWidth = 900;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FlowDirection = FlowDirection.RightToLeft;
            Background = new SolidColorBrush(Color.FromRgb(243, 246, 248));

            var root = new DockPanel { Margin = new Thickness(14) };

            var header = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 10),
                LastChildFill = false
            };

            summaryText = new TextBlock
            {
                Margin = new Thickness(4, 4, 4, 8),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(18, 61, 53))
            };

            header.Children.Add(summaryText);

            var reportActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            reportActions.Children.Add(MakeButton("تصدير Excel", ExportExcel));
            reportActions.Children.Add(MakeButton("طباعة", PrintReport));

            DockPanel.SetDock(reportActions, Dock.Right);
            header.Children.Add(reportActions);

            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var tabs = new TabControl();

            varianceGrid = CreateGrid();
            decisionsGrid = CreateGrid();
            comparisonGrid = CreateGrid();
            repeatedGrid = CreateGrid();

            tabs.Items.Add(MakeTab("ملخص الفروق", varianceGrid));
            tabs.Items.Add(MakeTab("تفاصيل قرارات التكلفة", decisionsGrid));
            tabs.Items.Add(MakeTab("مقارنة بالجرد السابق", comparisonGrid));
            tabs.Items.Add(MakeTab("الأهداف المتكرر بها العجز", repeatedGrid));

            root.Children.Add(tabs);

            Content = root;
            Loaded += (sender, args) => LoadReports();
        }

        private static Button MakeButton(string caption, Action action)
        {
            var button = new Button
            {
                Content = caption,
                Height = 34,
                MinWidth = 95,
                Margin = new Thickness(3),
                Padding = new Thickness(9, 4, 9, 4),
                Background = new SolidColorBrush(Color.FromRgb(31, 105, 85)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold
            };

            button.Click += (sender, args) => action();
            return button;
        }

        private void ExportExcel()
        {
            var dialog = new SaveFileDialog
            {
                Title = "حفظ تقرير الجرد",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = "InventoryAudit_" + sessionId + ".xlsx",
                AddExtension = true,
                DefaultExt = ".xlsx"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                using (var workbook = new XLWorkbook())
                {
                    var summarySheet = workbook.Worksheets.Add("ملخص الجرد");
                    summarySheet.Cell(1, 1).Value = "تقرير الجرد رقم " + sessionId;
                    summarySheet.Cell(3, 1).InsertTable(
                        queryService.GetVarianceSummary(sessionId, managerId));
                    summarySheet.Columns().AdjustToContents();

                    var varianceSheet = workbook.Worksheets.Add("الفروق");
                    varianceSheet.Cell(1, 1).Value = "الفروق المعتمدة";
                    varianceSheet.Cell(3, 1).InsertTable(
                        queryService.GetVarianceReport(sessionId, managerId));
                    varianceSheet.Columns().AdjustToContents();

                    var decisionsSheet = workbook.Worksheets.Add("قرارات التكلفة");
                    decisionsSheet.Cell(1, 1).Value =
                        "تفاصيل قرارات تقييم العجز والزيادة";
                    decisionsSheet.Cell(3, 1).InsertTable(
                        queryService.GetCostDecisionDetails(sessionId, managerId));
                    decisionsSheet.Columns().AdjustToContents();

                    var comparisonSheet = workbook.Worksheets.Add("مقارنة السابق");
                    comparisonSheet.Cell(1, 1).Value =
                        "مقارنة الهدف بآخر جرد سابق مغلق";
                    comparisonSheet.Cell(3, 1).InsertTable(
                        queryService.GetPreviousCountComparison(sessionId, managerId));
                    comparisonSheet.Columns().AdjustToContents();

                    var repeatedSheet = workbook.Worksheets.Add("العجز المتكرر");
                    repeatedSheet.Cell(1, 1).Value =
                        "الأهداف التي تجاوزت حد تكرار العجز";
                    repeatedSheet.Cell(3, 1).InsertTable(
                        queryService.GetRepeatedShortages(managerId));
                    repeatedSheet.Columns().AdjustToContents();

                    workbook.SaveAs(dialog.FileName);
                }

                MessageBox.Show(
                    this,
                    "تم تصدير تقرير الجرد إلى Excel.",
                    "تم التصدير",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "تعذر تصدير التقرير: " + ex.Message,
                    "خطأ في التصدير",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void PrintReport()
        {
            try
            {
                var dialog = new PrintDialog();

                if (dialog.ShowDialog() == true)
                    dialog.PrintVisual(this, "تقرير الجرد رقم " + sessionId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "تعذرت طباعة التقرير: " + ex.Message,
                    "خطأ في الطباعة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LoadReports()
        {
            DataTable summary =
                queryService.GetVarianceSummary(sessionId, managerId);

            if (summary.Rows.Count > 0)
            {
                DataRow row = summary.Rows[0];

                summaryText.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "الأهداف المعتمدة: {0}   |   كمية العجز: {1:N2}   |   قيمة العجز: {2:N2}   |   كمية الزيادة: {3:N2}   |   قيمة الزيادة: {4:N2}",
                    Value(row, "ApprovedTargets"),
                    Value(row, "ShortageQuantity"),
                    Value(row, "ShortageValue"),
                    Value(row, "ExcessQuantity"),
                    Value(row, "ExcessValue"));
            }

            varianceGrid.ItemsSource =
                queryService.GetVarianceReport(sessionId, managerId).DefaultView;

            decisionsGrid.ItemsSource =
                queryService.GetCostDecisionDetails(sessionId, managerId).DefaultView;

            comparisonGrid.ItemsSource =
                queryService.GetPreviousCountComparison(sessionId, managerId).DefaultView;

            repeatedGrid.ItemsSource =
                queryService.GetRepeatedShortages(managerId).DefaultView;
        }

        private static double Value(DataRow row, string column)
        {
            return row[column] == DBNull.Value
                ? 0
                : Convert.ToDouble(row[column]);
        }

        private static DataGrid CreateGrid()
        {
            return new DataGrid
            {
                AutoGenerateColumns = true,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeight = 32,
                FontSize = 12,
                Margin = new Thickness(4)
            };
        }

        private static TabItem MakeTab(string title, UIElement content)
        {
            return new TabItem
            {
                Header = title,
                Content = content,
                Padding = new Thickness(10, 6, 10, 6)
            };
        }
    }
}