
using Ocore.desktop.Models.Printing;
using Ocore.desktop.Services.Printing;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Ocore.desktop.Views
{
    public partial class PrinterTypeSettingsWindow : Window
    {
        private readonly string printerType;
        private readonly PrinterConfigurationService configurationService;

        private PrinterConfiguration configuration;

        public PrinterTypeSettingsWindow(string selectedPrinterType)
        {
            InitializeComponent();

            printerType = selectedPrinterType;
            configurationService = new PrinterConfigurationService();

            if (printerType != PrinterType.Cashier &&
                printerType != PrinterType.Normal &&
                printerType != PrinterType.BarcodeRoll)
            {
                throw new ArgumentException("نوع الطابعة غير صحيح.");
            }

            ConfigurePage();
            LoadConfiguration();
        }

        private void ConfigurePage()
        {
            CashierPanel.Visibility =
                printerType == PrinterType.Cashier
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            NormalPanel.Visibility =
                printerType == PrinterType.Normal
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            BarcodePanel.Visibility =
                printerType == PrinterType.BarcodeRoll
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            switch (printerType)
            {
                case PrinterType.Cashier:
                    PageTitle.Text = "إعدادات طابعة الكاشير";
                    PrintModeComboBox.ItemsSource =
                        new[] { "WindowsDriver", "EscPos" };
                    break;

                case PrinterType.Normal:
                    PageTitle.Text = "إعدادات الطابعة العادية";
                    PrintModeComboBox.ItemsSource =
                        new[] { "WindowsDriver" };

                    NormalPaperSizeComboBox.ItemsSource =
                        new[] { "A4", "A5", "Letter" };

                    OrientationComboBox.ItemsSource =
                        new[] { "Portrait", "Landscape" };
                    break;

                case PrinterType.BarcodeRoll:
                    PageTitle.Text = "إعدادات طابعة الباركود";
                    PrintModeComboBox.ItemsSource =
                        new[] { "WindowsDriver" };
                    break;
            }

            LoadInstalledPrinters();
        }

        private void LoadInstalledPrinters()
        {
            PrinterComboBox.Items.Clear();
            PrinterComboBox.Items.Add(string.Empty);

            foreach (string printerName in PrinterSettings.InstalledPrinters)
            {
                PrinterComboBox.Items.Add(printerName);
            }
        }

        private void LoadConfiguration()
        {
            configuration =
                configurationService.GetConfiguration(printerType);

            if (configuration == null)
            {
                configuration =
                    PrinterConfigurationDefaults.Create(printerType);
            }

            if (!string.IsNullOrWhiteSpace(configuration.PrinterName) &&
                !PrinterComboBox.Items.Contains(configuration.PrinterName))
            {
                PrinterComboBox.Items.Add(configuration.PrinterName);
            }

            PrinterComboBox.SelectedItem = configuration.PrinterName;
            PrintModeComboBox.SelectedItem = configuration.PrintMode;
            FontScaleTextBox.Text = Format(configuration.FontScale);

            CashierPaperWidthTextBox.Text =
                Format(configuration.PaperWidth);

            PrintWidthDotsTextBox.Text =
                configuration.PrintWidthDots.ToString();

            AutoCutCheckBox.IsChecked = configuration.AutoCut;
            OpenCashDrawerCheckBox.IsChecked =
                configuration.OpenCashDrawer;

            NormalPaperSizeComboBox.SelectedItem =
                configuration.PaperSize;

            OrientationComboBox.SelectedItem =
                configuration.Orientation;

            MarginLeftTextBox.Text = Format(configuration.MarginLeft);
            MarginRightTextBox.Text = Format(configuration.MarginRight);
            MarginTopTextBox.Text = Format(configuration.MarginTop);
            MarginBottomTextBox.Text = Format(configuration.MarginBottom);

            BarcodeWidthTextBox.Text =
                Format(configuration.BarcodeLabelWidth);

            BarcodeHeightTextBox.Text =
                Format(configuration.BarcodeLabelHeight);

            BarcodeGapTextBox.Text =
                Format(configuration.BarcodeGap);

            BarcodeOffsetXTextBox.Text =
                Format(configuration.BarcodeOffsetX);

            BarcodeOffsetYTextBox.Text =
                Format(configuration.BarcodeOffsetY);

            BarcodeShowPriceCheckBox.IsChecked =
                configuration.BarcodeShowPrice;

            BarcodeShowProductCodeCheckBox.IsChecked =
                configuration.BarcodeShowProductCode;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                configuration.PrinterName =
                    PrinterComboBox.SelectedItem as string ?? string.Empty;

                configuration.PrintMode =
                    PrintModeComboBox.SelectedItem as string
                    ?? "WindowsDriver";

                configuration.FontScale =
                    ReadDouble(FontScaleTextBox, "مقياس الخط");

                if (printerType == PrinterType.Cashier)
                {
                    configuration.PaperWidth =
                        ReadDouble(CashierPaperWidthTextBox, "عرض الورق");

                    configuration.PrintWidthDots =
                        ReadInt(PrintWidthDotsTextBox, "عرض الطباعة بالنقاط");

                    configuration.AutoCut =
                        AutoCutCheckBox.IsChecked == true;

                    configuration.OpenCashDrawer =
                        OpenCashDrawerCheckBox.IsChecked == true;
                }
                else if (printerType == PrinterType.Normal)
                {
                    configuration.PaperSize =
                        NormalPaperSizeComboBox.SelectedItem as string ?? "A4";

                    configuration.Orientation =
                        OrientationComboBox.SelectedItem as string ?? "Portrait";

                    configuration.MarginLeft =
                        ReadDouble(MarginLeftTextBox, "الهامش الأيسر");

                    configuration.MarginRight =
                        ReadDouble(MarginRightTextBox, "الهامش الأيمن");

                    configuration.MarginTop =
                        ReadDouble(MarginTopTextBox, "الهامش العلوي");

                    configuration.MarginBottom =
                        ReadDouble(MarginBottomTextBox, "الهامش السفلي");
                }
                else if (printerType == PrinterType.BarcodeRoll)
                {
                    configuration.PaperSize = "Roll";

                    configuration.BarcodeLabelWidth =
                        ReadDouble(BarcodeWidthTextBox, "عرض الملصق");

                    configuration.BarcodeLabelHeight =
                        ReadDouble(BarcodeHeightTextBox, "ارتفاع الملصق");

                    configuration.BarcodeGap =
                        ReadDouble(BarcodeGapTextBox, "المسافة بين الملصقات");

                    configuration.BarcodeOffsetX =
                        ReadDouble(BarcodeOffsetXTextBox, "الإزاحة الأفقية");

                    configuration.BarcodeOffsetY =
                        ReadDouble(BarcodeOffsetYTextBox, "الإزاحة الرأسية");

                    configuration.BarcodeShowPrice =
                        BarcodeShowPriceCheckBox.IsChecked == true;

                    configuration.BarcodeShowProductCode =
                        BarcodeShowProductCodeCheckBox.IsChecked == true;
                }

                configurationService.SaveConfiguration(configuration);

                MessageBox.Show(
                    "تم حفظ إعدادات الطابعة بنجاح.",
                    "إعدادات الطابعات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "تعذر حفظ الإعدادات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private double ReadDouble(TextBox textBox, string fieldName)
        {
            double value;

            if (!double.TryParse(
                textBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out value))
            {
                if (!double.TryParse(
                    textBox.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value))
                {
                    throw new ArgumentException(
                        "قيمة غير صحيحة في حقل " + fieldName + ".");
                }
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException(
                    "قيمة غير صحيحة في حقل " + fieldName + ".");
            }

            return value;
        }

        private int ReadInt(TextBox textBox, string fieldName)
        {
            int value;

            if (!int.TryParse(textBox.Text, out value))
            {
                throw new ArgumentException(
                    "قيمة غير صحيحة في حقل " + fieldName + ".");
            }

            return value;
        }

        private string Format(double value)
        {
            return value.ToString(
                "0.##",
                CultureInfo.CurrentCulture);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
