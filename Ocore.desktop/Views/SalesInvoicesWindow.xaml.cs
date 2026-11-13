
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Printing;
using System.Windows.Media;
using Microsoft.Win32;
using Ocore.desktop.Models;
using Ocore.desktop.Models.Printing;
using Ocore.desktop.Services;
using Ocore.desktop.Services.Printing;

namespace Ocore.desktop.Views
{
    public partial class SalesInvoicesWindow : Window
    {
        private readonly int currentUserId;
        private readonly SalesService salesService;
        private readonly PrinterConfigurationService printerConfigurationService;

        private PrinterConfiguration normalPrinterConfiguration;

        private double reportFontSize = 11;
        private double reportPageWidth = 793.7;
        private double reportPageHeight = 1122.5;

        public SalesInvoicesWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            salesService = new SalesService();
            printerConfigurationService = new PrinterConfigurationService();

            LoadInvoices();
        }

        private void LoadInvoices()
        {
            try
            {
                var invoices = salesService.GetInvoices(
                    currentUserId,
                    SearchTextBox.Text,
                    FromDatePicker.SelectedDate,
                    ToDatePicker.SelectedDate);

                InvoicesDataGrid.ItemsSource = invoices;

                InvoicesCountText.Text =
                    "عدد الفواتير: " + invoices.Count;

                double totalAmount = 0;

                foreach (var invoice in invoices)
                    totalAmount += invoice.Total;

                InvoicesTotalText.Text =
                    "إجمالي القيمة: " + totalAmount.ToString("N2");
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل سجل فواتير البيع:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (IsLoaded)
                LoadInvoices();
        }

        private void DatePicker_SelectedDateChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                LoadInvoices();
        }

