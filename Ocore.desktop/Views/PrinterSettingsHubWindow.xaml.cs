
using System.Windows;
using Ocore.desktop.Models.Printing;

namespace Ocore.desktop.Views
{
    public partial class PrinterSettingsHubWindow : Window
    {
        public string SelectedPrinterType { get; private set; }

        public bool HasSavedConfiguration { get; private set; }

        public PrinterSettingsHubWindow()
        {
            InitializeComponent();

            SelectedPrinterType = string.Empty;
            HasSavedConfiguration = false;
        }

        private void CashierButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenPrinterSettings(PrinterType.Cashier);
        }

        private void NormalButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenPrinterSettings(PrinterType.Normal);
        }

        private void BarcodeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenPrinterSettings(PrinterType.BarcodeRoll);
        }

        private void OpenPrinterSettings(string printerType)
        {
            SelectedPrinterType = printerType;

            var settingsWindow =
                new PrinterTypeSettingsWindow(printerType)
                {
                    Owner = this
                };

            bool? result = settingsWindow.ShowDialog();

            // تسجيل نجاح الحفظ دون إغلاق النافذة الرئيسية.
            if (result == true)
            {
                HasSavedConfiguration = true;
            }
        }
    }
}
