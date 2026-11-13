
using Ocore.desktop.Models.Printing;
using Ocore.desktop.Services.Printing;
using System;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Ocore.desktop.Views
{
    public partial class PrinterSettingsWindow : Window
    {
        private readonly PrintingProfileService printingProfileService;
        private readonly int currentUserId;

        private PrintingProfile currentProfile;

        private bool isLoading;
        private bool isSaving;

        public PrinterSettingsWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;

            printingProfileService = new PrintingProfileService();

            InitializeWindowEvents();
            AddAutomaticPaperWidthOption();

            LoadPrinters();

            FunctionComboBox.SelectedIndex = 0;
        }

        private void InitializeWindowEvents()
        {
            FunctionComboBox.SelectionChanged += FunctionComboBox_SelectionChanged;

            PrintWidthModeComboBox.SelectionChanged += PrintWidthModeComboBox_SelectionChanged;

            BarcodePrintTypeComboBox.SelectionChanged += BarcodePrintTypeComboBox_SelectionChanged;

            SaveButton.Click += SaveButton_Click;

            CancelButton.Click += CancelButton_Click;

            RefreshPrintersButton.Click += RefreshPrintersButton_Click;
        }

        private void AddAutomaticPaperWidthOption()
        {
            ComboBoxItem automaticItem = new ComboBoxItem
            {
                Content = "تلقائي / غير محدد",
                Tag = "0"
            };

            PaperSizeComboBox.Items.Insert(0, automaticItem);
        }

        #region Load Functions

        private void FunctionComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isLoading)
                return;

            string functionKey = GetSelectedTag(FunctionComboBox);

            if (string.IsNullOrWhiteSpace(functionKey))
                return;

            LoadProfile(functionKey);
        }

        private void LoadProfile(string functionKey)
        {
            try
            {
                isLoading = true;

                currentProfile = printingProfileService.GetProfile(functionKey);

                if (currentProfile == null)
                {
                    MessageBox.Show(
                        "تعذر تحميل إعدادات وظيفة الطباعة.",
                        "إعدادات الطباعة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                ConfigurePanels(functionKey);

                LoadCommonPrinter();

                if (functionKey == PrintingFunction.CashierSales ||
                    functionKey == PrintingFunction.CashierReturns)
                {
                    LoadCashierSettings();
                }
                else if (functionKey == PrintingFunction.Barcode)
                {
                    LoadBarcodeSettings();
                }
                else
                {
                    LoadNormalSettings();
                }

                PrinterStatusTextBlock.Text =
                    "الإعدادات محفوظة مركزيًا. يتم التحقق من وجود الطابعة على هذا الجهاز عند تنفيذ الطباعة.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل إعدادات الطباعة:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                isLoading = false;
            }
        }

        private void ConfigurePanels(string functionKey)
        {
            bool isCashier =
                functionKey == PrintingFunction.CashierSales ||
                functionKey == PrintingFunction.CashierReturns;

            bool isBarcode =
                functionKey == PrintingFunction.Barcode;

            CashierSettingsPanel.Visibility =
                isCashier ? Visibility.Visible : Visibility.Collapsed;

            BarcodeSettingsPanel.Visibility =
                isBarcode ? Visibility.Visible : Visibility.Collapsed;

            NormalSettingsPanel.Visibility =
                (!isCashier && !isBarcode)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PrinterSectionTitle.Text =
                isBarcode ? "الطابعة العامة للباركود" : "الطابعة الرئيسية للوظيفة";

            PrinterComboBox.Visibility =
                Visibility.Visible;
        }

        private void LoadCommonPrinter()
        {
            string printerName;

            if (currentProfile.FunctionKey == PrintingFunction.Barcode)
                printerName = currentProfile.BarcodePrinter;
            else
                printerName = currentProfile.PrinterName;

            FillPrinterCombo(PrinterComboBox, printerName);
        }

        private void LoadCashierSettings()
        {
            SelectComboTag(
                PrintModeComboBox,
                currentProfile.PrintMode);

            SelectComboTag(
                PaperSizeComboBox,
                currentProfile.PaperWidth.ToString(CultureInfo.InvariantCulture));

            if (currentProfile.PaperWidth == 0)
                SelectComboTag(PaperSizeComboBox, "0");

            if (currentProfile.PrintWidthDots > 0)
            {
                SelectComboTag(PrintWidthModeComboBox, "Manual");

                PrintWidthDotsTextBox.Text =
                    currentProfile.PrintWidthDots.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                SelectComboTag(PrintWidthModeComboBox, "Auto");

                PrintWidthDotsTextBox.Text = "0";
            }

            SelectComboTag(
                FontScaleComboBox,
                currentProfile.FontScale.ToString("0.##", CultureInfo.InvariantCulture));

            AutoCutCheckBox.IsChecked = currentProfile.AutoCut;

            OpenCashDrawerCheckBox.IsChecked = currentProfile.OpenCashDrawer;

            UpdatePrintWidthMode();
        }

        private void LoadBarcodeSettings()
        {
            SelectComboTag(
                BarcodePrintTypeComboBox,
                currentProfile.BarcodePrintType);

            BarcodeShowPriceCheckBox.IsChecked =
                currentProfile.BarcodeShowPrice;

            BarcodeShowProductCodeCheckBox.IsChecked =
                currentProfile.BarcodeShowProductCode;

            FillPrinterCombo(
                BarcodeA4PrinterComboBox,
                currentProfile.BarcodeA4Printer);

            FillPrinterCombo(
                BarcodeRollPrinterComboBox,
                currentProfile.BarcodeRollPrinter);

            BarcodeA4LabelWidthTextBox.Text =
                currentProfile.BarcodeA4LabelWidth;

            BarcodeA4LabelHeightTextBox.Text =
                currentProfile.BarcodeA4LabelHeight;

            BarcodeA4MarginTextBox.Text =
                currentProfile.BarcodeA4Margin;

            BarcodeA4GapTextBox.Text =
                currentProfile.BarcodeA4Gap;

            BarcodeRollLabelWidthTextBox.Text =
                currentProfile.BarcodeRollLabelWidth;

            BarcodeRollLabelHeightTextBox.Text =
                currentProfile.BarcodeRollLabelHeight;

            BarcodeRollGapTextBox.Text =
                currentProfile.BarcodeRollGap;

            BarcodeRollOffsetXTextBox.Text =
                currentProfile.BarcodeRollOffsetX;

            BarcodeRollOffsetYTextBox.Text =
                currentProfile.BarcodeRollOffsetY;

            SelectComboTag(
                BarcodeRollOrientationComboBox,
                currentProfile.BarcodeRollOrientation);

            UpdateBarcodePanels();
        }

        private void LoadNormalSettings()
        {
            SelectComboTag(
                NormalPaperSizeComboBox,
                currentProfile.PaperSize);

            SelectComboTag(
                NormalOrientationComboBox,
                currentProfile.Orientation);

            MarginLeftTextBox.Text =
                currentProfile.MarginLeft.ToString(CultureInfo.InvariantCulture);

            MarginRightTextBox.Text =
                currentProfile.MarginRight.ToString(CultureInfo.InvariantCulture);

            MarginTopTextBox.Text =
                currentProfile.MarginTop.ToString(CultureInfo.InvariantCulture);

            MarginBottomTextBox.Text =
                currentProfile.MarginBottom.ToString(CultureInfo.InvariantCulture);
        }

        #endregion

        #region Printer List

        private void LoadPrinters()
        {
            try
            {
                string[] installedPrinters = new string[PrinterSettings.InstalledPrinters.Count];

                PrinterSettings.InstalledPrinters.CopyTo(
                    installedPrinters,
                    0);

                FillPrinterCombo(PrinterComboBox, GetSelectedPrinter(PrinterComboBox));

                FillPrinterCombo(BarcodeA4PrinterComboBox, GetSelectedPrinter(BarcodeA4PrinterComboBox));

                FillPrinterCombo(BarcodeRollPrinterComboBox, GetSelectedPrinter(BarcodeRollPrinterComboBox));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر قراءة قائمة الطابعات المثبتة على الجهاز:\n" + ex.Message,
                    "الطابعات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void FillPrinterCombo(
            ComboBox comboBox,
            string savedPrinter)
        {
            if (comboBox == null)
                return;

            string previousSelection = savedPrinter;

            comboBox.Items.Clear();

            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                comboBox.Items.Add(printer);
            }

            if (!string.IsNullOrWhiteSpace(previousSelection))
            {
                bool exists = false;

                foreach (object item in comboBox.Items)
                {
                    if (string.Equals(
                        Convert.ToString(item),
                        previousSelection,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    comboBox.Items.Insert(0, previousSelection);

                comboBox.SelectedItem = previousSelection;
            }
            else
            {
                comboBox.SelectedIndex = -1;
            }
        }

        private string GetSelectedPrinter(ComboBox comboBox)
        {
            if (comboBox == null || comboBox.SelectedItem == null)
                return string.Empty;

            return Convert.ToString(comboBox.SelectedItem);
        }

        private void RefreshPrintersButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string commonPrinter = GetSelectedPrinter(PrinterComboBox);
            string a4Printer = GetSelectedPrinter(BarcodeA4PrinterComboBox);
            string rollPrinter = GetSelectedPrinter(BarcodeRollPrinterComboBox);

            FillPrinterCombo(PrinterComboBox, commonPrinter);
            FillPrinterCombo(BarcodeA4PrinterComboBox, a4Printer);
            FillPrinterCombo(BarcodeRollPrinterComboBox, rollPrinter);

            PrinterStatusTextBlock.Text =
                "تم تحديث قائمة الطابعات المثبتة على هذا الجهاز.";
        }

        #endregion

        #region Selection Helpers

        private string GetSelectedTag(ComboBox comboBox)
        {
            if (comboBox == null || comboBox.SelectedItem == null)
                return string.Empty;

            ComboBoxItem item = comboBox.SelectedItem as ComboBoxItem;

            if (item == null || item.Tag == null)
                return string.Empty;

            return Convert.ToString(item.Tag, CultureInfo.InvariantCulture);
        }

        private void SelectComboTag(
            ComboBox comboBox,
            string tag)
        {
            if (comboBox == null)
                return;

            foreach (object obj in comboBox.Items)
            {
                ComboBoxItem item = obj as ComboBoxItem;

                if (item == null)
                    continue;

                string itemTag = Convert.ToString(
                    item.Tag,
                    CultureInfo.InvariantCulture);

                if (string.Equals(
                    itemTag,
                    tag,
                    StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            comboBox.SelectedIndex = -1;
        }

        private void PrintWidthModeComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isLoading)
                return;

            UpdatePrintWidthMode();
        }

        private void UpdatePrintWidthMode()
        {
            string mode = GetSelectedTag(PrintWidthModeComboBox);

            PrintWidthDotsTextBox.IsEnabled = mode == "Manual";

            if (mode == "Auto")
                PrintWidthDotsTextBox.Text = "0";
        }

        private void BarcodePrintTypeComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isLoading)
                return;

            UpdateBarcodePanels();
        }

        private void UpdateBarcodePanels()
        {
            string printType = GetSelectedTag(BarcodePrintTypeComboBox);

            BarcodeA4Panel.Visibility =
                printType == "A4"
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            BarcodeRollPanel.Visibility =
                printType == "Roll"
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        #endregion

        #region Save

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isSaving || currentProfile == null)
                return;

            try
            {
                isSaving = true;

                string functionKey = GetSelectedTag(FunctionComboBox);

                if (string.IsNullOrWhiteSpace(functionKey))
                    return;

                if (functionKey == PrintingFunction.CashierSales ||
                    functionKey == PrintingFunction.CashierReturns)
                {
                    if (!SaveCashierSettings())
                        return;
                }
                else if (functionKey == PrintingFunction.Barcode)
                {
                    if (!SaveBarcodeSettings())
                        return;
                }
                else
                {
                    if (!SaveNormalSettings())
                        return;
                }

                printingProfileService.SaveProfile(currentProfile);

                MessageBox.Show(
                    "تم حفظ إعدادات الطباعة بنجاح في قاعدة البيانات المركزية.",
                    "إعدادات الطباعة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                PrinterStatusTextBlock.Text =
                    "تم حفظ الإعدادات المركزية بنجاح.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء حفظ إعدادات الطباعة:\n" + ex.Message,
                    "خطأ في الحفظ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                isSaving = false;
            }
        }

        private bool SaveCashierSettings()
        {
            string printer = GetSelectedPrinter(PrinterComboBox);

            if (string.IsNullOrWhiteSpace(printer))
            {
                ShowValidationMessage("اختر طابعة الكاشير أولاً.");
                return false;
            }

            string printMode = GetSelectedTag(PrintModeComboBox);

            if (string.IsNullOrWhiteSpace(printMode))
            {
                ShowValidationMessage("اختر طريقة الطباعة.");
                return false;
            }

            string paperWidth = GetSelectedTag(PaperSizeComboBox);

            double parsedWidth;

            if (!double.TryParse(
                paperWidth,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out parsedWidth))
            {
                ShowValidationMessage("اختر عرض الورق الصحيح.");
                return false;
            }

            string widthMode = GetSelectedTag(PrintWidthModeComboBox);

            int printWidthDots = 0;

            if (widthMode == "Manual")
            {
                if (!int.TryParse(
                    PrintWidthDotsTextBox.Text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out printWidthDots) ||
                    printWidthDots <= 0)
                {
                    ShowValidationMessage("أدخل عرض الطباعة بالنقاط بشكل صحيح.");
                    return false;
                }
            }

            double fontScale;

            if (!double.TryParse(
                GetSelectedTag(FontScaleComboBox),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out fontScale) ||
                fontScale <= 0)
            {
                ShowValidationMessage("اختر حجم الخط بشكل صحيح.");
                return false;
            }

            currentProfile.PrinterName = printer;
            currentProfile.PrintMode = printMode;
            currentProfile.PaperSize = "Thermal";
            currentProfile.PaperWidth = parsedWidth;
            currentProfile.PrintWidthDots = printWidthDots;
            currentProfile.FontScale = fontScale;

            currentProfile.AutoCut =
                AutoCutCheckBox.IsChecked == true;

            currentProfile.OpenCashDrawer =
                OpenCashDrawerCheckBox.IsChecked == true;

            return true;
        }

        private bool SaveBarcodeSettings()
        {
            string printType = GetSelectedTag(BarcodePrintTypeComboBox);

            if (printType != "A4" && printType != "Roll")
            {
                ShowValidationMessage("اختر نوع طباعة الباركود.");
                return false;
            }

            string commonPrinter = GetSelectedPrinter(PrinterComboBox);

            string a4Printer = GetSelectedPrinter(BarcodeA4PrinterComboBox);

            string rollPrinter = GetSelectedPrinter(BarcodeRollPrinterComboBox);

            if (string.IsNullOrWhiteSpace(commonPrinter))
            {
                ShowValidationMessage("اختر الطابعة العامة للباركود.");
                return false;
            }

            if (printType == "A4")
            {
                if (string.IsNullOrWhiteSpace(a4Printer))
                {
                    ShowValidationMessage("اختر طابعة ملصقات A4.");
                    return false;
                }

                if (!ValidatePositiveNumber(
                    BarcodeA4LabelWidthTextBox,
                    "عرض ملصق A4"))
                    return false;

                if (!ValidatePositiveNumber(
                    BarcodeA4LabelHeightTextBox,
                    "ارتفاع ملصق A4"))
                    return false;

                if (!ValidateNonNegativeNumber(
                    BarcodeA4MarginTextBox,
                    "هامش A4"))
                    return false;

                if (!ValidateNonNegativeNumber(
                    BarcodeA4GapTextBox,
                    "المسافة بين ملصقات A4"))
                    return false;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(rollPrinter))
                {
                    ShowValidationMessage("اختر طابعة رول الملصقات.");
                    return false;
                }

                if (!ValidatePositiveNumber(
                    BarcodeRollLabelWidthTextBox,
                    "عرض ملصق الرول"))
                    return false;

                if (!ValidatePositiveNumber(
                    BarcodeRollLabelHeightTextBox,
                    "ارتفاع ملصق الرول"))
                    return false;

                if (!ValidateNonNegativeNumber(
                    BarcodeRollGapTextBox,
                    "المسافة بين ملصقات الرول"))
                    return false;

                if (!ValidateAnyNumber(
                    BarcodeRollOffsetXTextBox,
                    "الإزاحة الأفقية"))
                    return false;

                if (!ValidateAnyNumber(
                    BarcodeRollOffsetYTextBox,
                    "الإزاحة الرأسية"))
                    return false;
            }

            currentProfile.BarcodePrinter = commonPrinter;
            currentProfile.BarcodePrintType = printType;

            currentProfile.BarcodeShowPrice =
                BarcodeShowPriceCheckBox.IsChecked == true;

            currentProfile.BarcodeShowProductCode =
                BarcodeShowProductCodeCheckBox.IsChecked == true;

            currentProfile.BarcodeA4Printer = a4Printer;
            currentProfile.BarcodeA4LabelWidth = BarcodeA4LabelWidthTextBox.Text.Trim();
            currentProfile.BarcodeA4LabelHeight = BarcodeA4LabelHeightTextBox.Text.Trim();
            currentProfile.BarcodeA4Margin = BarcodeA4MarginTextBox.Text.Trim();
            currentProfile.BarcodeA4Gap = BarcodeA4GapTextBox.Text.Trim();

            currentProfile.BarcodeRollPrinter = rollPrinter;
            currentProfile.BarcodeRollLabelWidth = BarcodeRollLabelWidthTextBox.Text.Trim();
            currentProfile.BarcodeRollLabelHeight = BarcodeRollLabelHeightTextBox.Text.Trim();
            currentProfile.BarcodeRollGap = BarcodeRollGapTextBox.Text.Trim();
            currentProfile.BarcodeRollOffsetX = BarcodeRollOffsetXTextBox.Text.Trim();
            currentProfile.BarcodeRollOffsetY = BarcodeRollOffsetYTextBox.Text.Trim();

            currentProfile.BarcodeRollOrientation =
                GetSelectedTag(BarcodeRollOrientationComboBox);

            if (string.IsNullOrWhiteSpace(currentProfile.BarcodeRollOrientation))
                currentProfile.BarcodeRollOrientation = "Portrait";

            return true;
        }

        private bool SaveNormalSettings()
        {
            string printer = GetSelectedPrinter(PrinterComboBox);

            if (string.IsNullOrWhiteSpace(printer))
            {
                ShowValidationMessage("اختر الطابعة أولاً.");
                return false;
            }

            string paperSize = GetSelectedTag(NormalPaperSizeComboBox);

            string orientation = GetSelectedTag(NormalOrientationComboBox);

            if (string.IsNullOrWhiteSpace(paperSize))
            {
                ShowValidationMessage("اختر مقاس الورق.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(orientation))
            {
                ShowValidationMessage("اختر اتجاه الورق.");
                return false;
            }

            double left;
            double right;
            double top;
            double bottom;

            if (!TryReadNumber(MarginLeftTextBox, "الهامش الأيسر", 0, false, out left))
                return false;

            if (!TryReadNumber(MarginRightTextBox, "الهامش الأيمن", 0, false, out right))
                return false;

            if (!TryReadNumber(MarginTopTextBox, "الهامش العلوي", 0, false, out top))
                return false;

            if (!TryReadNumber(MarginBottomTextBox, "الهامش السفلي", 0, false, out bottom))
                return false;

            currentProfile.PrinterName = printer;
            currentProfile.PrintMode = "WindowsDriver";
            currentProfile.PaperSize = paperSize;
            currentProfile.Orientation = orientation;

            currentProfile.MarginLeft = left;
            currentProfile.MarginRight = right;
            currentProfile.MarginTop = top;
            currentProfile.MarginBottom = bottom;

            return true;
        }

        #endregion

        #region Validation

        private bool ValidatePositiveNumber(
            TextBox textBox,
            string fieldName)
        {
            double value;

            return TryReadNumber(
                textBox,
                fieldName,
                0.000001,
                false,
                out value);
        }

        private bool ValidateNonNegativeNumber(
            TextBox textBox,
            string fieldName)
        {
            double value;

            return TryReadNumber(
                textBox,
                fieldName,
                0,
                false,
                out value);
        }

        private bool ValidateAnyNumber(
            TextBox textBox,
            string fieldName)
        {
            double value;

            return TryReadNumber(
                textBox,
                fieldName,
                double.MinValue,
                true,
                out value);
        }

        private bool TryReadNumber(
            TextBox textBox,
            string fieldName,
            double minimum,
            bool allowNegative,
            out double value)
        {
            value = 0;

            string input = textBox.Text.Trim();

            bool parsed =
                double.TryParse(
                    input,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);

            if (!parsed)
            {
                parsed =
                    double.TryParse(
                        input,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out value);
            }

            if (!parsed ||
                double.IsNaN(value) ||
                double.IsInfinity(value))
            {
                ShowValidationMessage(
                    "أدخل قيمة رقمية صحيحة في حقل: " + fieldName);

                return false;
            }

            if (!allowNegative && value < minimum)
            {
                ShowValidationMessage(
                    "قيمة حقل " + fieldName + " غير صحيحة.");

                return false;
            }

            return true;
        }

        private void ShowValidationMessage(string message)
        {
            MessageBox.Show(
                message,
                "مراجعة البيانات",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        #endregion

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