        private void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadInvoices();
        }

        private void ResetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            FromDatePicker.SelectedDate = null;
            ToDatePicker.SelectedDate = null;
            LoadInvoices();
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadInvoices();
        }

        private void ViewInvoiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenSelectedInvoice();
        }

        private void InvoicesDataGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            OpenSelectedInvoice();
        }

        private void OpenSelectedInvoice()
        {
            var selectedInvoice =
                InvoicesDataGrid.SelectedItem as SaleInvoiceDetails;

            if (selectedInvoice == null)
            {
                MessageBox.Show("من فضلك اختر فاتورة أولاً.");
                return;
            }

            SaleInvoiceDetailsWindow window =
                new SaleInvoiceDetailsWindow(
                    currentUserId,
                    selectedInvoice.InvoiceNumber);

            window.Owner = this;
            window.ShowDialog();
        }

        #region Excel CSV Export

        private void ExportExcelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var invoices =
                    InvoicesDataGrid.ItemsSource as
                    IEnumerable<SaleInvoiceDetails>;

                if (invoices == null)
                {
                    MessageBox.Show("لا توجد فواتير للتصدير.");
                    return;
                }

                SaveFileDialog dialog = new SaveFileDialog
                {
                    Title = "تصدير سجل فواتير البيع",
                    Filter = "Excel CSV (*.csv)|*.csv",
                    FileName = "سجل فواتير البيع.csv",
                    DefaultExt = ".csv",
                    AddExtension = true
                };

                if (dialog.ShowDialog() != true)
                    return;

                StringBuilder csv = new StringBuilder();

                csv.AppendLine(
                    "رقم الفاتورة,اسم الموظف,التاريخ,الإجمالي,الحالة");

                int count = 0;

                foreach (var invoice in invoices)
                {
                    csv.Append(CsvValue(invoice.InvoiceNumber)).Append(",");
                    csv.Append(CsvValue(invoice.EmployeeName)).Append(",");
                    csv.Append(CsvValue(
                        invoice.CreatedAt.ToString("yyyy-MM-dd HH:mm"))).Append(",");
                    csv.Append(CsvValue(
                        invoice.Total.ToString("N2"))).Append(",");
                    csv.AppendLine(CsvValue(invoice.Status));

                    count++;
                }

                if (count == 0)
                {
                    MessageBox.Show("لا توجد فواتير للتصدير.");
                    return;
                }

                File.WriteAllText(
                    dialog.FileName,
                    csv.ToString(),
                    new UTF8Encoding(true));

                MessageBox.Show(
                    "تم تصدير " + count + " فاتورة بنجاح.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تصدير Excel:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string CsvValue(string value)
        {
            if (value == null)
                value = string.Empty;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        #endregion

        #region Normal Printer Configuration

        private void LoadNormalPrinterConfiguration()
        {
            normalPrinterConfiguration =
                printerConfigurationService.GetConfiguration(
                    PrinterType.Normal);

            if (normalPrinterConfiguration == null)
            {
                normalPrinterConfiguration =
                    PrinterConfigurationDefaults.Create(
                        PrinterType.Normal);
            }
        }

        private void ApplyReportPaperSettings()
        {
            if (normalPrinterConfiguration == null)
                throw new InvalidOperationException(
                    "إعدادات الطابعة العادية غير متاحة.");

            double width;
            double height;

            switch (
                (normalPrinterConfiguration.PaperSize ?? "A4").Trim())
            {
                case "A5":
                    width = 559.37;
                    height = 793.70;
                    break;

                case "Letter":
                    width = 816.00;
                    height = 1056.00;
                    break;

                case "A4":
                default:
                    width = 793.70;
                    height = 1122.50;
                    break;
            }

            if (string.Equals(
                normalPrinterConfiguration.Orientation,
                "Landscape",
                StringComparison.OrdinalIgnoreCase))
            {
                reportPageWidth = height;
                reportPageHeight = width;
            }
            else
            {
                reportPageWidth = width;
                reportPageHeight = height;
            }

            double scale = normalPrinterConfiguration.FontScale;

            if (double.IsNaN(scale) ||
                double.IsInfinity(scale) ||
                scale < 0.7 ||
                scale > 1.5)
            {
                scale = 1.0;
            }

            reportFontSize = 11.0 * scale;
        }

        private Thickness GetReportMargins()
        {
            if (normalPrinterConfiguration == null)
                return new Thickness(35);

            const double millimeterToDip = 96.0 / 25.4;

            return new Thickness(
                normalPrinterConfiguration.MarginLeft * millimeterToDip,
                normalPrinterConfiguration.MarginTop * millimeterToDip,
                normalPrinterConfiguration.MarginRight * millimeterToDip,
                normalPrinterConfiguration.MarginBottom * millimeterToDip);
        }

        private string GetNormalPrinterName()
        {
            return normalPrinterConfiguration == null
                ? string.Empty
                : (normalPrinterConfiguration.PrinterName ?? "").Trim();
        }

        #endregion

        #region PDF Report

        private void ExportPdfButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var invoices =
                    InvoicesDataGrid.ItemsSource as
                    IEnumerable<SaleInvoiceDetails>;

                if (invoices == null)
                {
                    MessageBox.Show("لا توجد فواتير للتصدير.");
                    return;
                }

                List<SaleInvoiceDetails> invoiceList =
                    new List<SaleInvoiceDetails>(invoices);

                if (invoiceList.Count == 0)
                {
                    MessageBox.Show("لا توجد فواتير للتصدير.");
                    return;
                }

                LoadNormalPrinterConfiguration();

                string printerName = GetNormalPrinterName();

                if (string.IsNullOrWhiteSpace(printerName))
                {
                    MessageBox.Show(
                        "لم يتم اختيار الطابعة العادية.\n\n" +
                        "افتح إعدادات الطابعات واختر الطابعة المطلوبة ثم احفظ الإعدادات.",
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                ApplyReportPaperSettings();

                FlowDocument document =
                    CreateSalesInvoicesReport(invoiceList);

                using (LocalPrintServer printServer =
                    new LocalPrintServer())
                {
                    PrintQueue printQueue =
                        printServer.GetPrintQueue(printerName);

                    if (printQueue == null)
                    {
                        MessageBox.Show(
                            "الطابعة المحددة غير متاحة على الجهاز:\n\n" +
                            printerName,
                            "O CORE",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        return;
                    }

                    PrintTicket printTicket =
                        printQueue.DefaultPrintTicket;

                    if (printTicket == null)
                        printTicket = new PrintTicket();

                    printTicket.PageOrientation =
                        string.Equals(
                            normalPrinterConfiguration.Orientation,
                            "Landscape",
                            StringComparison.OrdinalIgnoreCase)
                            ? PageOrientation.Landscape
                            : PageOrientation.Portrait;

                    try
                    {
                        printTicket.PageMediaSize =
                            new PageMediaSize(
                                reportPageWidth,
                                reportPageHeight);
                    }
                    catch
                    {
                        // بعض تعريفات الطابعات لا تدعم المقاسات المخصصة.
                    }

                    PrintDialog printDialog = new PrintDialog
                    {
                        PrintQueue = printQueue,
                        PrintTicket = printTicket
                    };

                    IDocumentPaginatorSource paginator = document;

                    printDialog.PrintDocument(
                        paginator.DocumentPaginator,
                        "O CORE - سجل فواتير البيع");

                    MessageBox.Show(
                        "تم إرسال التقرير إلى الطابعة:\n" + printerName,
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تصدير التقرير:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private FlowDocument CreateSalesInvoicesReport(
            List<SaleInvoiceDetails> invoiceList)
        {
            FlowDocument document = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = new System.Windows.Media.FontFamily("Arial"),
                FontSize = reportFontSize,
                PagePadding = GetReportMargins(),
                ColumnWidth = double.PositiveInfinity,
                PageWidth = reportPageWidth,
                PageHeight = reportPageHeight
            };

            Paragraph title = new Paragraph(
                new Run("سجل فواتير البيع"))
            {
                FontSize = reportFontSize * 2,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };

            document.Blocks.Add(title);

            Paragraph info = new Paragraph(
                new Run(
                    "عدد الفواتير: " + invoiceList.Count +
                    "     تاريخ التقرير: " +
                    DateTime.Now.ToString("yyyy/MM/dd HH:mm")))
            {
                FontSize = reportFontSize,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };

            document.Blocks.Add(info);

            Table table = new Table
            {
                CellSpacing = 0
            };

            for (int i = 0; i < 5; i++)
            {
                table.Columns.Add(
                    new TableColumn
                    {
                        Width = new GridLength(1, GridUnitType.Star)
                    });
            }

            TableRowGroup headerGroup = new TableRowGroup();
            TableRow headerRow = new TableRow();

            AddPdfCell(headerRow, "رقم الفاتورة", true);
            AddPdfCell(headerRow, "اسم الموظف", true);
            AddPdfCell(headerRow, "التاريخ", true);
            AddPdfCell(headerRow, "الإجمالي", true);
            AddPdfCell(headerRow, "الحالة", true);

            headerGroup.Rows.Add(headerRow);
            table.RowGroups.Add(headerGroup);

            TableRowGroup bodyGroup = new TableRowGroup();

            foreach (var invoice in invoiceList)
            {
                TableRow row = new TableRow();

                AddPdfCell(row, invoice.InvoiceNumber, false);
                AddPdfCell(row, invoice.EmployeeName, false);

                AddPdfCell(
                    row,
                    invoice.CreatedAt.ToString("yyyy/MM/dd HH:mm"),
                    false);

                AddPdfCell(
                    row,
                    invoice.Total.ToString("N2"),
                    false);

                AddPdfCell(row, invoice.Status, false);

                bodyGroup.Rows.Add(row);
            }

            table.RowGroups.Add(bodyGroup);
            document.Blocks.Add(table);

            Paragraph totalParagraph = new Paragraph(
                new Run(
                    "إجمالي قيمة الفواتير: " +
                    GetInvoicesTotal(invoiceList).ToString("N2")))
            {
                FontSize = reportFontSize * 1.3,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 15, 0, 0)
            };

            document.Blocks.Add(totalParagraph);

            return document;
        }

        private void AddPdfCell(
            TableRow row,
            string value,
            bool isHeader)
        {
            Paragraph paragraph = new Paragraph(
                new Run(value ?? string.Empty))
            {
                Margin = new Thickness(5),
                TextAlignment = TextAlignment.Center,
                FontSize = reportFontSize * 0.9
            };

            TableCell cell = new TableCell(paragraph)
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(0.5),
                Padding = new Thickness(3)
            };

            if (isHeader)
            {
                cell.Background = Brushes.LightGray;
                paragraph.FontWeight = FontWeights.Bold;
            }

            row.Cells.Add(cell);
        }

        private double GetInvoicesTotal(
            IEnumerable<SaleInvoiceDetails> invoices)
        {
            double total = 0;

            foreach (var invoice in invoices)
                total += invoice.Total;

            return total;
        }

        #endregion
    }
}
