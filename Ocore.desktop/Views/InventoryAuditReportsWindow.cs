using System;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                using (var workbook = new XLWorkbook())
                {
                    DataTable summary = queryService.GetVarianceSummary(sessionId, managerId);
                    DataTable variance = queryService.GetVarianceReport(sessionId, managerId);
                    DataTable decisions = queryService.GetCostDecisionDetails(sessionId, managerId);
                    DataTable comparison = queryService.GetPreviousCountComparison(sessionId, managerId);
                    DataTable repeated = queryService.GetRepeatedShortages(managerId);

                    AddExportSheet(workbook, "ملخص الجرد", "تقرير الجرد رقم " + sessionId, summary);
                    AddExportSheet(workbook, "الفروق", "الفروق المعتمدة - الجرد رقم " + sessionId, variance);
                    AddExportSheet(workbook, "قرارات التكلفة", "تفاصيل قرارات تقييم العجز والزيادة", decisions);
                    AddExportSheet(workbook, "مقارنة السابق", "مقارنة الهدف بآخر جرد سابق مغلق", comparison);
                    AddExportSheet(workbook, "العجز المتكرر", "الأهداف التي تجاوزت حد تكرار العجز", repeated);

                    workbook.SaveAs(dialog.FileName);
                }

                MessageBox.Show(this, "تم تصدير تقرير الجرد إلى Excel.", "تم التصدير",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "تعذر تصدير التقرير: " + ex.Message, "خطأ في التصدير",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void AddExportSheet(
            XLWorkbook workbook, string sheetName, string title, DataTable data)
        {
            var sheet = workbook.Worksheets.Add(sheetName);
            int columnCount = Math.Max(1, data.Columns.Count);

            sheet.RightToLeft = true;
            sheet.Cell(1, 1).Value = title;
            sheet.Range(1, 1, 1, columnCount).Merge();
            sheet.Row(1).Height = 30;

            var titleRange = sheet.Range(1, 1, 1, columnCount);
            titleRange.Style.Font.Bold = true;
            titleRange.Style.Font.FontSize = 16;
            titleRange.Style.Font.FontColor = XLColor.White;
            titleRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F6955");
            titleRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            if (data.Columns.Count == 0)
            {
                sheet.Cell(3, 1).Value = "لا توجد بيانات لهذا التقرير.";
                sheet.Cell(3, 1).Style.Font.Italic = true;
                sheet.Column(1).Width = 36;
            }
            else
            {
                string tableName = "AuditReport" + workbook.Worksheets.Count.ToString(CultureInfo.InvariantCulture);
                sheet.Cell(3, 1).InsertTable(data, tableName, true);
                int lastRow = Math.Max(3, data.Rows.Count + 3);
                var header = sheet.Range(3, 1, 3, data.Columns.Count);
                header.Style.Font.Bold = true;
                header.Style.Font.FontColor = XLColor.White;
                header.Style.Fill.BackgroundColor = XLColor.FromHtml("#315F52");
                header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                header.Style.Alignment.WrapText = true;
                sheet.Row(3).Height = 34;

                var used = sheet.Range(3, 1, lastRow, data.Columns.Count);
                used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                used.Style.Alignment.WrapText = true;
                used.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                used.Style.Border.BottomBorderColor = XLColor.FromHtml("#D9E2DE");

                for (int i = 0; i < data.Columns.Count; i++)
                {
                    string name = data.Columns[i].ColumnName ?? string.Empty;
                    var column = sheet.Column(i + 1);
                    column.AdjustToContents(1, lastRow);
                    if (column.Width < 12) column.Width = 12;
                    if (column.Width > 36) column.Width = 36;

                    if (name.IndexOf("Value", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Price", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Cost", StringComparison.OrdinalIgnoreCase) >= 0)
                        sheet.Range(4, i + 1, Math.Max(4, lastRow), i + 1)
                            .Style.NumberFormat.Format = "#,##0.00;[Red]-#,##0.00";
                    else if (name.IndexOf("Quantity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("Variance", StringComparison.OrdinalIgnoreCase) >= 0)
                        sheet.Range(4, i + 1, Math.Max(4, lastRow), i + 1)
                            .Style.NumberFormat.Format = "#,##0.###;[Red]-#,##0.###";

                    if (name.IndexOf("Date", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("At", StringComparison.OrdinalIgnoreCase) >= 0)
                        column.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                sheet.SheetView.FreezeRows(3);
                used.SetAutoFilter();
            }

            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.FitToPages(1, 0);
            sheet.PageSetup.PagesWide = 1;
            sheet.PageSetup.CenterHorizontally = true;
            sheet.PageMargins.Top = 0.5;
            sheet.PageMargins.Bottom = 0.5;
            sheet.PageMargins.Left = 0.25;
            sheet.PageMargins.Right = 0.25;
        }

        private void PrintReport()
        {
            try
            {
                var dialog = new PrintDialog();
                if (dialog.ShowDialog() != true) return;
                DataTable summary = queryService.GetVarianceSummary(sessionId, managerId);
                DataTable variance = queryService.GetVarianceReport(sessionId, managerId);
                DataTable decisions = queryService.GetCostDecisionDetails(sessionId, managerId);
                DataTable comparison = queryService.GetPreviousCountComparison(sessionId, managerId);
                DataTable repeated = queryService.GetRepeatedShortages(managerId);
                var document = new System.Windows.Documents.FlowDocument
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 9,
                    PageWidth = dialog.PrintableAreaWidth,
                    PageHeight = dialog.PrintableAreaHeight,
                    PagePadding = new Thickness(28),
                    ColumnWidth = Math.Max(100, dialog.PrintableAreaWidth - 56),
                    IsColumnWidthFlexible = false
                };
                document.Blocks.Add(new System.Windows.Documents.Paragraph(
                    new System.Windows.Documents.Run("تقرير الجرد رقم " + sessionId))
                {
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 12)
                });
                string summaryLine = "الأهداف المعتمدة: 0 | أهداف بها عجز: 0 | قيمة العجز: 0.00 | أهداف بها زيادة: 0 | قيمة الزيادة: 0.00";
                if (summary.Rows.Count > 0)
                {
                    DataRow row = summary.Rows[0];
                    summaryLine = string.Format(CultureInfo.CurrentCulture,
                        "الأهداف المعتمدة: {0} | أهداف بها عجز: {1} | قيمة العجز: {2:N2} | أهداف بها زيادة: {3} | قيمة الزيادة: {4:N2}",
                        Value(row, "ApprovedTargets"), Value(row, "ShortageTargets"),
                        Value(row, "ShortageValue"), Value(row, "ExcessTargets"), Value(row, "ExcessValue"));
                }
                document.Blocks.Add(new System.Windows.Documents.Paragraph(
                    new System.Windows.Documents.Run(summaryLine))
                { FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
                AddPrintSection(document, "ملخص الفروق المعتمدة", variance);
                AddPrintSection(document, "تفاصيل قرارات التكلفة", decisions);
                AddPrintSection(document, "مقارنة بالجرد السابق", comparison);
                AddPrintSection(document, "الأهداف المتكرر بها العجز", repeated);
                dialog.PrintDocument(((System.Windows.Documents.IDocumentPaginatorSource)document).DocumentPaginator, "تقرير الجرد رقم " + sessionId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "تعذرت طباعة التقرير: " + ex.Message,
                    "خطأ في الطباعة", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void AddPrintSection(
            System.Windows.Documents.FlowDocument document, string title, DataTable data)
        {
            document.Blocks.Add(new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run(title))
            { FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 6) });
            if (data == null || data.Columns.Count == 0 || data.Rows.Count == 0)
            {
                document.Blocks.Add(new System.Windows.Documents.Paragraph(
                    new System.Windows.Documents.Run("لا توجد بيانات لهذا التقرير.")));
                return;
            }
            var table = new System.Windows.Documents.Table
            { CellSpacing = 0, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
            for (int i = 0; i < data.Columns.Count; i++)
                table.Columns.Add(new System.Windows.Documents.TableColumn
                { Width = new GridLength(1, GridUnitType.Star) });
            var headerGroup = new System.Windows.Documents.TableRowGroup();
            var headerRow = new System.Windows.Documents.TableRow
            { Background = new SolidColorBrush(Color.FromRgb(225, 233, 230)), FontWeight = FontWeights.Bold };
            foreach (DataColumn column in data.Columns)
                headerRow.Cells.Add(CreatePrintCell(column.ColumnName, true));
            headerGroup.Rows.Add(headerRow);
            table.RowGroups.Add(headerGroup);
            var bodyGroup = new System.Windows.Documents.TableRowGroup();
            foreach (DataRow dataRow in data.Rows)
            {
                var row = new System.Windows.Documents.TableRow();
                foreach (DataColumn column in data.Columns)
                {
                    string value = dataRow[column] == DBNull.Value ? string.Empty :
                        Convert.ToString(dataRow[column], CultureInfo.CurrentCulture);
                    row.Cells.Add(CreatePrintCell(value, false));
                }
                bodyGroup.Rows.Add(row);
            }
            table.RowGroups.Add(bodyGroup);
            document.Blocks.Add(table);
        }

        private static System.Windows.Documents.TableCell CreatePrintCell(string value, bool isHeader)
        {
            var paragraph = new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run(value ?? string.Empty))
            { FontSize = 8, Margin = new Thickness(1), TextAlignment = TextAlignment.Right };
            return new System.Windows.Documents.TableCell(paragraph)
            {
                Padding = new Thickness(4, 3, 4, 3),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0.35),
                Background = isHeader ? new SolidColorBrush(Color.FromRgb(225, 233, 230)) : Brushes.White
            };
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
                    "الأهداف المعتمدة: {0}   |   أهداف بها عجز: {1}   |   قيمة العجز: {2:N2}   |   أهداف بها زيادة: {3}   |   قيمة الزيادة: {4:N2}",
                    Value(row, "ApprovedTargets"),
                    Value(row, "ShortageTargets"),
                    Value(row, "ShortageValue"),
                    Value(row, "ExcessTargets"),
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