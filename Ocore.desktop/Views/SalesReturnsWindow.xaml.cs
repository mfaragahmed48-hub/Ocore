
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class SalesReturnsWindow : Window
    {
        private readonly int currentUserId;
        private readonly SalesReturnsService returnsService;

        private const double Epsilon = 0.00000001;

        private bool isUpdatingItems;

        public SalesReturnsWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            returnsService = new SalesReturnsService();

            ReturnTotalText.Text = "0.00";
        }

        // ==========================================
        // البحث عن الأصناف المباعة
        // ==========================================

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (IsLoaded)
                SearchReturnableItems();
        }

        private void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SearchReturnableItems();
        }

        private void SearchReturnableItems()
        {
            try
            {
                ItemsDataGrid.ItemsSource = null;
                ReturnTotalText.Text = "0.00";

                string searchText = SearchTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(searchText))
                {
                    ItemsStatusText.Text =
                        "اكتب اسم الصنف أو كوده للبحث عن عمليات البيع المتاحة للمرتجع.";

                    return;
                }

                List<SalesReturnItem> items =
                    returnsService.SearchReturnableItems(
                        currentUserId,
                        searchText);

                ItemsDataGrid.ItemsSource = items;

                if (items.Count == 0)
                {
                    ItemsStatusText.Text =
                        "لا توجد عمليات بيع تحتوي على هذا الصنف وبها كمية متاحة للمرتجع.";

                    return;
                }

                ItemsStatusText.Text =
                    "عدد عمليات البيع المتاحة: " +
                    items.Count +
                    " | اختر وحدة المرتجع والكمية المطلوبة لكل صنف.";

                UpdateReturnTotal();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "خطأ في البحث",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================================
        // توافق مع عنصر ComboBox القديم
        // ==========================================

        private void InvoicesComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            // الشاشة لا تعتمد على اختيار الفاتورة من هذا العنصر.
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
        // سعر العبوة التاريخي
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
        // حساب سعر وحدة المرتجع المختارة
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
            if (item == null ||
                item.ReturnUnitQuantity <= 0)
            {
                return 0;
            }

            if (IsSmallReturnUnit(item))
            {
                return item.ReturnUnitQuantity /
                       GetUnitsPerPackage(item);
            }

            return item.ReturnUnitQuantity;
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

            item.Total = CalculateLineTotal(item);
        }

        // ==========================================
        // حساب إجمالي سطر المرتجع
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

            // تحديث الوحدة المختارة مباشرة
            item.ReturnUnitName = selectedUnit;

            // إعادة حساب السعر والإجمالي بعد تحديث الاختيار
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(delegate
                {
                    try
                    {
                        UpdateAllReturnItems();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "خطأ في تحديث سعر المرتجع",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }));
        }

        // ==========================================
        // عند تعديل الوحدة أو الكمية
        // ==========================================

        private void ItemsDataGrid_CellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(delegate
                {
                    try
                    {
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
        // تحديث جميع الأسطر والإجمالي
        // ==========================================

        private void UpdateAllReturnItems()
        {
            if (isUpdatingItems)
                return;

            isUpdatingItems = true;

            try
            {
                double total = 0;

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

                    total += item.Total;
                }

                total = Math.Round(
                    total,
                    2,
                    MidpointRounding.AwayFromZero);

                ReturnTotalText.Text = total.ToString("N2");

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

            if (Math.Abs(quantity - Math.Round(quantity)) > Epsilon)
            {
                errorMessage =
                    "كمية المرتجع للصنف " +
                    item.ItemName +
                    " يجب أن تكون عددًا صحيحًا من الوحدة المختارة.";

                return false;
            }

            if (string.IsNullOrWhiteSpace(item.ReturnUnitName))
            {
                errorMessage =
                    "اختر وحدة المرتجع للصنف: " +
                    item.ItemName;

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

                int originalInvoiceId =
                    selectedItems[0].InvoiceId;

                bool hasDifferentInvoices =
                    selectedItems.Any(
                        x => x.InvoiceId != originalInvoiceId);

                if (hasDifferentInvoices)
                {
                    MessageBox.Show(
                        "لقد اخترت أصنافًا من أكثر من فاتورة.\n\n" +
                        "يجب أن تكون جميع الأصناف في عملية المرتجع الواحدة من نفس فاتورة البيع.\n" +
                        "أزل كميات الأصناف التابعة للفواتير الأخرى ثم حاول مرة أخرى.",
                        "اختلاف الفواتير",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                double total = selectedItems.Sum(
                    x => CalculateLineTotal(x));

                total = Math.Round(
                    total,
                    2,
                    MidpointRounding.AwayFromZero);

                string invoiceNumber =
                    selectedItems[0].InvoiceNumber;

                MessageBoxResult confirmation =
                    MessageBox.Show(
                        "هل أنت متأكد من حفظ المرتجع؟\n\n" +
                        "الفاتورة الأصلية: " +
                        invoiceNumber +
                        "\nعدد الأصناف: " +
                        selectedItems.Count +
                        "\nإجمالي المرتجع: " +
                        total.ToString("N2") +
                        "\n\nسيتم تحديث المخزون وفقًا للتشغيلات الأصلية الصالحة فقط.",
                        "تأكيد المرتجع",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                SaveReturnButton.IsEnabled = false;

                int returnId = returnsService.SaveReturn(
                    currentUserId,
                    originalInvoiceId,
                    ReasonTextBox.Text,
                    selectedItems);

                MessageBox.Show(
                    "تم حفظ المرتجع بنجاح.\n\n" +
                    "رقم عملية المرتجع: " + returnId,
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                ReasonTextBox.Clear();

                SearchReturnableItems();
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
