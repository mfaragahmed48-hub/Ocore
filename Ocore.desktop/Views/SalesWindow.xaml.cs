
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class SalesWindow : Window
    {
        private readonly int currentUserId;

        private readonly SalesProductSearchService searchService;
        private readonly SalesService salesService;
        private readonly CustomerService customerService;
        private readonly ProductService productService;

        private List<SalesProductSearchResult> allSearchResults;
        private ObservableCollection<SaleInvoiceItem> invoiceItems;

        private SalesProductSearchResult selectedSearchResult;
        private Product selectedProduct;

        private bool isSaving;
        private bool isUpdatingInvoice;
        private bool isLoadingCustomers;
        private bool isUpdatingCustomerSearch;

        private List<Customer> allCustomers;
        private ICollectionView customersView;

        // تحسين البحث:
        // تجهيز حقول البحث مرة واحدة لكل صنف.
        private DispatcherTimer searchDebounceTimer;
        private List<SalesProductSearchEntry> indexedSearchResults;

        private class SalesProductSearchEntry
        {
            public SalesProductSearchResult Result { get; set; }

            public string[] SearchFields { get; set; }
        }

        private const double BatchQuantityEpsilon = 0.000001;

        public SalesWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;

            searchService = new SalesProductSearchService();
            salesService = new SalesService();
            customerService = new CustomerService();
            productService = new ProductService();

            allSearchResults = new List<SalesProductSearchResult>();
            indexedSearchResults = new List<SalesProductSearchEntry>();
            invoiceItems = new ObservableCollection<SaleInvoiceItem>();

            // مؤقت البحث: لا ينفذ البحث مع كل حرف.
            searchDebounceTimer = new DispatcherTimer();
            searchDebounceTimer.Interval =
                TimeSpan.FromMilliseconds(200);

            searchDebounceTimer.Tick += (s, e) =>
            {
                searchDebounceTimer.Stop();
                ApplySearchFilter();
            };

            // ربط جدول الفاتورة بالقائمة الأصلية مباشرة.
            InvoiceDataGrid.ItemsSource = invoiceItems;

            // تحديث الإجمالي تلقائيًا عند إضافة أو حذف أو تفريغ القائمة.
            invoiceItems.CollectionChanged += (s, e) =>
            {
                UpdateGrandTotal();
            };

            Loaded += SalesWindow_Loaded;

            SearchTextBox.TextChanged += SearchTextBox_TextChanged;

            SearchTextBox.PreviewKeyDown +=
                EnterToAddProduct_PreviewKeyDown;

            ItemsDataGrid.PreviewKeyDown +=
                EnterToAddProduct_PreviewKeyDown;

            SaleQuantityTextBox.PreviewKeyDown +=
                EnterToAddProduct_PreviewKeyDown;

            ItemsDataGrid.SelectionChanged +=
                ItemsDataGrid_SelectionChanged;

            SaleUnitComboBox.SelectionChanged +=
                SaleUnitComboBox_SelectionChanged;

            AddToInvoiceButton.Click +=
                AddToInvoiceButton_Click;

            RemoveInvoiceItemButton.Click +=
                RemoveInvoiceItemButton_Click;

            SaveInvoiceButton.Click +=
                SaveInvoiceButton_Click;

            NewInvoiceButton.Click +=
                NewInvoiceButton_Click;

            CloseWindowButton.Click +=
                CloseWindowButton_Click;

            InvoiceDataGrid.CellEditEnding +=
                InvoiceDataGrid_CellEditEnding;

            InvoiceDataGrid.CurrentCellChanged +=
                InvoiceDataGrid_CurrentCellChanged;

            InvoiceDataGrid.SelectionChanged +=
                InvoiceDataGrid_SelectionChanged;

            CustomerComboBox.SelectionChanged +=
                CustomerComboBox_SelectionChanged;

            CustomerComboBox.AddHandler(
                TextBox.TextChangedEvent,
                new TextChangedEventHandler(CustomerComboBox_TextChanged));

            DiscountValueTextBox.TextChanged +=
                AccountingInput_TextChanged;

            DiscountTypeComboBox.SelectionChanged +=
                AccountingInput_SelectionChanged;

            PaidAmountTextBox.TextChanged +=
                AccountingInput_TextChanged;

            PaymentMethodComboBox.SelectionChanged +=
                PaymentMethodComboBox_SelectionChanged;

            CashAmountTextBox.TextChanged +=
                AccountingInput_TextChanged;

            CardAmountTextBox.TextChanged +=
                AccountingInput_TextChanged;

            CardReferenceTextBox.TextChanged +=
                AccountingInput_TextChanged;

            InstaPayAmountTextBox.TextChanged +=
                AccountingInput_TextChanged;

            AddToInvoiceButton.IsEnabled = false;
            SaleUnitComboBox.IsEnabled = false;
            SaveInvoiceButton.IsEnabled = false;

            UpdatePaymentFieldsVisibility();
        }

        private async void SalesWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            UpdatePaymentFieldsVisibility();

            LoadCustomers();
            RefreshInvoice();

            // تحميل الأصناف في الخلفية حتى لا تتجمد الواجهة.
            await LoadProductsAsync();
        }

        private async Task LoadProductsAsync()
        {
            try
            {
                List<SalesProductSearchResult> loadedResults =
                    await Task.Run(
                        () => searchService.GetSearchResults(currentUserId));

                // قد تكون النافذة أُغلقت أثناء تحميل البيانات.
                if (!IsLoaded)
                    return;

                allSearchResults =
                    loadedResults ??
                    new List<SalesProductSearchResult>();

                BuildProductSearchIndex();

                ApplySearchFilter();
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!IsLoaded)
                    return;

                MessageBox.Show(
                    ex.Message,
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
            }
            catch (Exception ex)
            {
                if (!IsLoaded)
                    return;

                MessageBox.Show(
                    "حدث خطأ أثناء تحميل الأدوية والأصناف:\n" +
                    ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #region Customers

        private void LoadCustomers()
        {
            isLoadingCustomers = true;

            try
            {
                allCustomers = customerService.GetCustomers(
                    currentUserId,
                    false,
                    "");

                customersView =
                    CollectionViewSource.GetDefaultView(allCustomers);

                customersView.Filter = CustomerSearchFilter;

                CustomerComboBox.ItemsSource = customersView;
                CustomerComboBox.SelectedValuePath = "Id";
                CustomerComboBox.SelectedIndex = -1;
                CustomerComboBox.Text = string.Empty;
            }
            catch (UnauthorizedAccessException ex)
            {
                CustomerComboBox.ItemsSource = null;
                allCustomers = null;
                customersView = null;

                MessageBox.Show(
                    "يمكنك متابعة البيع، لكن لا توجد لديك صلاحية عرض العملاء.\n\n" +
                    ex.Message,
                    "صلاحيات العملاء",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                CustomerComboBox.ItemsSource = null;
                allCustomers = null;
                customersView = null;

                MessageBox.Show(
                    "تعذر تحميل قائمة العملاء.\n\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                isLoadingCustomers = false;
            }
        }

        private void CustomerComboBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (isLoadingCustomers || isUpdatingCustomerSearch)
                return;

            if (customersView == null)
                return;

            // لا نعتبر النص الذي يظهر بعد اختيار عميل عملية بحث جديدة.
            if (CustomerComboBox.SelectedItem is Customer)
                return;

            // عند كتابة بحث جديد، نتأكد من عدم الاحتفاظ بعميل سابق.
            if (CustomerComboBox.SelectedIndex != -1)
            {
                isUpdatingCustomerSearch = true;

                CustomerComboBox.SelectedIndex = -1;

                isUpdatingCustomerSearch = false;
            }

            customersView.Refresh();

            if (!string.IsNullOrWhiteSpace(CustomerComboBox.Text))
                CustomerComboBox.IsDropDownOpen = true;
        }

        private bool CustomerSearchFilter(object item)
        {
            Customer customer = item as Customer;

            if (customer == null)
                return false;

            string searchText =
                (CustomerComboBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            bool nameMatches =
                !string.IsNullOrWhiteSpace(customer.Name) &&
                customer.Name.IndexOf(
                    searchText,
                    StringComparison.CurrentCultureIgnoreCase) >= 0;

            bool codeMatches =
                !string.IsNullOrWhiteSpace(customer.CustomerCode) &&
                customer.CustomerCode.IndexOf(
                    searchText,
                    StringComparison.CurrentCultureIgnoreCase) >= 0;

            string normalizedSearch =
                NormalizeCustomerPhone(searchText);

            string normalizedPhone =
                NormalizeCustomerPhone(customer.Phone);

            bool phoneMatches =
                normalizedSearch.Length > 0 &&
                normalizedPhone.Length > 0 &&
                normalizedPhone.Contains(normalizedSearch);

            return nameMatches || codeMatches || phoneMatches;
        }

        private string NormalizeCustomerPhone(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("+", "")
                .Replace("(", "")
                .Replace(")", "")
                .Trim();
        }

        private void CustomerComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isLoadingCustomers)
                return;

            UpdateGrandTotal();
        }

        private Customer GetSelectedCustomer()
        {
            return CustomerComboBox.SelectedItem as Customer;
        }

        #endregion

        #region Accounting

        private void AccountingInput_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            UpdateGrandTotal();
        }

        private void AccountingInput_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateGrandTotal();
        }

        private void PaymentMethodComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdatePaymentFieldsVisibility();
            UpdateGrandTotal();
        }

        private bool IsSplitPaymentMethod(string paymentMethod)
        {
            switch ((paymentMethod ?? string.Empty).Trim())
            {
                case "Cash":
                case "Card":
                case "InstaPay":
                case "CashCard":
                case "CashInstaPay":
                case "CardInstaPay":
                case "CashCardInstaPay":
                    return true;

                default:
                    return false;
            }
        }

        private void UpdatePaymentFieldsVisibility()
        {
            string paymentMethod = GetPaymentMethod();

            bool showCash =
                paymentMethod == "CashCard" ||
                paymentMethod == "CashInstaPay" ||
                paymentMethod == "CashCardInstaPay";

            bool showCard =
                paymentMethod == "Card" ||
                paymentMethod == "CashCard" ||
                paymentMethod == "CardInstaPay" ||
                paymentMethod == "CashCardInstaPay";

            bool showCardReference = showCard;

            bool showInstaPay =
                paymentMethod == "InstaPay" ||
                paymentMethod == "CashInstaPay" ||
                paymentMethod == "CardInstaPay" ||
                paymentMethod == "CashCardInstaPay";

            CashAmountPanel.Visibility =
                showCash
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            CardAmountPanel.Visibility =
                showCard
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            CardReferencePanel.Visibility =
                showCardReference
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            InstaPayAmountPanel.Visibility =
                showInstaPay
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private bool TryParseMoney(
            string value,
            out double amount)
        {
            amount = 0;

            if (string.IsNullOrWhiteSpace(value))
                return true;

            if (double.TryParse(
                value.Trim(),
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out amount))
            {
                return IsFinite(amount);
            }

            if (double.TryParse(
                value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out amount))
            {
                return IsFinite(amount);
            }

            return false;
        }

        private bool TryGetRequiredPaymentAmount(
            TextBox textBox,
            string fieldName,
            out double amount,
            out string errorMessage)
        {
            amount = 0;
            errorMessage = string.Empty;

            if (textBox == null ||
                string.IsNullOrWhiteSpace(textBox.Text))
            {
                errorMessage =
                    "أدخل " + fieldName + ".";

                return false;
            }

            if (!TryParseMoney(textBox.Text, out amount))
            {
                errorMessage =
                    "قيمة " + fieldName + " غير صحيحة.";

                return false;
            }

            if (amount <= 0)
            {
                errorMessage =
                    "يجب أن يكون " + fieldName + " أكبر من صفر.";

                return false;
            }

            amount = RoundMoney(amount);

            return true;
        }

        private bool TryGetSplitPaymentAmounts(
            string paymentMethod,
            double netTotal,
            out double cashAmount,
            out double cardAmount,
            out double instaPayAmount,
            out string cardReference,
            out string errorMessage)
        {
            cashAmount = 0;
            cardAmount = 0;
            instaPayAmount = 0;
            cardReference = string.Empty;
            errorMessage = string.Empty;

            bool usesCash = false;
            bool usesCard = false;
            bool usesInstaPay = false;

            switch ((paymentMethod ?? string.Empty).Trim())
            {
                case "Cash":
                    usesCash = true;
                    break;

                case "Card":
                    usesCard = true;
                    break;

                case "InstaPay":
                    usesInstaPay = true;
                    break;

                case "CashCard":
                    usesCash = true;
                    usesCard = true;
                    break;

                case "CashInstaPay":
                    usesCash = true;
                    usesInstaPay = true;
                    break;

                case "CardInstaPay":
                    usesCard = true;
                    usesInstaPay = true;
                    break;

                case "CashCardInstaPay":
                    usesCash = true;
                    usesCard = true;
                    usesInstaPay = true;
                    break;

                default:
                    errorMessage = "اختر طريقة دفع صحيحة.";
                    return false;
            }

            if (paymentMethod == "Cash")
            {
                if (netTotal <= 0)
                {
                    errorMessage =
                        "صافي الفاتورة يجب أن يكون أكبر من صفر.";

                    return false;
                }

                cashAmount = RoundMoney(netTotal);
            }

            if (usesCash && paymentMethod != "Cash")
            {
                if (!TryGetRequiredPaymentAmount(
                    CashAmountTextBox,
                    "مبلغ النقدي",
                    out cashAmount,
                    out errorMessage))
                    return false;
            }

            if (usesCard)
            {
                if (!TryGetRequiredPaymentAmount(
                    CardAmountTextBox,
                    "مبلغ الفيزا",
                    out cardAmount,
                    out errorMessage))
                    return false;

                cardReference =
                    (CardReferenceTextBox.Text ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(cardReference))
                {
                    errorMessage =
                        "يجب إدخال رقم عملية الفيزا من إيصال الماكينة.";

                    return false;
                }
            }

            if (usesInstaPay)
            {
                if (!TryGetRequiredPaymentAmount(
                    InstaPayAmountTextBox,
                    "مبلغ إنستا باي",
                    out instaPayAmount,
                    out errorMessage))
                    return false;
            }

            double totalPayments = RoundMoney(
                cashAmount + cardAmount + instaPayAmount);

            if (Math.Abs(totalPayments - netTotal) > 0.005)
            {
                errorMessage =
                    "مجموع مبالغ الدفع لا يساوي صافي الفاتورة.\n\n" +
                    "صافي الفاتورة: " + netTotal.ToString("0.00") +
                    "\nمجموع المدفوعات: " + totalPayments.ToString("0.00") +
                    "\n\nراجع المبالغ المدخلة.";

                return false;
            }

            return true;
        }

        private bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        private double RoundMoney(double value)
        {
            return Math.Round(
                value,
                2,
                MidpointRounding.AwayFromZero);
        }

        private long ToCents(double value)
        {
            return (long)Math.Round(
                value * 100.0,
                MidpointRounding.AwayFromZero);
        }

        private double FromCents(long value)
        {
            return value / 100.0;
        }

        private double CalculateSubtotal()
        {
            return RoundMoney(
                invoiceItems.Sum(x => x.Total));
        }

        private double GetManualDiscountAmount(
            SaleInvoiceItem item)
        {
            if (item == null ||
                !IsFinite(item.Total) ||
                !IsFinite(item.DiscountPercentage))
                return 0;

            return RoundMoney(
                item.Total * item.DiscountPercentage / 100.0);
        }

        private double CalculateManualDiscountTotal()
        {
            return RoundMoney(
                invoiceItems.Sum(GetManualDiscountAmount));
        }

        private string GetDiscountType()
        {
            ComboBoxItem selected =
                DiscountTypeComboBox.SelectedItem as ComboBoxItem;

            if (selected == null || selected.Tag == null)
                return "Amount";

            return selected.Tag.ToString();
        }

        private string GetPaymentMethod()
        {
            ComboBoxItem selected =
                PaymentMethodComboBox.SelectedItem as ComboBoxItem;

            if (selected == null || selected.Tag == null)
                return string.Empty;

            return selected.Tag.ToString();
        }

        private bool ValidateItemDiscounts(
            out string errorMessage)
        {
            errorMessage = string.Empty;

            foreach (SaleInvoiceItem item in invoiceItems)
            {
                if (!IsFinite(item.Total) || item.Total < 0)
                {
                    errorMessage =
                        "إجمالي الصنف " + item.ItemName +
                        " غير صحيح.";

                    return false;
                }

                if (!IsFinite(item.DiscountPercentage) ||
                    item.DiscountPercentage < 0)
                {
                    errorMessage =
                        "نسبة الخصم للصنف " + item.ItemName +
                        " غير صحيحة.";

                    return false;
                }

                if (!IsFinite(item.MaxDiscountPercentage) ||
                    item.MaxDiscountPercentage < 0 ||
                    item.MaxDiscountPercentage > 100)
                {
                    errorMessage =
                        "الحد الأقصى للخصم للصنف " + item.ItemName +
                        " غير صحيح.";

                    return false;
                }

                if (item.DiscountPercentage >
                    item.MaxDiscountPercentage + 0.000001)
                {
                    errorMessage =
                        "نسبة الخصم للصنف " + item.ItemName +
                        " تتجاوز الحد الأقصى المسموح به.\n\n" +
                        "الحد الأقصى: " +
                        item.MaxDiscountPercentage.ToString("0.##") +
                        "%";

                    return false;
                }
            }

            return true;
        }

        private double CalculateMaximumDiscountCapacity()
        {
            double capacity = 0;

            foreach (SaleInvoiceItem item in invoiceItems)
            {
                double maximumDiscount =
                    RoundMoney(
                        item.Total *
                        item.MaxDiscountPercentage / 100.0);

                double manualDiscount =
                    GetManualDiscountAmount(item);

                capacity += Math.Max(
                    0,
                    maximumDiscount - manualDiscount);
            }

            return RoundMoney(capacity);
        }

        private bool TryGetAccountingValues(
            out double subtotal,
            out double discountValue,
            out double discountAmount,
            out double netTotal,
            out double paidAmount,
            out double remainingAmount,
            out string errorMessage)
        {
            subtotal = CalculateSubtotal();
            discountValue = 0;
            discountAmount = 0;
            netTotal = 0;
            paidAmount = 0;
            remainingAmount = 0;
            errorMessage = string.Empty;

            if (!ValidateItemDiscounts(out errorMessage))
                return false;

            if (!TryParseMoney(
                DiscountValueTextBox.Text,
                out discountValue))
            {
                errorMessage = "قيمة الخصم العام غير صحيحة.";
                return false;
            }

            if (discountValue < 0)
            {
                errorMessage =
                    "قيمة الخصم العام لا يمكن أن تكون سالبة.";

                return false;
            }

            double manualDiscount =
                CalculateManualDiscountTotal();

            if (manualDiscount > subtotal + 0.005)
            {
                errorMessage =
                    "إجمالي خصومات الأصناف غير صحيح.";

                return false;
            }

            double subtotalAfterItemDiscount =
                RoundMoney(subtotal - manualDiscount);

            string discountType = GetDiscountType();

            if (discountType == "Percentage")
            {
                if (discountValue > 100)
                {
                    errorMessage =
                        "نسبة الخصم العام لا يمكن أن تتجاوز 100%.";

                    return false;
                }
            }
            else
            {
                if (discountValue >
                    subtotalAfterItemDiscount + 0.005)
                {
                    errorMessage =
                        "قيمة الخصم العام لا يمكن أن تتجاوز " +
                        "الإجمالي بعد خصومات الأصناف.";

                    return false;
                }
            }

            Dictionary<SaleInvoiceItem, long> allocations =
                CalculateGeneralDiscountAllocation(
                    discountType,
                    discountValue);

            long generalDiscountCents =
                allocations.Values.Sum();

            double generalDiscountAmount =
                FromCents(generalDiscountCents);

            discountAmount = RoundMoney(
                manualDiscount + generalDiscountAmount);

            if (discountAmount > subtotal)
                discountAmount = subtotal;

            netTotal = RoundMoney(
                subtotal - discountAmount);

            if (IsSplitPaymentMethod(GetPaymentMethod()))
            {
                paidAmount = netTotal;
                remainingAmount = 0;

                return true;
            }

            if (!TryParseMoney(
                PaidAmountTextBox.Text,
                out paidAmount))
            {
                errorMessage = "المبلغ المدفوع غير صحيح.";
                return false;
            }

            if (paidAmount < 0)
            {
                errorMessage =
                    "المبلغ المدفوع لا يمكن أن يكون سالبًا.";

                return false;
            }

            if (paidAmount > netTotal + 0.005)
            {
                errorMessage =
                    "المبلغ المدفوع لا يمكن أن يتجاوز صافي الفاتورة.";

                return false;
            }

            paidAmount = RoundMoney(
                Math.Min(paidAmount, netTotal));

            remainingAmount = RoundMoney(
                netTotal - paidAmount);

            if (remainingAmount < 0.005)
                remainingAmount = 0;

            return true;
        }

        private Dictionary<SaleInvoiceItem, long>
            CalculateGeneralDiscountAllocation(
                string discountType,
                double discountValue)
        {
            Dictionary<SaleInvoiceItem, long> allocations =
                new Dictionary<SaleInvoiceItem, long>();

            foreach (SaleInvoiceItem item in invoiceItems)
                allocations[item] = 0;

            if (discountValue <= 0 ||
                invoiceItems.Count == 0)
                return allocations;

            var capacities =
                new Dictionary<SaleInvoiceItem, long>();

            var weights =
                new Dictionary<SaleInvoiceItem, long>();

            long subtotalAfterManualDiscountCents = 0;

            foreach (SaleInvoiceItem item in invoiceItems)
            {
                long lineTotalCents =
                    ToCents(item.Total);

                long manualDiscountCents =
                    ToCents(GetManualDiscountAmount(item));

                long maximumDiscountCents = ToCents(
                    RoundMoney(
                        item.Total *
                        item.MaxDiscountPercentage / 100.0));

                capacities[item] =
                    Math.Max(
                        0,
                        maximumDiscountCents - manualDiscountCents);

                weights[item] =
                    Math.Max(
                        0,
                        lineTotalCents - manualDiscountCents);

                subtotalAfterManualDiscountCents +=
                    lineTotalCents - manualDiscountCents;
            }

            long requestedTotalCents;

            if (discountType == "Percentage")
            {
                double subtotalAfterManualDiscount =
                    FromCents(subtotalAfterManualDiscountCents);

                requestedTotalCents = ToCents(
                    RoundMoney(
                        subtotalAfterManualDiscount *
                        discountValue / 100.0));
            }
            else
            {
                requestedTotalCents =
                    ToCents(RoundMoney(discountValue));
            }

            if (requestedTotalCents <= 0)
                return allocations;

            List<SaleInvoiceItem> eligibleItems =
                invoiceItems
                    .Where(x =>
                        capacities[x] > 0 &&
                        weights[x] > 0)
                    .ToList();

            if (eligibleItems.Count == 0)
                return allocations;

            long totalWeight =
                eligibleItems.Sum(x => weights[x]);

            if (totalWeight <= 0)
                return allocations;

            var fractions =
                new List<DiscountFraction>();

            long totalFloorCents = 0;

            for (int i = 0; i < eligibleItems.Count; i++)
            {
                SaleInvoiceItem item = eligibleItems[i];

                decimal exactShare =
                    (decimal)requestedTotalCents *
                    weights[item] /
                    totalWeight;

                long floorShare =
                    (long)Math.Floor(exactShare);

                decimal fraction =
                    exactShare - Math.Floor(exactShare);

                long appliedShare =
                    Math.Min(
                        floorShare,
                        capacities[item]);

                allocations[item] = appliedShare;

                totalFloorCents += floorShare;

                fractions.Add(
                    new DiscountFraction
                    {
                        Item = item,
                        Fraction = fraction,
                        OriginalOrder = i
                    });
            }

            long remainingRemainderCents =
                requestedTotalCents - totalFloorCents;

            fractions.Sort(delegate (
                DiscountFraction first,
                DiscountFraction second)
            {
                int fractionComparison =
                    second.Fraction.CompareTo(first.Fraction);

                if (fractionComparison != 0)
                    return fractionComparison;

                return first.OriginalOrder.CompareTo(
                    second.OriginalOrder);
            });

            foreach (DiscountFraction fraction in fractions)
            {
                if (remainingRemainderCents <= 0)
                    break;

                SaleInvoiceItem item = fraction.Item;

                if (allocations[item] >= capacities[item])
                    continue;

                allocations[item]++;
                remainingRemainderCents--;
            }

            return allocations;
        }

        private void UpdateLineDiscountDisplays(
            string discountType,
            double discountValue)
        {
            Dictionary<SaleInvoiceItem, long> allocations =
                CalculateGeneralDiscountAllocation(
                    discountType,
                    discountValue);

            foreach (SaleInvoiceItem item in invoiceItems)
            {
                double manualDiscount =
                    GetManualDiscountAmount(item);

                double generalAllocation =
                    allocations.ContainsKey(item)
                        ? FromCents(allocations[item])
                        : 0;

                item.DiscountAmount = RoundMoney(
                    manualDiscount + generalAllocation);
            }
        }

        private void UpdateGrandTotal()
        {
            double subtotal = CalculateSubtotal();

            GrandTotalText.Text =
                subtotal.ToString("0.00");

            double discountValue;
            double discountAmount;
            double netTotal;
            double paidAmount;
            double remainingAmount;
            string errorMessage;

            bool valid = TryGetAccountingValues(
                out subtotal,
                out discountValue,
                out discountAmount,
                out netTotal,
                out paidAmount,
                out remainingAmount,
                out errorMessage);

            if (!valid)
            {
                UpdateLineDiscountDisplays("Amount", 0);

                DiscountAmountText.Text = "—";
                NetTotalText.Text = "—";
                RemainingAmountText.Text = "—";

                SaveInvoiceButton.IsEnabled = false;

                return;
            }

            UpdateLineDiscountDisplays(
                GetDiscountType(),
                discountValue);

            DiscountAmountText.Text =
                discountAmount.ToString("0.00");

            NetTotalText.Text =
                netTotal.ToString("0.00");

            RemainingAmountText.Text =
                remainingAmount.ToString("0.00");

            RemainingAmountText.Foreground =
                remainingAmount > 0
                    ? System.Windows.Media.Brushes.DarkOrange
                    : System.Windows.Media.Brushes.ForestGreen;

            bool paymentValid = true;

            if (IsSplitPaymentMethod(GetPaymentMethod()))
            {
                double cashAmount;
                double cardAmount;
                double instaPayAmount;
                string cardReference;
                string paymentError;

                paymentValid = TryGetSplitPaymentAmounts(
                    GetPaymentMethod(),
                    netTotal,
                    out cashAmount,
                    out cardAmount,
                    out instaPayAmount,
                    out cardReference,
                    out paymentError);
            }

            bool allocationsComplete =
                invoiceItems.Count > 0;

            foreach (SaleInvoiceItem item in invoiceItems)
            {
                double allocatedTotal;
                string allocationError;

                if (!AreBatchAllocationsValid(
                    item,
                    out allocatedTotal,
                    out allocationError))
                {
                    allocationsComplete = false;
                    break;
                }
            }

            SaveInvoiceButton.IsEnabled =
                invoiceItems.Count > 0 &&
                !isSaving &&
                paymentValid &&
                allocationsComplete;
        }

        private bool ValidateAccountingBeforeSave(
            out int? customerId,
            out double discountValue,
            out double paidAmount,
            out string discountType,
            out string paymentMethod,
            out string paymentReference,
            out double cashAmount,
            out double cardAmount,
            out double instaPayAmount,
            out string cardReference)
        {
            customerId = null;
            discountValue = 0;
            paidAmount = 0;
            discountType = GetDiscountType();
            paymentMethod = GetPaymentMethod();
            paymentReference =
                (PaymentReferenceTextBox.Text ?? string.Empty).Trim();

            cashAmount = -1;
            cardAmount = -1;
            instaPayAmount = -1;
            cardReference = string.Empty;

            double subtotal;
            double discountAmount;
            double netTotal;
            double remainingAmount;
            string errorMessage;

            if (!TryGetAccountingValues(
                out subtotal,
                out discountValue,
                out discountAmount,
                out netTotal,
                out paidAmount,
                out remainingAmount,
                out errorMessage))
            {
                MessageBox.Show(
                    errorMessage,
                    "مراجعة الحسابات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return false;
            }

            if (IsSplitPaymentMethod(paymentMethod))
            {
                if (!TryGetSplitPaymentAmounts(
                    paymentMethod,
                    netTotal,
                    out cashAmount,
                    out cardAmount,
                    out instaPayAmount,
                    out cardReference,
                    out errorMessage))
                {
                    MessageBox.Show(
                        errorMessage,
                        "مراجعة بيانات الدفع",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return false;
                }

                paymentReference = string.Empty;
            }

            Customer selectedCustomer = GetSelectedCustomer();

            if (selectedCustomer != null)
                customerId = selectedCustomer.Id;

            if (remainingAmount > 0 && !customerId.HasValue)
            {
                MessageBox.Show(
                    "يوجد مبلغ متبقٍ على الفاتورة.\n\n" +
                    "يجب اختيار عميل مسجل حتى يتم تسجيل المديونية على حسابه.",
                    "اختيار العميل مطلوب",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                CustomerComboBox.Focus();
                return false;
            }

            if (paidAmount > 0 &&
                !IsSplitPaymentMethod(paymentMethod) &&
                paymentMethod != "Cash" &&
                paymentMethod != "Card" &&
                paymentMethod != "Transfer")
            {
                MessageBox.Show(
                    "اختر طريقة دفع صحيحة.",
                    "طريقة الدفع",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                PaymentMethodComboBox.Focus();
                return false;
            }

            return true;
        }

        private void ResetAccountingInputs()
        {
            CustomerComboBox.SelectedIndex = -1;
            CustomerComboBox.Text = string.Empty;

            DiscountValueTextBox.Text = "0";
            DiscountTypeComboBox.SelectedIndex = 0;

            PaidAmountTextBox.Text = "0";
            PaymentMethodComboBox.SelectedIndex = 0;

            PaymentReferenceTextBox.Clear();

            CashAmountTextBox.Clear();
            CardAmountTextBox.Clear();
            CardReferenceTextBox.Clear();
            InstaPayAmountTextBox.Clear();

            UpdatePaymentFieldsVisibility();
            UpdateGrandTotal();
        }

        #endregion

        #region Product Search

        private void EnterToAddProduct_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter &&
                e.Key != Key.Return)
                return;

            e.Handled = true;

            if (sender == SearchTextBox)
            {
                // قارئ الباركود يرسل Enter:
                // نفّذ البحث فورًا دون انتظار المؤقت.
                if (searchDebounceTimer != null)
                    searchDebounceTimer.Stop();

                if (allSearchResults == null ||
                    allSearchResults.Count == 0)
                    return;

                ApplySearchFilter();

                if (ItemsDataGrid.SelectedItem == null &&
                    ItemsDataGrid.Items.Count > 0)
                {
                    ItemsDataGrid.SelectedIndex = 0;
                }
            }

            if (selectedSearchResult == null)
            {
                if (ItemsDataGrid.SelectedItem != null)
                {
                    selectedSearchResult =
                        ItemsDataGrid.SelectedItem
                        as SalesProductSearchResult;

                    selectedProduct =
                        selectedSearchResult == null
                            ? null
                            : selectedSearchResult.Product;
                }
                else if (ItemsDataGrid.Items.Count > 0)
                {
                    ItemsDataGrid.SelectedIndex = 0;
                }
            }

            if (selectedSearchResult == null)
                return;

            AddToInvoiceButton_Click(
                AddToInvoiceButton,
                new RoutedEventArgs(Button.ClickEvent));

            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        }

        private void BuildProductSearchIndex()
        {
            indexedSearchResults =
                new List<SalesProductSearchEntry>();

            if (allSearchResults == null)
                return;

            foreach (SalesProductSearchResult result in allSearchResults)
            {
                if (result == null)
                    continue;

                indexedSearchResults.Add(
                    new SalesProductSearchEntry
                    {
                        Result = result,

                        SearchFields = new[]
                        {
                            NormalizeSearchField(result.Name),
                            NormalizeSearchField(result.TradeNameArabic),
                            NormalizeSearchField(result.GenericName),
                            NormalizeSearchField(result.ProductCode),
                            NormalizeSearchField(result.Barcode)
                        }
                    });
            }
        }

        private string NormalizeSearchField(string value)
        {
            return (value ?? string.Empty).ToUpperInvariant();
        }

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (searchDebounceTimer == null)
                return;

            // عند مسح مربع البحث نعرض القائمة فورًا.
            if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
            {
                searchDebounceTimer.Stop();
                ApplySearchFilter();
                return;
            }

            // لا نبحث قبل انتهاء تحميل الأصناف.
            if (allSearchResults == null ||
                allSearchResults.Count == 0)
                return;

            searchDebounceTimer.Stop();
            searchDebounceTimer.Start();
        }

        private void ApplySearchFilter()
        {
            if (allSearchResults == null)
                return;

            ItemsDataGrid.SelectedItem = null;

            selectedSearchResult = null;
            selectedProduct = null;

            ResetSelectedProduct();

            string searchText =
                (SearchTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                ItemsDataGrid.ItemsSource =
                    allSearchResults.ToList();

                return;
            }

            string searchPattern =
                NormalizeWildcardPattern(searchText)
                    .ToUpperInvariant();

            if (indexedSearchResults == null)
                BuildProductSearchIndex();

            var filteredResults = indexedSearchResults
                .Where(entry =>
                    entry.SearchFields.Any(field =>
                        ContainsNormalizedText(
                            field,
                            searchPattern)))
                .Select(entry => entry.Result)
                .ToList();

            ItemsDataGrid.ItemsSource = filteredResults;
        }

        private string NormalizeWildcardPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return "*";

            StringBuilder result = new StringBuilder();

            bool previousWasWildcard = false;

            foreach (char character in pattern)
            {
                if (character == '*')
                {
                    if (!previousWasWildcard)
                        result.Append('*');

                    previousWasWildcard = true;
                }
                else
                {
                    result.Append(character);
                    previousWasWildcard = false;
                }
            }

            if (result.Length == 0 || result[0] != '*')
                result.Insert(0, '*');

            if (result[result.Length - 1] != '*')
                result.Append('*');

            return result.ToString();
        }

        // البحث هنا يقارن نصوصًا تم تجهيزها مسبقًا
        // بحروف كبيرة، لذلك لا يعيد تحويل الأحرف لكل صنف.
        private bool ContainsNormalizedText(
            string source,
            string searchPattern)
        {
            if (string.IsNullOrEmpty(source))
                return false;

            if (string.IsNullOrEmpty(searchPattern))
                return true;

            int sourceIndex = 0;
            int patternIndex = 0;

            int lastWildcardIndex = -1;
            int lastWildcardSourceIndex = -1;

            while (sourceIndex < source.Length)
            {
                if (patternIndex < searchPattern.Length &&
                    searchPattern[patternIndex] != '*' &&
                    source[sourceIndex] == searchPattern[patternIndex])
                {
                    sourceIndex++;
                    patternIndex++;
                }
                else if (patternIndex < searchPattern.Length &&
                         searchPattern[patternIndex] == '*')
                {
                    lastWildcardIndex = patternIndex;
                    lastWildcardSourceIndex = sourceIndex;
                    patternIndex++;
                }
                else if (lastWildcardIndex != -1)
                {
                    patternIndex = lastWildcardIndex + 1;
                    lastWildcardSourceIndex++;
                    sourceIndex = lastWildcardSourceIndex;
                }
                else
                {
                    return false;
                }
            }

            while (patternIndex < searchPattern.Length &&
                   searchPattern[patternIndex] == '*')
            {
                patternIndex++;
            }

            return patternIndex == searchPattern.Length;
        }

        // الإبقاء على الدالة القديمة تحسبًا لأي استخدام آخر داخل الملف.
        private bool ContainsText(
            string source,
            string searchPattern)
        {
            if (string.IsNullOrEmpty(source))
                return false;

            if (string.IsNullOrEmpty(searchPattern))
                return true;

            return ContainsNormalizedText(
                source.ToUpperInvariant(),
                searchPattern.ToUpperInvariant());
        }

        private void ItemsDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            selectedSearchResult =
                ItemsDataGrid.SelectedItem as SalesProductSearchResult;

            selectedProduct =
                selectedSearchResult == null
                    ? null
                    : selectedSearchResult.Product;

            if (selectedSearchResult == null)
            {
                ResetSelectedProduct();
                return;
            }

            if (selectedProduct == null)
            {
                SelectedProductNameText.Text =
                    selectedSearchResult.Name +
                    (
                        string.IsNullOrWhiteSpace(
                            selectedSearchResult.TradeNameArabic)
                            ? ""
                            : " - " +
                              selectedSearchResult.TradeNameArabic
                    ) +
                    " - غير مضاف للمخزون";

                SelectedUnitPriceText.Text = "غير مسجل";
                SaleQuantityTextBox.Text = "1";

                SaleUnitComboBox.IsEnabled = false;
                AddToInvoiceButton.IsEnabled = false;

                return;
            }

            SelectedProductNameText.Text =
                selectedProduct.Name +
                (
                    string.IsNullOrWhiteSpace(
                        selectedSearchResult.TradeNameArabic)
                        ? ""
                        : " - " +
                          selectedSearchResult.TradeNameArabic
                );

            SaleQuantityTextBox.Text = "1";

            SaleUnitComboBox.IsEnabled = true;

            AddToInvoiceButton.IsEnabled =
                selectedProduct.Quantity > 0;

            UpdateSaleUnitNames();

            SaleUnitComboBox.SelectedIndex = 0;

            UpdateSaleUnitPrice();
        }

        private void ResetSelectedProduct()
        {
            SelectedProductNameText.Text = "لم يتم اختيار صنف";
            SelectedUnitPriceText.Text = "0.00";
            SaleQuantityTextBox.Text = "1";

            SaleUnitComboBox.IsEnabled = false;
            AddToInvoiceButton.IsEnabled = false;
        }

        private void UpdateSaleUnitNames()
        {
            if (selectedProduct == null ||
                SaleUnitComboBox.Items.Count < 2)
                return;

            ComboBoxItem packageItem =
                SaleUnitComboBox.Items[0] as ComboBoxItem;

            ComboBoxItem smallUnitItem =
                SaleUnitComboBox.Items[1] as ComboBoxItem;

            if (packageItem != null)
            {
                packageItem.Content =
                    string.IsNullOrWhiteSpace(selectedProduct.UnitName)
                        ? "العبوة"
                        : selectedProduct.UnitName;
            }

            if (smallUnitItem != null)
            {
                smallUnitItem.Content =
                    string.IsNullOrWhiteSpace(selectedProduct.SmallUnitName)
                        ? "الوحدة الصغيرة"
                        : selectedProduct.SmallUnitName;
            }
        }

        private void SaleUnitComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (selectedProduct == null)
                return;

            UpdateSaleUnitPrice();
        }

        private void UpdateSaleUnitPrice()
        {
            if (selectedProduct == null)
                return;

            ComboBoxItem selectedUnit =
                SaleUnitComboBox.SelectedItem as ComboBoxItem;

            if (selectedUnit == null)
                return;

            string unitType =
                selectedUnit.Tag == null
                    ? ""
                    : selectedUnit.Tag.ToString();

            double unitPrice = selectedProduct.SalePrice;

            if (unitType == "SmallUnit")
            {
                int unitsPerPackage =
                    Math.Max(1, selectedProduct.UnitsPerPackage);

                unitPrice =
                    selectedProduct.SalePrice / unitsPerPackage;
            }

            SelectedUnitPriceText.Text =
                unitPrice.ToString("0.00");
        }

        private void AddToInvoiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (selectedProduct == null)
            {
                MessageBox.Show(
                    "اختر صنفًا أولًا.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            double quantity;

            if (!double.TryParse(
                SaleQuantityTextBox.Text,
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                out quantity) &&
                !double.TryParse(
                    SaleQuantityTextBox.Text,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out quantity))
            {
                MessageBox.Show(
                    "أدخل كمية صحيحة.",
                    "كمية غير صحيحة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                SaleQuantityTextBox.Focus();
                SaleQuantityTextBox.SelectAll();
                return;
            }

            if (!IsFinite(quantity) || quantity <= 0)
            {
                MessageBox.Show(
                    "يجب أن تكون الكمية أكبر من صفر.",
                    "كمية غير صحيحة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                SaleQuantityTextBox.Focus();
                SaleQuantityTextBox.SelectAll();
                return;
            }

            ComboBoxItem selectedUnit =
                SaleUnitComboBox.SelectedItem as ComboBoxItem;

            if (selectedUnit == null)
            {
                MessageBox.Show(
                    "اختر وحدة البيع.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string unitName =
                selectedUnit.Content == null
                    ? string.Empty
                    : selectedUnit.Content.ToString();

            string unitType =
                selectedUnit.Tag == null
                    ? string.Empty
                    : selectedUnit.Tag.ToString();

            int unitsPerPackage =
                Math.Max(1, selectedProduct.UnitsPerPackage);

            bool isSmallUnit = unitType == "SmallUnit";

            double unitPrice = isSmallUnit
                ? selectedProduct.SalePrice / unitsPerPackage
                : selectedProduct.SalePrice;

            double packageQuantity = isSmallUnit
                ? quantity / unitsPerPackage
                : quantity;

            packageQuantity =
                Math.Round(packageQuantity, 8);

            double alreadyAdded =
                invoiceItems
                    .Where(x => x.ProductId == selectedProduct.Id)
                    .Sum(x => x.PackageQuantity);

            double availableQuantity =
                selectedProduct.Quantity;

            if (packageQuantity + alreadyAdded >
                availableQuantity + BatchQuantityEpsilon)
            {
                MessageBox.Show(
                    "الكمية المطلوبة أكبر من الرصيد المتاح.\n" +
                    "الرصيد الحالي: " +
                    availableQuantity.ToString("0.###"),
                    "رصيد غير كافٍ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                SaleQuantityTextBox.Focus();
                SaleQuantityTextBox.SelectAll();
                return;
            }

            SaleInvoiceItem invoiceItem =
                new SaleInvoiceItem
                {
                    ProductId = selectedProduct.Id,
                    ItemName = selectedProduct.Name,
                    UnitName = unitName,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    Total = RoundMoney(quantity * unitPrice),
                    PackageQuantity = packageQuantity,

                    UnitsPerPackage = unitsPerPackage,
                    PackageUnitName = selectedProduct.UnitName,
                    SmallUnitName = selectedProduct.SmallUnitName,
                    IsSmallUnit = isSmallUnit,
                    MaxDiscountPercentage =
                        selectedProduct.MaxDiscountPercentage,

                    BatchAllocations =
                        new ObservableCollection<SaleInvoiceBatchAllocation>()
                };

            invoiceItem.PropertyChanged +=
                InvoiceItem_PropertyChanged;

            invoiceItems.Add(invoiceItem);

            RefreshInvoice(invoiceItem);

            // اختيار تشغيلة واحدة لهذا الصف الجديد.
            OpenBatchSelectionWindow(invoiceItem, true);

            SaleQuantityTextBox.Text = "1";
        }

        #endregion

        #region Invoice Items

        private void InvoiceItem_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "UnitName")
                return;

            SaleInvoiceItem item = sender as SaleInvoiceItem;

            if (item == null)
                return;

            bool newIsSmallUnit =
                !string.IsNullOrWhiteSpace(item.SmallUnitName) &&
                string.Equals(
                    item.UnitName,
                    item.SmallUnitName,
                    StringComparison.OrdinalIgnoreCase);

            if (newIsSmallUnit == item.IsSmallUnit)
                return;

            int unitsPerPackage = Math.Max(1, item.UnitsPerPackage);
            double packageQuantity = item.PackageQuantity;
            double oldTotal = item.Total;

            double newQuantity = newIsSmallUnit
                ? packageQuantity * unitsPerPackage
                : packageQuantity;

            double newUnitPrice = newQuantity > 0
                ? oldTotal / newQuantity
                : item.UnitPrice;

            item.IsSmallUnit = newIsSmallUnit;
            item.Quantity = newQuantity;
            item.UnitPrice = newUnitPrice;
            item.PackageQuantity = packageQuantity;
            item.Total = oldTotal;

            // تغيير وحدة العرض لا يغيّر كمية العبوة المصروفة
            // ولا يغيّر تخصيص التشغيلة المرتبط بالصف.
            UpdateGrandTotal();
        }

        private void InvoiceDataGrid_CellEditEnding(
            object sender,
            DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            SaleInvoiceItem item =
                e.Row.Item as SaleInvoiceItem;

            if (item == null)
                return;

            TextBox editor =
                e.EditingElement as TextBox;

            if (editor == null)
                return;

            string columnHeader =
                e.Column.Header == null
                    ? string.Empty
                    : e.Column.Header.ToString();

            if (columnHeader == "خصم %")
            {
                double newDiscountPercentage;

                if (!TryParseQuantity(
                    editor.Text,
                    out newDiscountPercentage) ||
                    !IsFinite(newDiscountPercentage) ||
                    newDiscountPercentage < 0)
                {
                    MessageBox.Show(
                        "أدخل نسبة خصم صحيحة لا تقل عن صفر.",
                        "نسبة خصم غير صحيحة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    e.Cancel = true;
                    return;
                }

                if (!IsFinite(item.MaxDiscountPercentage) ||
                    item.MaxDiscountPercentage < 0 ||
                    item.MaxDiscountPercentage > 100)
                {
                    MessageBox.Show(
                        "الحد الأقصى للخصم لهذا الصنف غير صحيح.",
                        "حد الخصم",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    e.Cancel = true;
                    return;
                }

                if (newDiscountPercentage >
                    item.MaxDiscountPercentage + 0.000001)
                {
                    MessageBox.Show(
                        "لا يمكن تجاوز الحد الأقصى للخصم لهذا الصنف.\n\n" +
                        "الحد الأقصى المسموح: " +
                        item.MaxDiscountPercentage.ToString("0.##") +
                        "%",
                        "تجاوز حد الخصم",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    e.Cancel = true;
                    return;
                }

                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        item.DiscountPercentage =
                            newDiscountPercentage;

                        RefreshInvoice(item);
                    }));

                return;
            }

            if (columnHeader != "الكمية")
                return;

            double newQuantity;

            if (!TryParseQuantity(
                editor.Text,
                out newQuantity) ||
                newQuantity <= 0 ||
                !IsFinite(newQuantity))
            {
                MessageBox.Show(
                    "أدخل كمية صحيحة أكبر من صفر.",
                    "كمية غير صحيحة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                e.Cancel = true;
                return;
            }

            if (!IsWholeQuantity(newQuantity))
            {
                MessageBox.Show(
                    "يجب أن تكون كمية البيع عددًا صحيحًا من الوحدات.",
                    "كمية غير صحيحة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                e.Cancel = true;
                return;
            }

            SalesProductSearchResult searchResult =
                allSearchResults.FirstOrDefault(
                    p => p.Product != null &&
                         p.Product.Id == item.ProductId);

            if (searchResult == null)
            {
                MessageBox.Show(
                    "تعذر التحقق من رصيد الصنف.",
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                e.Cancel = true;
                return;
            }

            double unitsPerPackage =
                Math.Max(1, item.UnitsPerPackage);

            bool isSmallUnit = item.IsSmallUnit;

            double newPackageQuantity =
                isSmallUnit
                    ? newQuantity / unitsPerPackage
                    : newQuantity;

            newPackageQuantity =
                Math.Round(newPackageQuantity, 8);

            double otherItemsQuantity =
                invoiceItems
                    .Where(x => x.ProductId == item.ProductId &&
                                !ReferenceEquals(x, item))
                    .Sum(x => x.PackageQuantity);

            double availableQuantity =
                searchResult.Product.Quantity;

            if (newPackageQuantity + otherItemsQuantity >
                availableQuantity + BatchQuantityEpsilon)
            {
                MessageBox.Show(
                    "الكمية المطلوبة أكبر من الرصيد المتاح.\n" +
                    "الرصيد الحالي: " +
                    availableQuantity.ToString("0.###"),
                    "رصيد غير كافٍ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                e.Cancel = true;
                return;
            }

            // كل صف يجب أن يرتبط بتشغيلة واحدة فقط.
            if (item.BatchAllocations != null &&
                item.BatchAllocations.Count > 1)
            {
                MessageBox.Show(
                    "هذا الصف يحتوي على أكثر من تشغيلة.\n\n" +
                    "أعد اختيار تشغيلة واحدة لهذا الصف قبل تعديل الكمية.",
                    "تخصيص التشغيلات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                e.Cancel = true;
                return;
            }

            SaleInvoiceBatchAllocation selectedAllocation = null;

            if (item.BatchAllocations != null &&
                item.BatchAllocations.Count == 1)
            {
                selectedAllocation = item.BatchAllocations[0];

                if (selectedAllocation == null)
                {
                    MessageBox.Show(
                        "بيانات التشغيلة المرتبطة بهذا الصف غير صحيحة.",
                        "تخصيص التشغيلات",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    e.Cancel = true;
                    return;
                }

                if (!IsFinite(selectedAllocation.AvailableQuantity) ||
                    selectedAllocation.AvailableQuantity < 0)
                {
                    MessageBox.Show(
                        "الرصيد المتاح للتشغيلة غير صحيح.",
                        "رصيد التشغيلة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    e.Cancel = true;
                    return;
                }

                if (IsBatchExpiredOrInvalid(
                    selectedAllocation.ExpiryDate))
                {
                    MessageBox.Show(
                        "لا يمكن تعديل كمية صف مرتبط بتشغيلة منتهية الصلاحية " +
                        "أو تاريخ صلاحيتها غير صحيح.\n\n" +
                        "التشغيلة: " +
                        selectedAllocation.BatchNumber +
                        "\nالصلاحية: " +
                        selectedAllocation.ExpiryDate,
                        "صلاحية التشغيلة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    e.Cancel = true;
                    return;
                }

                // إعادة قراءة رصيد التشغيلة الفعلي من المخزون.
                // ثم خصم تخصيصات الصفوف الأخرى لنفس التشغيلة.
                List<ProductBatch> currentBatches =
                    productService.GetAvailableBatches(
                        currentUserId,
                        item.ProductId);

                ProductBatch currentBatch =
                    currentBatches == null
                        ? null
                        : currentBatches.FirstOrDefault(
                            x => x != null &&
                                 x.Id == selectedAllocation.BatchId);

                if (currentBatch == null ||
                    !IsFinite(currentBatch.Quantity) ||
                    currentBatch.Quantity < 0)
                {
                    MessageBox.Show(
                        "تعذر التحقق من الرصيد الحالي للتشغيلة.\n\n" +
                        "التشغيلة: " +
                        selectedAllocation.BatchNumber,
                        "رصيد التشغيلة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    e.Cancel = true;
                    return;
                }

                double allocatedByOtherRows =
                    invoiceItems
                        .Where(x =>
                            x != null &&
                            !ReferenceEquals(x, item) &&
                            x.ProductId == item.ProductId &&
                            x.BatchAllocations != null)
                        .SelectMany(x => x.BatchAllocations)
                        .Where(allocation =>
                            allocation != null &&
                            allocation.BatchId ==
                                selectedAllocation.BatchId &&
                            IsFinite(allocation.AllocatedQuantity) &&
                            allocation.AllocatedQuantity > 0)
                        .Sum(allocation =>
                            allocation.AllocatedQuantity);

                double remainingBatchQuantity = Math.Max(
                    0,
                    currentBatch.Quantity - allocatedByOtherRows);

                if (newPackageQuantity >
                    remainingBatchQuantity + BatchQuantityEpsilon)
                {
                    MessageBox.Show(
                        "الكمية الجديدة تتجاوز الرصيد المتبقي للتشغيلة بعد احتساب " +
                        "الكميات المخصصة في الصفوف الأخرى.\n\n" +
                        "التشغيلة: " +
                        selectedAllocation.BatchNumber +
                        "\nالكمية المطلوبة: " +
                        newPackageQuantity.ToString("0.###") +
                        "\nالرصيد الفعلي للتشغيلة: " +
                        currentBatch.Quantity.ToString("0.###") +
                        "\nالمخصص في الصفوف الأخرى: " +
                        allocatedByOtherRows.ToString("0.###") +
                        "\nالمتبقي لهذا الصف: " +
                        remainingBatchQuantity.ToString("0.###"),
                        "رصيد التشغيلة غير كافٍ",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    e.Cancel = true;
                    return;
                }

                // تحديث الرصيد المتاح المسجل لهذا الصف
                // ليعكس الرصيد المتبقي بعد تخصيص الصفوف الأخرى.
                selectedAllocation.AvailableQuantity =
                    remainingBatchQuantity;
            }

            double newTotal =
                RoundMoney(newQuantity * item.UnitPrice);

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    item.Quantity = newQuantity;
                    item.PackageQuantity = newPackageQuantity;

                    item.Total = newTotal;

                    // الصف مرتبط بتشغيلة واحدة:
                    // كمية الصرف من هذه التشغيلة تساوي كمية الصف الجديدة.
                    if (selectedAllocation != null)
                    {
                        selectedAllocation.AllocatedQuantity =
                            newPackageQuantity;
                    }

                    RefreshInvoice(item);
                }));
        }

        private void InvoiceDataGrid_CurrentCellChanged(
            object sender,
            EventArgs e)
        {
            if (!isUpdatingInvoice)
                return;

            UpdateGrandTotal();
        }

        private bool TryParseQuantity(
            string value,
            out double quantity)
        {
            if (double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.CurrentCulture,
                out quantity))
                return true;

            return double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out quantity);
        }

        private bool IsWholeQuantity(double quantity)
        {
            return Math.Abs(
                quantity - Math.Round(quantity)) < 0.000001;
        }

        private void RemoveInvoiceItemButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedItem =
                InvoiceDataGrid.SelectedItem as SaleInvoiceItem;

            if (selectedItem == null)
            {
                MessageBox.Show(
                    "اختر صنفًا من الفاتورة لحذفه.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            selectedItem.PropertyChanged -= InvoiceItem_PropertyChanged;

            invoiceItems.Remove(selectedItem);

            RefreshInvoice();
        }

        #endregion

        #region Batch Allocation

        private SaleInvoiceItem GetSelectedInvoiceItem()
        {
            return InvoiceDataGrid.SelectedItem as SaleInvoiceItem;
        }

        private void BatchSelectionButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SaleInvoiceItem item = GetSelectedInvoiceItem();

            if (item == null)
            {
                MessageBox.Show(
                    "اختر صنفًا من الفاتورة أولًا.",
                    "اختيار التشغيلات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            OpenBatchSelectionWindow(item);
        }


        private void OpenBatchSelectionWindow(
            SaleInvoiceItem item,
            bool removeItemOnCancel = false)
        {
            if (item == null)
                return;

            try
            {
                List<ProductBatch> availableBatches =
                    productService.GetAvailableBatches(
                        currentUserId,
                        item.ProductId);

                if (availableBatches == null)
                    availableBatches = new List<ProductBatch>();

                availableBatches = availableBatches
                    .Where(x => x != null && x.Id > 0)
                    .GroupBy(x => x.Id)
                    .Select(x => x.First())
                    .ToList();

                // خصم الكميات المخصصة لنفس التشغيلة في الصفوف الأخرى.
                // يتم استثناء الصف الحالي حتى لا تخصم كميته مرتين
                // عند إعادة فتح نافذة اختيار التشغيلة.
                foreach (ProductBatch batch in availableBatches)
                {
                    double allocatedByOtherRows = invoiceItems
                        .Where(x =>
                            x != null &&
                            !ReferenceEquals(x, item) &&
                            x.ProductId == item.ProductId &&
                            x.BatchAllocations != null)
                        .SelectMany(x => x.BatchAllocations)
                        .Where(allocation =>
                            allocation != null &&
                            allocation.BatchId == batch.Id &&
                            IsFinite(allocation.AllocatedQuantity) &&
                            allocation.AllocatedQuantity > 0)
                        .Sum(allocation =>
                            allocation.AllocatedQuantity);

                    double originalBatchQuantity =
                        IsFinite(batch.Quantity)
                            ? batch.Quantity
                            : 0;

                    batch.Quantity = Math.Max(
                        0,
                        originalBatchQuantity - allocatedByOtherRows);
                }

                // استبعاد التشغيلات التي لم يعد لها رصيد متاح
                // بعد احتساب الصفوف الأخرى.
                availableBatches = availableBatches
                    .Where(x =>
                        IsFinite(x.Quantity) &&
                        x.Quantity > BatchQuantityEpsilon)
                    .ToList();

                // لا نفتح نافذة اختيار فارغة.
                // يظل الصف موجودًا، لكن لن يسمح بالحفظ قبل تخصيص تشغيلة.
                if (availableBatches.Count == 0)
                {
                    UpdateBatchAllocationSummary(item);
                    UpdateGrandTotal();

                    MessageBox.Show(
                        "لا توجد تشغيلات متاحة لهذا الصنف بالكمية المطلوبة حاليًا.\n\n" +
                        "تمت إضافة الصنف إلى الفاتورة، لكن لا يمكن حفظ الفاتورة " +
                        "قبل تخصيص تشغيلة يتوفر بها رصيد كافٍ.",
                        "لا توجد تشغيلات متاحة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                BatchSelectionWindow window =
                    new BatchSelectionWindow(
                        item,
                        availableBatches);

                window.Owner = this;

                bool? result = window.ShowDialog();

                // معالجة الإلغاء أولًا قبل قراءة ConfirmedAllocations.
                if (result != true)
                {
                    // إذا كان الصنف جديدًا، نحذفه عند إلغاء اختيار التشغيلة.
                    if (removeItemOnCancel)
                    {
                        item.PropertyChanged -= InvoiceItem_PropertyChanged;

                        invoiceItems.Remove(item);

                        RefreshInvoice();

                        return;
                    }

                    // إذا كان الصنف موجودًا بالفعل، نحتفظ به
                    // وبأي تخصيص سابق دون تغييره.
                    UpdateBatchAllocationSummary(item);
                    UpdateGrandTotal();

                    return;
                }

                // التأكيد دون بيانات تخصيص حالة غير صحيحة.
                if (window.ConfirmedAllocations == null)
                {
                    MessageBox.Show(
                        "لم يتم استلام بيانات التشغيلة المختارة.\n\n" +
                        "أعد فتح نافذة اختيار التشغيلة وحاول مرة أخرى.",
                        "بيانات تخصيص غير صحيحة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    UpdateBatchAllocationSummary(item);
                    UpdateGrandTotal();

                    return;
                }

                // كل صف يجب أن يحتوي على تشغيلة واحدة فقط.
                if (window.ConfirmedAllocations.Count != 1)
                {
                    MessageBox.Show(
                        "يجب اختيار تشغيلة واحدة فقط لكل صف.",
                        "اختيار التشغيلات",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    UpdateBatchAllocationSummary(item);
                    UpdateGrandTotal();

                    return;
                }

                SaleInvoiceBatchAllocation confirmed =
                    window.ConfirmedAllocations[0];

                if (confirmed == null ||
                    confirmed.BatchId <= 0 ||
                    !IsFinite(confirmed.AllocatedQuantity) ||
                    Math.Abs(
                        confirmed.AllocatedQuantity -
                        item.PackageQuantity) > BatchQuantityEpsilon)
                {
                    MessageBox.Show(
                        "بيانات التشغيلة المختارة لا تطابق كمية الصف.\n\n" +
                        "أعد اختيار التشغيلة.",
                        "بيانات تخصيص غير صحيحة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    UpdateBatchAllocationSummary(item);
                    UpdateGrandTotal();

                    return;
                }

                if (item.BatchAllocations == null)
                {
                    item.BatchAllocations =
                        new ObservableCollection<SaleInvoiceBatchAllocation>();
                }

                item.BatchAllocations.Clear();

                item.BatchAllocations.Add(
                    new SaleInvoiceBatchAllocation
                    {
                        BatchId = confirmed.BatchId,
                        BatchNumber = confirmed.BatchNumber,
                        ExpiryDate = confirmed.ExpiryDate,
                        AvailableQuantity = confirmed.AvailableQuantity,
                        AllocatedQuantity = item.PackageQuantity
                    });

                UpdateBatchAllocationSummary(item);
                UpdateGrandTotal();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر فتح نافذة اختيار التشغيلات.\n\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void InvoiceDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isUpdatingInvoice)
                return;

            UpdateBatchAllocationSummary(GetSelectedInvoiceItem());
        }

        private static bool TryGetBatchExpiryDate(
            string expiryDate,
            out DateTime parsedDate)
        {
            parsedDate = DateTime.MinValue;

            if (string.IsNullOrWhiteSpace(expiryDate))
                return false;

            return DateTime.TryParseExact(
                expiryDate.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedDate);
        }


        private static bool IsBatchExpiredOrInvalid(string expiryDate)
        {
            // السماح ببيع التشغيلات التي ليس لها تاريخ صلاحية.
            if (string.IsNullOrWhiteSpace(expiryDate))
                return false;

            DateTime parsedDate;

            // رفض التاريخ المكتوب بصيغة غير صحيحة.
            if (!TryGetBatchExpiryDate(expiryDate, out parsedDate))
                return true;

            // السماح بالبيع في يوم انتهاء الصلاحية نفسه.
            return parsedDate.Date < DateTime.Today;
        }


        private bool AreBatchAllocationsValid(
            SaleInvoiceItem item,
            out double allocatedTotal,
            out string errorMessage)
        {
            allocatedTotal = 0;
            errorMessage = string.Empty;

            if (item == null)
            {
                errorMessage = "بيانات الصنف غير صحيحة.";
                return false;
            }

            if (!IsFinite(item.PackageQuantity) ||
                item.PackageQuantity <= 0)
            {
                errorMessage =
                    "الكمية المطلوبة غير صحيحة للصنف: " +
                    item.ItemName;

                return false;
            }

            if (item.BatchAllocations == null ||
                item.BatchAllocations.Count == 0)
            {
                errorMessage =
                    "لم يتم اختيار تشغيلة للصنف: " +
                    item.ItemName;

                return false;
            }

            // كل صف في الفاتورة يجب أن يحتوي على تشغيلة واحدة فقط.
            if (item.BatchAllocations.Count != 1)
            {
                errorMessage =
                    "يجب تخصيص تشغيلة واحدة فقط للصف الخاص بالصنف: " +
                    item.ItemName;

                return false;
            }

            SaleInvoiceBatchAllocation allocation =
                item.BatchAllocations[0];

            if (allocation == null)
            {
                errorMessage =
                    "بيانات التشغيلة غير صحيحة للصنف: " +
                    item.ItemName;

                return false;
            }

            if (allocation.BatchId <= 0)
            {
                errorMessage =
                    "رقم التشغيلة غير صحيح للصنف: " +
                    item.ItemName;

                return false;
            }

            if (!IsFinite(allocation.AllocatedQuantity) ||
                allocation.AllocatedQuantity <= 0)
            {
                errorMessage =
                    "كمية الصرف غير صحيحة للصنف: " +
                    item.ItemName;

                return false;
            }

            if (!IsFinite(allocation.AvailableQuantity) ||
                allocation.AvailableQuantity < 0)
            {
                errorMessage =
                    "الرصيد المتاح غير صحيح للتشغيلة: " +
                    allocation.BatchNumber +
                    "\nالصنف: " + item.ItemName;

                return false;
            }

            if (allocation.AllocatedQuantity >
                allocation.AvailableQuantity + BatchQuantityEpsilon)
            {
                errorMessage =
                    "كمية الصرف تتجاوز رصيد التشغيلة.\n\n" +
                    "الصنف: " + item.ItemName +
                    "\nالتشغيلة: " + allocation.BatchNumber +
                    "\nالمتاح: " +
                    allocation.AvailableQuantity.ToString("0.###") +
                    "\nالمطلوب صرفه: " +
                    allocation.AllocatedQuantity.ToString("0.###");

                return false;
            }

            if (IsBatchExpiredOrInvalid(allocation.ExpiryDate))
            {
                errorMessage =
                    "لا يمكن البيع من تشغيلة منتهية الصلاحية " +
                    "أو تاريخ صلاحيتها غير صحيح.\n\n" +
                    "الصنف: " + item.ItemName +
                    "\nالتشغيلة: " + allocation.BatchNumber +
                    "\nالصلاحية: " + allocation.ExpiryDate;

                return false;
            }

            if (Math.Abs(
                allocation.AllocatedQuantity -
                item.PackageQuantity) > BatchQuantityEpsilon)
            {
                errorMessage =
                    "كمية التشغيلة لا تطابق كمية الصف.\n\n" +
                    "الصنف: " + item.ItemName +
                    "\nكمية الصف: " +
                    item.PackageQuantity.ToString("0.###") +
                    "\nكمية التشغيلة: " +
                    allocation.AllocatedQuantity.ToString("0.###");

                return false;
            }

            allocatedTotal = allocation.AllocatedQuantity;

            return true;
        }

        private void UpdateBatchAllocationSummary(
            SaleInvoiceItem item)
        {
            if (item == null)
            {
                BatchRequiredText.Text = "0";
                BatchAllocatedText.Text = "0";
                BatchRemainingText.Text = "0";

                BatchAllocationStatusText.Text =
                    "اختر صنفًا من الفاتورة";

                BatchAllocationStatusText.Foreground =
                    System.Windows.Media.Brushes.Gray;

                return;
            }

            double required = item.PackageQuantity;
            double allocated = 0;

            bool validData = true;
            bool hasExpiredAllocation = false;
            bool hasSingleAllocation =
                item.BatchAllocations != null &&
                item.BatchAllocations.Count == 1;

            if (!IsFinite(required) || required <= 0)
                validData = false;

            if (!hasSingleAllocation)
            {
                validData = false;

                if (item.BatchAllocations != null)
                {
                    foreach (SaleInvoiceBatchAllocation allocation
                        in item.BatchAllocations)
                    {
                        if (allocation != null &&
                            IsFinite(allocation.AllocatedQuantity))
                        {
                            allocated += allocation.AllocatedQuantity;
                        }
                    }
                }
            }
            else
            {
                SaleInvoiceBatchAllocation allocation =
                    item.BatchAllocations[0];

                if (allocation == null ||
                    allocation.BatchId <= 0 ||
                    !IsFinite(allocation.AllocatedQuantity) ||
                    allocation.AllocatedQuantity < 0 ||
                    !IsFinite(allocation.AvailableQuantity) ||
                    allocation.AvailableQuantity < 0 ||
                    allocation.AllocatedQuantity >
                    allocation.AvailableQuantity + BatchQuantityEpsilon)
                {
                    validData = false;
                }
                else
                {
                    allocated = allocation.AllocatedQuantity;

                    if (IsBatchExpiredOrInvalid(allocation.ExpiryDate))
                    {
                        hasExpiredAllocation = true;
                        validData = false;
                    }

                    if (Math.Abs(
                        allocation.AllocatedQuantity - required) >
                        BatchQuantityEpsilon)
                    {
                        validData = false;
                    }
                }
            }

            BatchRequiredText.Text =
                IsFinite(required)
                    ? required.ToString("0.###")
                    : "—";

            BatchAllocatedText.Text =
                IsFinite(allocated)
                    ? allocated.ToString("0.###")
                    : "—";

            BatchRemainingText.Text =
                IsFinite(required) && IsFinite(allocated)
                    ? (required - allocated).ToString("0.###")
                    : "—";

            if (hasExpiredAllocation)
            {
                BatchAllocationStatusText.Text =
                    "التشغيلة منتهية الصلاحية أو تاريخها غير صحيح";

                BatchAllocationStatusText.Foreground =
                    System.Windows.Media.Brushes.DarkRed;
            }
            else if (item.BatchAllocations == null ||
                     item.BatchAllocations.Count == 0)
            {
                BatchAllocationStatusText.Text =
                    "لم يتم اختيار تشغيلة";

                BatchAllocationStatusText.Foreground =
                    System.Windows.Media.Brushes.DarkOrange;
            }
            else if (item.BatchAllocations.Count > 1)
            {
                BatchAllocationStatusText.Text =
                    "يجب اختيار تشغيلة واحدة فقط";

                BatchAllocationStatusText.Foreground =
                    System.Windows.Media.Brushes.DarkRed;
            }
            else if (!validData)
            {
                BatchAllocationStatusText.Text =
                    "بيانات التشغيلة أو الكمية غير متطابقة";

                BatchAllocationStatusText.Foreground =
                    System.Windows.Media.Brushes.DarkRed;
            }
            else
            {
                BatchAllocationStatusText.Text =
                    "التشغيلة محددة والكمية مطابقة";

                BatchAllocationStatusText.Foreground =
                    System.Windows.Media.Brushes.ForestGreen;
            }
        }

        private bool ValidateBatchAllocationsBeforeSave(
            out string errorMessage)
        {
            errorMessage = string.Empty;

            foreach (SaleInvoiceItem item in invoiceItems)
            {
                double allocatedTotal;
                string itemError;

                if (!AreBatchAllocationsValid(
                    item,
                    out allocatedTotal,
                    out itemError))
                {
                    errorMessage = itemError;
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Save Invoice

        private void SaveInvoiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isSaving)
                return;

            isSaving = true;
            SaveInvoiceButton.IsEnabled = false;

            try
            {
                InvoiceDataGrid.CommitEdit(
                    DataGridEditingUnit.Cell, true);

                InvoiceDataGrid.CommitEdit(
                    DataGridEditingUnit.Row, true);

                if (invoiceItems.Count == 0)
                {
                    MessageBox.Show(
                        "لا يمكن حفظ فاتورة فارغة.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                string batchAllocationError;

                if (!ValidateBatchAllocationsBeforeSave(
                    out batchAllocationError))
                {
                    MessageBox.Show(
                        batchAllocationError,
                        "مراجعة توزيع التشغيلات",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                int? customerId;
                double discountValue;
                double paidAmount;
                string discountType;
                string paymentMethod;
                string paymentReference;

                double cashAmount;
                double cardAmount;
                double instaPayAmount;
                string cardReference;

                if (!ValidateAccountingBeforeSave(
                    out customerId,
                    out discountValue,
                    out paidAmount,
                    out discountType,
                    out paymentMethod,
                    out paymentReference,
                    out cashAmount,
                    out cardAmount,
                    out instaPayAmount,
                    out cardReference))
                {
                    return;
                }

                MessageBoxResult confirmation =
                    MessageBox.Show(
                        "هل تريد تأكيد حفظ الفاتورة وخصم الكميات من المخزون؟",
                        "تأكيد البيع",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                bool invoiceSaved = false;
                string invoiceNumber = null;

                try
                {
                    if (IsSplitPaymentMethod(paymentMethod))
                    {
                        invoiceNumber =
                            salesService.SaveInvoice(
                                currentUserId,
                                invoiceItems.ToList(),
                                customerId,
                                discountType,
                                discountValue,
                                paidAmount,
                                paymentMethod,
                                paymentReference,
                                cardAmount,
                                cardReference,
                                cashAmount,
                                instaPayAmount);
                    }
                    else
                    {
                        invoiceNumber =
                            salesService.SaveInvoice(
                                currentUserId,
                                invoiceItems.ToList(),
                                customerId,
                                discountType,
                                discountValue,
                                paidAmount,
                                paymentMethod,
                                paymentReference);
                    }

                    invoiceSaved = true;

                    ClearSavedInvoice();

                    // إعادة تحميل الأصناف بالخلفية دون تجميد الواجهة.
                    LoadProductsAsync();

                    MessageBox.Show(
                        "تم حفظ الفاتورة بنجاح.\n\n" +
                        "رقم الفاتورة: " + invoiceNumber,
                        "نجاح عملية البيع",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    SaleInvoiceDetailsWindow detailsWindow =
                        new SaleInvoiceDetailsWindow(
                            currentUserId,
                            invoiceNumber);

                    detailsWindow.Owner = this;
                    detailsWindow.ShowDialog();
                }
                catch (UnauthorizedAccessException ex)
                {
                    if (invoiceSaved)
                    {
                        ShowPostSaveWarning(invoiceNumber, ex.Message);
                    }
                    else
                    {
                        MessageBox.Show(
                            ex.Message,
                            "الصلاحيات",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    if (invoiceSaved)
                    {
                        ShowPostSaveWarning(invoiceNumber, ex.Message);
                    }
                    else if (ex.Message.StartsWith(
                        "تغير سعر الصنف ",
                        StringComparison.Ordinal))
                    {
                        RefreshInvoicePricesAfterConfirmation();
                    }
                    else
                    {
                        MessageBox.Show(
                            ex.Message,
                            "تعذر حفظ الفاتورة",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    if (invoiceSaved)
                    {
                        ShowPostSaveWarning(invoiceNumber, ex.Message);
                    }
                    else
                    {
                        MessageBox.Show(
                            "تعذر حفظ الفاتورة.\n\n" +
                            ex.Message,
                            "خطأ في البيع",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            finally
            {
                isSaving = false;
                UpdateGrandTotal();
            }
        }

        private void ClearSavedInvoice()
        {
            foreach (SaleInvoiceItem item in invoiceItems)
            {
                item.PropertyChanged -= InvoiceItem_PropertyChanged;
            }

            invoiceItems.Clear();

            selectedSearchResult = null;
            selectedProduct = null;

            ResetSelectedProduct();

            ItemsDataGrid.SelectedItem = null;

            SaleUnitComboBox.SelectedIndex = 0;

            SearchTextBox.Clear();

            ResetAccountingInputs();

            RefreshInvoice();
        }

        private void ShowPostSaveWarning(
            string invoiceNumber,
            string errorMessage)
        {
            MessageBox.Show(
                "تم حفظ الفاتورة بنجاح، لكن حدثت مشكلة بعد الحفظ.\n\n" +
                "رقم الفاتورة: " + invoiceNumber + "\n\n" +
                errorMessage +
                "\n\nلا تحاول حفظ الفاتورة مرة أخرى.",
                "تم الحفظ",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void RefreshInvoicePricesAfterConfirmation()
        {
            MessageBoxResult result = MessageBox.Show(
                "تغير سعر صنف واحد أو أكثر بعد إضافته للفاتورة.\n\n" +
                "هل تريد تحديث أسعار جميع الأصناف الموجودة في الفاتورة " +
                "بالأسعار الحالية؟\n\n" +
                "لن يتم حفظ الفاتورة تلقائيًا.",
                "تحديث أسعار الفاتورة",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                List<SalesProductSearchResult> refreshedResults =
                    searchService.GetSearchResults(currentUserId);

                var updatedPrices =
                    new Dictionary<int, double>();

                var updatedDiscountLimits =
                    new Dictionary<int, double>();

                foreach (SaleInvoiceItem item in invoiceItems)
                {
                    SalesProductSearchResult searchResult =
                        refreshedResults.FirstOrDefault(
                            p => p.Product != null &&
                                 p.Product.Id == item.ProductId);

                    if (searchResult == null ||
                        searchResult.Product.Quantity <= 0)
                    {
                        throw new InvalidOperationException(
                            "الصنف " + item.ItemName +
                            " لم يعد متاحًا للبيع أو أصبح رصيده صفرًا.");
                    }

                    Product product = searchResult.Product;

                    double totalPackageQuantity =
                        invoiceItems
                            .Where(x => x.ProductId == item.ProductId)
                            .Sum(x => x.PackageQuantity);

                    if (totalPackageQuantity >
                        product.Quantity + 0.000001)
                    {
                        throw new InvalidOperationException(
                            "الرصيد الحالي للصنف " + item.ItemName +
                            " لا يكفي الكمية الموجودة في الفاتورة.\n" +
                            "الرصيد المتاح: " +
                            product.Quantity.ToString("0.###") +
                            "\nالكمية المطلوبة: " +
                            totalPackageQuantity.ToString("0.###"));
                    }

                    if (product.UnitsPerPackage != item.UnitsPerPackage ||
                        !string.Equals(
                            product.UnitName,
                            item.PackageUnitName,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            product.SmallUnitName ?? "",
                            item.SmallUnitName ?? "",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "تغيرت بيانات وحدات الصنف " +
                            item.ItemName +
                            ". يرجى إزالة الصنف وإضافته مرة أخرى.");
                    }

                    double newUnitPrice;

                    if (string.Equals(
                        item.UnitName,
                        product.UnitName,
                        StringComparison.Ordinal))
                    {
                        newUnitPrice = product.SalePrice;
                    }
                    else if (!string.IsNullOrWhiteSpace(product.SmallUnitName) &&
                             string.Equals(
                                 item.UnitName,
                                 product.SmallUnitName,
                                 StringComparison.Ordinal))
                    {
                        newUnitPrice =
                            product.SalePrice / product.UnitsPerPackage;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "وحدة الصنف " + item.ItemName +
                            " لم تعد متاحة.");
                    }

                    if (!IsFinite(newUnitPrice) ||
                        newUnitPrice < 0)
                    {
                        throw new InvalidOperationException(
                            "السعر الحالي للصنف " +
                            item.ItemName + " غير صحيح.");
                    }

                    if (!IsFinite(product.MaxDiscountPercentage) ||
                        product.MaxDiscountPercentage < 0 ||
                        product.MaxDiscountPercentage > 100)
                    {
                        throw new InvalidOperationException(
                            "حد الخصم الحالي للصنف " +
                            item.ItemName + " غير صحيح.");
                    }

                    if (item.DiscountPercentage >
                        product.MaxDiscountPercentage + 0.000001)
                    {
                        throw new InvalidOperationException(
                            "تم تخفيض الحد الأقصى للخصم للصنف " +
                            item.ItemName + ".\n" +
                            "النسبة الحالية في الفاتورة: " +
                            item.DiscountPercentage.ToString("0.##") +
                            "%\nالحد الجديد: " +
                            product.MaxDiscountPercentage.ToString("0.##") +
                            "%\n\nراجع نسبة الخصم قبل متابعة البيع.");
                    }

                    updatedPrices[item.ProductId] = newUnitPrice;

                    updatedDiscountLimits[item.ProductId] =
                        product.MaxDiscountPercentage;
                }

                allSearchResults = refreshedResults;

                // إعادة بناء الفهرس لأن نتائج الأصناف تغيرت.
                BuildProductSearchIndex();

                foreach (SaleInvoiceItem item in invoiceItems)
                {
                    item.UnitPrice = updatedPrices[item.ProductId];

                    item.MaxDiscountPercentage =
                        updatedDiscountLimits[item.ProductId];

                    item.Total = RoundMoney(
                        item.Quantity * item.UnitPrice);
                }

                ApplySearchFilter();
                RefreshInvoice();

                MessageBox.Show(
                    "تم تحديث أسعار الأصناف وحدود الخصم في الفاتورة.\n\n" +
                    "راجع الإجمالي الجديد واضغط حفظ مرة أخرى عند التأكد.",
                    "تم تحديث الأسعار",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحديث أسعار الفاتورة.\n\n" +
                    ex.Message +
                    "\n\nلم يتم حفظ الفاتورة.",
                    "تعذر تحديث الأسعار",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        #endregion

        #region New Invoice

        private void NewInvoiceButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (invoiceItems.Count > 0)
            {
                MessageBoxResult result =
                    MessageBox.Show(
                        "هل تريد بدء فاتورة جديدة؟ سيتم حذف الأصناف الحالية من الفاتورة.",
                        "فاتورة جديدة",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            foreach (SaleInvoiceItem item in invoiceItems)
            {
                item.PropertyChanged -= InvoiceItem_PropertyChanged;
            }

            invoiceItems.Clear();

            selectedSearchResult = null;
            selectedProduct = null;

            ItemsDataGrid.SelectedItem = null;

            ResetSelectedProduct();

            SearchTextBox.Clear();

            SaleUnitComboBox.SelectedIndex = 0;

            ResetAccountingInputs();

            RefreshInvoice();
        }

        private void CloseWindowButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (searchDebounceTimer != null)
                searchDebounceTimer.Stop();

            Close();
        }

        #endregion

        #region Invoice Display

        private void RefreshInvoice(
            SaleInvoiceItem itemToSelect = null)
        {
            isUpdatingInvoice = true;

            try
            {
                if (itemToSelect == null)
                    itemToSelect = GetSelectedInvoiceItem();

                // لا نعيد إنشاء مصدر بيانات جديد.
                // InvoiceDataGrid مرتبط بالفعل بـ invoiceItems.
                if (itemToSelect != null &&
                    invoiceItems.Contains(itemToSelect))
                {
                    InvoiceDataGrid.SelectedItem = itemToSelect;
                }
                else
                {
                    InvoiceDataGrid.SelectedItem = null;
                }

                UpdateGrandTotal();
            }
            finally
            {
                isUpdatingInvoice = false;
            }

            UpdateBatchAllocationSummary(GetSelectedInvoiceItem());
        }

        #endregion
    }

    internal class DiscountFraction
    {
        public SaleInvoiceItem Item { get; set; }

        public decimal Fraction { get; set; }

        public int OriginalOrder { get; set; }
    }
}
