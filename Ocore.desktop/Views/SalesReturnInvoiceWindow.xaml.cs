
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class SalesReturnInvoiceWindow : Window
    {
        private readonly int currentUserId;
        private readonly SalesReturnsService returnsService;
        private readonly int selectedInvoiceId;

        private const double Epsilon = 0.00000001;

        private bool isUpdatingItems;
        private bool isLoadingInvoice;
        private bool isAdjustingReturnQuantity;

        private readonly SalesReturnDetails selectedInvoice;

        public SalesReturnInvoiceWindow(
            int userId,
            SalesReturnDetails invoice)
        {
            InitializeComponent();

            if (userId <= 0)
                throw new ArgumentException("رقم المستخدم غير صحيح.");

            if (invoice == null || invoice.InvoiceId <= 0)
                throw new ArgumentException("بيانات الفاتورة غير صحيحة.");

            currentUserId = userId;
            selectedInvoice = invoice;
            selectedInvoiceId = invoice.InvoiceId;

            returnsService = new SalesReturnsService();

            InvoiceNumberText.Text = invoice.InvoiceNumber ?? "-";
            EmployeeNameText.Text = invoice.EmployeeName ?? "-";

            InvoiceDateText.Text = invoice.CreatedAt == DateTime.MinValue
                ? "-"
                : invoice.CreatedAt.ToString("yyyy-MM-dd HH:mm");

            InvoiceTotalText.Text = invoice.Total.ToString("N2");

            ReturnTotalText.Text = "0.00";
            RefundAmountTextBox.Text = "0";
            RefundPaymentMethodComboBox.SelectedIndex = -1;

            ReturnFullInvoiceButton.IsEnabled = false;

            LoadInvoiceItems();
        }

        // ==========================================
        // تحميل جميع أصناف الفاتورة
        // ==========================================

        private void LoadInvoiceItems()
        {
            try
            {
                double previousReturnsTotal =
                    returnsService.GetPreviousReturnsTotal(
                        currentUserId,
                        selectedInvoiceId);

                PreviousReturnsTotalText.Text =
                    previousReturnsTotal.ToString("N2");

                double netInvoiceTotal = Math.Round(
                    selectedInvoice.Total - previousReturnsTotal,
                    2,
                    MidpointRounding.AwayFromZero);

                NetInvoiceTotalText.Text =
                    netInvoiceTotal.ToString("N2");

                isLoadingInvoice = true;

                List<SalesReturnItem> items =
                    returnsService.GetInvoiceItemsForReturn(
                        currentUserId,
                        selectedInvoiceId);

                ItemsDataGrid.ItemsSource = items;

                bool hasRemainingItems =
                    items.Any(x => x.RemainingQuantity > Epsilon);

                ReturnFullInvoiceButton.IsEnabled =
                    hasRemainingItems;

                if (items.Count == 0)
                {
                    ItemsStatusText.Text =
                        "لا توجد أصناف في هذه الفاتورة.";

                    return;
                }

                int remainingItemsCount =
                    items.Count(x => x.RemainingQuantity > Epsilon);

                int fullyReturnedItemsCount =
                    items.Count(x => x.RemainingQuantity <= Epsilon);

                ItemsStatusText.Text =
                    "إجمالي الأصناف: " + items.Count +
                    " | أصناف متاحة للمرتجع: " + remainingItemsCount +
                    " | أصناف مرتجعة بالكامل: " + fullyReturnedItemsCount +
                    " | أدخل كميات المرتجع المطلوبة.";

                UpdateAllReturnItems();
            }
            catch (Exception ex)
            {
                ItemsDataGrid.ItemsSource = null;

                ReturnFullInvoiceButton.IsEnabled = false;

                MessageBox.Show(
                    ex.Message,
                    "خطأ في تحميل الفاتورة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                isLoadingInvoice = false;
            }
        }

        // ==========================================
        // إرجاع الفاتورة كاملة
        // ==========================================

        private void ReturnFullInvoiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                ItemsDataGrid.CommitEdit(
                    DataGridEditingUnit.Cell, true);

                ItemsDataGrid.CommitEdit(
                    DataGridEditingUnit.Row, true);

                int updatedItemsCount = 0;

                foreach (var row in ItemsDataGrid.Items)
                {
                    SalesReturnItem item =
                        row as SalesReturnItem;

                    if (item == null)
                        continue;

                    if (item.RemainingQuantity <= Epsilon)
                    {
                        item.ReturnUnitQuantity = 0;
                        item.Total = 0;
                        item.DisplayTotal = 0;
                        item.DisplayUnitPrice = 0;
                        continue;
                    }

                    item.ReturnUnitName = item.UnitName;

                    item.ReturnUnitQuantity =
                        item.RemainingQuantity;

                    UpdateReturnItem(item);

                    updatedItemsCount++;
                }

                ItemsDataGrid.Items.Refresh();

                UpdateAllReturnItems();

                if (updatedItemsCount == 0)
                {
                    MessageBox.Show(
                        "لا توجد كميات متبقية في هذه الفاتورة لإرجاعها.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    ReturnFullInvoiceButton.IsEnabled = false;

                    return;
                }

                ItemsStatusText.Text =
                    "تم تحديد جميع الكميات المتبقية للمرتجع. " +
                    "يمكنك تعديل كمية أي صنف قبل الحفظ.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "خطأ في تحديد كميات المرتجع",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================================
        // تحديد هل الوحدة المختارة هي الوحدة الصغيرة
        // ==========================================

        private bool IsSmallReturnUnit(SalesReturnItem item)
        {
            if (item == null)
                return false;

            if (string.IsNullOrWhiteSpace(item.SmallUnitName))
                return false;

            return string.Equals(
                (item.ReturnUnitName ?? "").Trim(),
                item.SmallUnitName.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        // ==========================================
        // عدد الوحدات الصغيرة داخل العبوة
        // ==========================================

        private double GetUnitsPerPackage(SalesReturnItem item)
        {
            if (item == null ||
                item.UnitsPerPackage <= 0 ||
                double.IsNaN(item.UnitsPerPackage) ||
                double.IsInfinity(item.UnitsPerPackage))
            {
                return 1;
            }

            return item.UnitsPerPackage;
        }

        // ==========================================
        // سعر العبوة التاريخي قبل الخصم والضريبة
        // ==========================================

        private double GetHistoricalPackagePrice(SalesReturnItem item)
        {
            if (item == null)
                return 0;

            double packageQuantity = item.PackageQuantity;
            double soldQuantity = item.SoldQuantity;

            if (packageQuantity > Epsilon &&
                soldQuantity > Epsilon)
            {
                return item.UnitPrice *
                       soldQuantity /
                       packageQuantity;
            }

            return item.UnitPrice;
        }

        // ==========================================
        // حساب سعر وحدة المرتجع الأصلية
        // ==========================================

        private void UpdateReturnUnitPrice(SalesReturnItem item)
        {
            if (item == null)
                return;

            double packagePrice =
                GetHistoricalPackagePrice(item);

            if (IsSmallReturnUnit(item))
            {
                item.ReturnUnitPrice =
                    packagePrice / GetUnitsPerPackage(item);
            }
            else
            {
                item.ReturnUnitPrice = packagePrice;
            }

            item.ReturnUnitPrice = Math.Round(
                item.ReturnUnitPrice,
                4,
                MidpointRounding.AwayFromZero);
        }

        // ==========================================
        // تحويل كمية المرتجع إلى كمية العبوات
        // ==========================================

        private double GetReturnPackageQuantity(
            SalesReturnItem item)
        {
            if (item == null)
                return 0;

            return GetReturnPackageQuantity(
                item,
                item.ReturnUnitQuantity);
        }

        private double GetReturnPackageQuantity(
            SalesReturnItem item,
            double quantity)
        {
            if (item == null || quantity <= 0)
                return 0;

            if (IsSmallReturnUnit(item))
            {
                return quantity / GetUnitsPerPackage(item);
            }

            return quantity;
        }

        // ==========================================
        // تحويل المتاح إلى كمية العبوات
        // ==========================================

        private double GetRemainingPackageQuantity(
            SalesReturnItem item)
        {
            if (item == null)
                return 0;

            double remaining = item.RemainingQuantity;

            if (remaining <= 0)
                return 0;

            bool originalUnitIsSmall =
                !string.IsNullOrWhiteSpace(item.SmallUnitName) &&
                string.Equals(
                    (item.UnitName ?? "").Trim(),
                    item.SmallUnitName.Trim(),
                    StringComparison.OrdinalIgnoreCase);

            if (originalUnitIsSmall)
            {
                return remaining / GetUnitsPerPackage(item);
            }

            return remaining;
        }

        // ==========================================
        // حساب الحد الأقصى المسموح به حسب الوحدة
        // ==========================================

        private double GetMaximumReturnUnitQuantity(
            SalesReturnItem item)
        {
            if (item == null)
                return 0;

            double remainingPackages =
                GetRemainingPackageQuantity(item);

            double maximumQuantity;

            if (IsSmallReturnUnit(item))
            {
                maximumQuantity =
                    remainingPackages * GetUnitsPerPackage(item);
            }
            else
            {
                maximumQuantity = remainingPackages;
            }

            return Math.Round(
                maximumQuantity,
                2,
                MidpointRounding.AwayFromZero);
        }

        // ==========================================
        // تحديث بيانات سطر المرتجع
        // ==========================================

        private void UpdateReturnItem(SalesReturnItem item)
        {
            if (item == null)
                return;

            UpdateReturnUnitPrice(item);

            double packageQuantity =
                GetReturnPackageQuantity(item);

            bool originalUnitIsSmall =
                !string.IsNullOrWhiteSpace(item.SmallUnitName) &&
                string.Equals(
                    (item.UnitName ?? "").Trim(),
                    item.SmallUnitName.Trim(),
                    StringComparison.OrdinalIgnoreCase);

            if (originalUnitIsSmall)
            {
                item.Quantity =
                    packageQuantity *
                    GetUnitsPerPackage(item);
            }
            else
            {
                item.Quantity = packageQuantity;
            }

            // Total يظل بالقيمة الأصلية قبل الخصم والضريبة.
            item.Total = CalculateLineTotal(item);
        }

        // ==========================================
        // حساب إجمالي السطر قبل الخصم والضريبة
        // ==========================================

        private double CalculateLineTotal(SalesReturnItem item)
        {
            if (item == null ||
                item.ReturnUnitQuantity <= 0 ||
                item.ReturnUnitPrice < 0)
            {
                return 0;
            }

            return Math.Round(
                item.ReturnUnitQuantity * item.ReturnUnitPrice,
                2,
                MidpointRounding.AwayFromZero);
        }

        // ==========================================
        // حساب قيمة العرض بعد الخصم والضريبة
        // باستخدام نفس محرك حساب المرتجعات
        // ==========================================

        private void UpdateReturnDisplayAmounts(
            SalesReturnItem item)
        {
            if (item == null)
                return;

            if (item.ReturnUnitQuantity <= Epsilon)
            {
                item.DisplayUnitPrice = 0;
                item.DisplayTotal = 0;
                return;
            }

            // نستخدم الخدمة نفسها حتى يكون توزيع الخصم والضريبة
            // مطابقًا تمامًا لما سيتم حفظه في قاعدة البيانات.
            double accountingTotal =
                returnsService.CalculateReturnAccountingTotal(
                    currentUserId,
                    selectedInvoiceId,
                    new List<SalesReturnItem> { item });

            accountingTotal = Math.Round(
                accountingTotal,
                2,
                MidpointRounding.AwayFromZero);

            item.DisplayTotal = accountingTotal;

            item.DisplayUnitPrice =
                item.ReturnUnitQuantity > Epsilon
                    ? Math.Round(
                        accountingTotal / item.ReturnUnitQuantity,
                        4,
                        MidpointRounding.AwayFromZero)
                    : 0;
        }

        // ==========================================
        // تغيير وحدة المرتجع
        // ==========================================

        private void ReturnUnitComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;

            if (comboBox == null)
                return;

            SalesReturnItem item =
                comboBox.DataContext as SalesReturnItem;

            if (item == null)
                return;

            string selectedUnit =
                comboBox.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(selectedUnit))
                return;

            item.ReturnUnitName = selectedUnit;
        }

        // ==========================================
        // قراءة الكمية حسب الفاصل العشري
        // ==========================================

        private bool TryParseReturnQuantity(
            string text,
            out double quantity)
        {
            quantity = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Trim()
                .Replace('\u066B', '.');

            NumberStyles styles =
                NumberStyles.AllowLeadingSign |
                NumberStyles.AllowDecimalPoint;

            if (normalized.Contains(".") &&
                double.TryParse(
                    normalized,
                    styles,
                    CultureInfo.InvariantCulture,
                    out quantity))
            {
                return true;
            }

            if (double.TryParse(
                normalized,
                styles,
                CultureInfo.CurrentCulture,
                out quantity))
            {
                return true;
            }

            return double.TryParse(
                normalized,
                styles,
                CultureInfo.InvariantCulture,
                out quantity);
        }

        // ==========================================
        // التحقق الفوري عند تغيير كمية المرتجع
        // ==========================================

        private void ReturnQuantityTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (isLoadingInvoice ||
                isUpdatingItems ||
                isAdjustingReturnQuantity)
            {
                return;
            }

            TextBox textBox = sender as TextBox;

            if (textBox == null)
                return;

            SalesReturnItem item =
                textBox.DataContext as SalesReturnItem;

            if (item == null)
                return;

            double enteredQuantity;

            if (!TryParseReturnQuantity(
                textBox.Text,
                out enteredQuantity))
            {
                return;
            }

            if (double.IsNaN(enteredQuantity) ||
                double.IsInfinity(enteredQuantity) ||
                enteredQuantity <= 0)
            {
                return;
            }

            double maximumQuantity =
                GetMaximumReturnUnitQuantity(item);

            if (enteredQuantity <= maximumQuantity + Epsilon)
                return;

            isAdjustingReturnQuantity = true;

            try
            {
                item.ReturnUnitQuantity = maximumQuantity;

                textBox.Text = maximumQuantity.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture);

                textBox.CaretIndex = textBox.Text.Length;

                MessageBox.Show(
                    "لا يمكن إرجاع كمية أكبر من المتاح للصنف: " +
                    item.ItemName +
                    "\n\nالكمية المدخلة: " +
                    enteredQuantity.ToString(
                        "0.##",
                        CultureInfo.InvariantCulture) +
                    "\nالحد الأقصى المسموح: " +
                    maximumQuantity.ToString(
                        "0.##",
                        CultureInfo.InvariantCulture) +
                    "\nوحدة المرتجع: " +
                    item.ReturnUnitName,
                    "تجاوز الكمية المتاحة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                isAdjustingReturnQuantity = false;
            }
        }

        // ==========================================
        // عند تعديل الوحدة أو الكمية
        // ==========================================

        private void ItemsDataGrid_CellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (isLoadingInvoice || isUpdatingItems)
                return;

            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(delegate
                {
                    try
                    {
                        ItemsDataGrid.CommitEdit(
                            DataGridEditingUnit.Row, true);

                        UpdateAllReturnItems();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "خطأ في حساب المرتجع",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }));
        }

        // ==========================================
        // تحديث جميع الأسطر وصافي إجمالي المرتجع
        // ==========================================

        private void UpdateAllReturnItems()
        {
            if (isUpdatingItems)
                return;

            isUpdatingItems = true;

            try
            {
                double accountingTotal = 0;

                if (ItemsDataGrid.ItemsSource == null)
                {
                    ReturnTotalText.Text = "0.00";
                    return;
                }

                foreach (var row in ItemsDataGrid.Items)
                {
                    SalesReturnItem item =
                        row as SalesReturnItem;

                    if (item == null)
                        continue;

                    UpdateReturnItem(item);

                    // القيمة المعروضة لا تُستخدم في الحفظ.
                    // يتم حسابها من لقطة الفاتورة الأصلية.
                    UpdateReturnDisplayAmounts(item);

                    accountingTotal += item.DisplayTotal;
                }

                accountingTotal = Math.Round(
                    accountingTotal,
                    2,
                    MidpointRounding.AwayFromZero);

                ReturnTotalText.Text =
                    accountingTotal.ToString("N2");

                ItemsDataGrid.Items.Refresh();
            }
            finally
            {
                isUpdatingItems = false;
            }
        }

        private void UpdateReturnTotal()
        {
            UpdateAllReturnItems();
        }

        // ==========================================
        // التحقق من كمية المرتجع
        // ==========================================

        private bool ValidateReturnQuantity(
            SalesReturnItem item,
            out string errorMessage)
        {
            errorMessage = "";

            if (item == null)
            {
                errorMessage = "يوجد صنف غير صالح.";
                return false;
            }

            double quantity = item.ReturnUnitQuantity;

            if (double.IsNaN(quantity) ||
                double.IsInfinity(quantity))
            {
                errorMessage =
                    "كمية المرتجع غير صحيحة للصنف: " +
                    item.ItemName;

                return false;
            }

            if (quantity < 0)
            {
                errorMessage =
                    "لا يمكن إدخال كمية مرتجع سالبة للصنف: " +
                    item.ItemName;

                return false;
            }

            if (quantity <= 0)
                return true;

            if (string.IsNullOrWhiteSpace(item.ReturnUnitName))
            {
                errorMessage =
                    "اختر وحدة المرتجع للصنف: " +
                    item.ItemName;

                return false;
            }

            if (IsSmallReturnUnit(item) &&
                Math.Abs(quantity - Math.Round(quantity)) > Epsilon)
            {
                errorMessage =
                    "كمية المرتجع للصنف " +
                    item.ItemName +
                    " يجب أن تكون عددًا صحيحًا من الوحدة الصغيرة.";

                return false;
            }

            if (item.ReturnUnitPrice < 0 ||
                double.IsNaN(item.ReturnUnitPrice) ||
                double.IsInfinity(item.ReturnUnitPrice))
            {
                errorMessage =
                    "سعر المرتجع غير صحيح للصنف: " +
                    item.ItemName;

                return false;
            }

            double returnPackageQuantity =
                GetReturnPackageQuantity(item);

            double remainingPackageQuantity =
                GetRemainingPackageQuantity(item);

            if (returnPackageQuantity >
                remainingPackageQuantity + Epsilon)
            {
                errorMessage =
                    "كمية المرتجع أكبر من الكمية المتبقية للصنف: " +
                    item.ItemName +
                    "\nالمتاح يعادل: " +
                    remainingPackageQuantity.ToString("N3") +
                    " عبوة.";

                return false;
            }

            return true;
        }

        // ==========================================
        // قراءة والتحقق من مبلغ الرد الفعلي
        // ==========================================

        private bool TryGetRefundDetails(
            double accountingTotal,
            out double refundAmount,
            out string refundPaymentMethod,
            out string errorMessage)
        {
            refundAmount = 0;
            refundPaymentMethod = "";
            errorMessage = "";

            string amountText =
                (RefundAmountTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(amountText))
                amountText = "0";

            string normalizedAmount = amountText
                .Replace("٬", "")
                .Replace("٫", ".")
                .Replace(",", ".");

            if (!double.TryParse(
                normalizedAmount,
                NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out refundAmount))
            {
                errorMessage =
                    "أدخل مبلغ الرد الفعلي بصورة رقمية صحيحة.";

                return false;
            }

            if (double.IsNaN(refundAmount) ||
                double.IsInfinity(refundAmount) ||
                refundAmount < 0)
            {
                errorMessage =
                    "مبلغ الرد لا يمكن أن يكون سالبًا أو غير صحيح.";

                return false;
            }

            refundAmount = Math.Round(
                refundAmount,
                2,
                MidpointRounding.AwayFromZero);

            if (refundAmount > accountingTotal + Epsilon)
            {
                errorMessage =
                    "مبلغ الرد الفعلي لا يمكن أن يتجاوز صافي قيمة المرتجع.";

                return false;
            }
            if (refundAmount <= Epsilon)
            {
                refundAmount = 0;
                return true;
            }

            ComboBoxItem selectedMethod =
                RefundPaymentMethodComboBox.SelectedItem as ComboBoxItem;

            if (selectedMethod == null)
            {
                errorMessage =
                    "اختر وسيلة رد المبلغ المصروف: نقدي أو فيزا أو إنستا باي.";

                return false;
            }

            refundPaymentMethod =
                Convert.ToString(selectedMethod.Tag);

            if (refundPaymentMethod != "Cash" &&
                refundPaymentMethod != "Card" &&
                refundPaymentMethod != "InstaPay")
            {
                errorMessage =
                    "وسيلة الرد المختارة غير صحيحة.";

                return false;
            }

            return true;
        }

        // ==========================================
        // حفظ المرتجع
        // ==========================================

        private void SaveReturnButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                ItemsDataGrid.CommitEdit(
                    DataGridEditingUnit.Cell, true);

                ItemsDataGrid.CommitEdit(
                    DataGridEditingUnit.Row, true);

                UpdateAllReturnItems();

                var selectedItems =
                    new List<SalesReturnItem>();

                foreach (var row in ItemsDataGrid.Items)
                {
                    SalesReturnItem returnItem =
                        row as SalesReturnItem;

                    if (returnItem == null)
                        continue;

                    string validationError;

                    if (!ValidateReturnQuantity(
                        returnItem,
                        out validationError))
                    {
                        MessageBox.Show(
                            validationError,
                            "كمية غير صحيحة",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        return;
                    }

                    if (returnItem.ReturnUnitQuantity > 0)
                        selectedItems.Add(returnItem);
                }

                if (selectedItems.Count == 0)
                {
                    MessageBox.Show(
                        "أدخل كمية مرتجع لصنف واحد على الأقل.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                bool hasDifferentInvoices =
                    selectedItems.Any(
                        x => x.InvoiceId != selectedInvoiceId);

                if (hasDifferentInvoices)
                {
                    MessageBox.Show(
                        "توجد أصناف لا تتبع الفاتورة المحددة.\n" +
                        "يجب أن تكون جميع الأصناف في عملية المرتجع الواحدة من نفس الفاتورة.",
                        "اختلاف الفواتير",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                // الإجمالي الأصلي قبل الخصم والضريبة.
                double total = selectedItems.Sum(
                    x => CalculateLineTotal(x));

                total = Math.Round(
                    total,
                    2,
                    MidpointRounding.AwayFromZero);

                // صافي القيمة المحاسبية بعد الخصم والضريبة.
                double accountingTotal =
                    returnsService.CalculateReturnAccountingTotal(
                        currentUserId,
                        selectedInvoiceId,
                        selectedItems);

                double refundAmount;
                string refundPaymentMethod;
                string refundError;

                if (!TryGetRefundDetails(
                    accountingTotal,
                    out refundAmount,
                    out refundPaymentMethod,
                    out refundError))
                {
                    MessageBox.Show(
                        refundError,
                        "بيانات مبلغ الرد غير صحيحة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                string invoiceNumber =
                    selectedItems[0].InvoiceNumber;

                string refundMethodDisplay =
                    refundAmount > 0
                        ? GetRefundMethodDisplay(refundPaymentMethod)
                        : "لم يتم صرف مبلغ";

                MessageBoxResult confirmation =
                    MessageBox.Show(
                        "هل أنت متأكد من حفظ المرتجع؟\n\n" +
                        "الفاتورة الأصلية: " +
                        invoiceNumber +
                        "\nعدد الأصناف: " +
                        selectedItems.Count +
                       "\nإجمالي الأصناف المرتجعة: " +
                        total.ToString("N2") +
                        "\nصافي القيمة المحاسبية للمرتجع: " +
                        accountingTotal.ToString("N2") +
                        "\nالمبلغ المصروف فعليًا: " +
                        refundAmount.ToString("N2") +
                        "\nوسيلة الرد: " +
                        refundMethodDisplay +
                        "\n\nسيتم تحديث المخزون وفقًا للتشغيلات الأصلية الصالحة فقط.",
                        "تأكيد المرتجع",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                SaveReturnButton.IsEnabled = false;

                int returnId = returnsService.SaveReturn(
                    currentUserId,
                    selectedInvoiceId,
                    ReasonTextBox.Text,
                    selectedItems,
                    refundAmount,
                    refundPaymentMethod);

                MessageBox.Show(
                    "تم حفظ المرتجع بنجاح.\n\n" +
                    "رقم عملية المرتجع: " + returnId +
                    "\nإجمالي الأصناف المرتجعة: " +
                    total.ToString("N2") +
                    "\nصافي القيمة المحاسبية: " +
                    accountingTotal.ToString("N2") +
                    "\nالمبلغ المصروف فعليًا: " +
                    refundAmount.ToString("N2") +
                    "\nوسيلة الرد: " + refundMethodDisplay,
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "تعذر حفظ المرتجع",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                SaveReturnButton.IsEnabled = true;
            }
        }

        // ==========================================
        // اسم وسيلة الرد للعرض
        // ==========================================

        private string GetRefundMethodDisplay(string method)
        {
            switch (method)
            {
                case "Cash":
                    return "نقدي";

                case "Card":
                    return "فيزا";

                case "InstaPay":
                    return "إنستا باي";

                default:
                    return "غير محدد";
            }
        }

        // ==========================================
        // إغلاق النافذة
        // ==========================================

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
