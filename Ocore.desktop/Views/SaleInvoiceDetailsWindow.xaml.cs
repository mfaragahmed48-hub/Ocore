
using System;
using System.Globalization;
using System.Windows;
using Ocore.desktop.Models;
using Ocore.desktop.Models.Printing;
using Ocore.desktop.Services;
using Ocore.desktop.Services.Printing;

namespace Ocore.desktop.Views
{
    public partial class SaleInvoiceDetailsWindow : Window
    {
        private readonly int currentUserId;
        private readonly string invoiceNumber;

        private readonly SalesService salesService;
        private readonly PharmacySettingsService settingsService;

        private SaleInvoiceDetails currentInvoice;
        private PharmacySettings pharmacySettings;

        public SaleInvoiceDetailsWindow(
            int userId,
            string selectedInvoiceNumber)
        {
            InitializeComponent();

            currentUserId = userId;
            invoiceNumber = selectedInvoiceNumber;

            salesService = new SalesService();
            settingsService = new PharmacySettingsService();

            Loaded += SaleInvoiceDetailsWindow_Loaded;
        }

        private void SaleInvoiceDetailsWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            LoadInvoiceDetails();
        }

        private void LoadInvoiceDetails()
        {
            try
            {
                currentInvoice = salesService.GetInvoiceByNumber(
                    currentUserId,
                    invoiceNumber);

                if (currentInvoice == null)
                {
                    MessageBox.Show(
                        "لم يتم العثور على الفاتورة.",
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    Close();
                    return;
                }

                InvoiceNumberText.Text =
                    currentInvoice.InvoiceNumber ?? "-";

                EmployeeNameText.Text =
                    currentInvoice.EmployeeName ?? "-";

                CustomerNameText.Text =
                    string.IsNullOrWhiteSpace(currentInvoice.CustomerName)
                        ? "عميل نقدي"
                        : currentInvoice.CustomerName;

                InvoiceDateText.Text =
                    currentInvoice.CreatedAt == DateTime.MinValue
                        ? "-"
                        : currentInvoice.CreatedAt.ToString(
                            "dd/MM/yyyy HH:mm");

                InvoiceItemsDataGrid.ItemsSource =
                    currentInvoice.Items;

                double subtotal = GetEffectiveSubtotal();
                double netTotal = GetEffectiveNetTotal();

                SubtotalText.Text = FormatNumber(subtotal);

                DiscountAmountText.Text =
                    FormatNumber(currentInvoice.DiscountAmount);

                NetTotalText.Text = FormatNumber(netTotal);

                PaidAmountText.Text =
                    FormatNumber(currentInvoice.PaidAmount);

                RemainingAmountText.Text =
                    FormatNumber(currentInvoice.RemainingAmount);

                PaymentStatusText.Text =
                    GetPaymentStatusText();

                pharmacySettings =
                    settingsService.GetSettingsForPrinting(currentUserId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل تفاصيل الفاتورة:\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Close();
            }
        }

        private double GetEffectiveSubtotal()
        {
            if (currentInvoice == null)
                return 0;

            if (currentInvoice.Subtotal > 0)
                return currentInvoice.Subtotal;

            return currentInvoice.Total;
        }

        private double GetEffectiveNetTotal()
        {
            if (currentInvoice == null)
                return 0;

            if (currentInvoice.NetTotal > 0)
                return currentInvoice.NetTotal;

            return currentInvoice.Total;
        }

        private string GetPaymentStatusText()
        {
            if (currentInvoice == null)
                return "غير محدد";

            switch ((currentInvoice.PaymentStatus ?? "").Trim())
            {
                case "Paid":
                    return "مدفوع بالكامل";

                case "Partial":
                    return "مدفوع جزئيًا";

                case "Unpaid":
                    return "غير مدفوع";

                case "Legacy":
                    return "فاتورة قديمة";

                default:
                    return string.IsNullOrWhiteSpace(
                        currentInvoice.PaymentStatus)
                        ? "غير محدد"
                        : currentInvoice.PaymentStatus;
            }
        }

        // فتح القائمة المركزية لإعدادات الطابعات.
        private void PrinterSettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                PrinterSettingsNavigationService.Open(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح إعدادات الطابعات:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // فتح معاينة فاتورة البيع.
        private void PrintInvoiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenThermalPreview();
        }

        private void OpenThermalPreview()
        {
            if (currentInvoice == null)
            {
                MessageBox.Show(
                    "لا توجد فاتورة جاهزة للمعاينة.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (pharmacySettings == null)
            {
                MessageBox.Show(
                    "تعذر تحميل إعدادات الصيدلية.\n" +
                    "راجع بيانات الصيدلية وحاول مرة أخرى.",
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                ThermalReceiptPreviewWindow previewWindow =
                    new ThermalReceiptPreviewWindow(
                        currentInvoice,
                        pharmacySettings,
                        PrintingFunction.CashierSales,
                        currentUserId);

                previewWindow.Owner = this;
                previewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح المعاينة الحرارية:\n\n" +
                    ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private string FormatNumber(object value)
        {
            if (value == null)
                return "0.00";

            try
            {
                return Convert.ToDecimal(
                    value,
                    CultureInfo.CurrentCulture).ToString("N2");
            }
            catch
            {
                return Convert.ToString(value) ?? "";
            }
        }
    }
}
