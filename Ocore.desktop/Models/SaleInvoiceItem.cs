
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Ocore.desktop.Models
{
    public class SaleInvoiceItem : INotifyPropertyChanged
    {
        private double quantity;
        private double unitPrice;
        private double total;
        private double packageQuantity;
        private string unitName;
        private bool isSmallUnit;

        private double maxDiscountPercentage;
        private double discountPercentage;
        private double discountAmount;

        private ObservableCollection<SaleInvoiceBatchAllocation>
            batchAllocations;

        public SaleInvoiceItem()
        {
            BatchAllocations =
                new ObservableCollection<SaleInvoiceBatchAllocation>();

            TaxProfileCode = string.Empty;
            TaxProfileName = string.Empty;
            TaxType = string.Empty;
            TaxPriceMode = string.Empty;
            TaxStatus = string.Empty;

            TaxComponents =
                new List<SaleInvoiceItemTaxComponent>();
        }

        public int InvoiceItemId { get; set; }

        public int ProductId { get; set; }

        public string ItemName { get; set; }

        public string PackageUnitName { get; set; }

        public string SmallUnitName { get; set; }

        public List<string> UnitOptions { get; set; }

        public int UnitsPerPackage { get; set; } = 1;

        // ==========================================
        // البيانات الضريبية المحفوظة وقت إصدار الفاتورة
        // ==========================================

        public int? TaxProfileId { get; set; }

        public string TaxProfileCode { get; set; }

        public string TaxProfileName { get; set; }

        public string TaxType { get; set; }

        public string TaxPriceMode { get; set; }

        public double TaxRate { get; set; }

        // الوعاء الضريبي بعد الخصم
        public double TaxableAmount { get; set; }

        // ضريبة القيمة المضافة
        public double VatAmount { get; set; }

        // ضريبة الجدول
        public double ScheduleTaxAmount { get; set; }

        // إجمالي الضرائب
        public double TaxTotal { get; set; }

        // حالة حساب الضريبة
        public string TaxStatus { get; set; }

        // مكونات الضرائب المحفوظة لهذا البند
        public List<SaleInvoiceItemTaxComponent> TaxComponents
        {
            get;
            set;
        }

        // ==========================================
        // التشغيلات
        // ==========================================

        public ObservableCollection<SaleInvoiceBatchAllocation>
            BatchAllocations
        {
            get { return batchAllocations; }
            set
            {
                if (ReferenceEquals(batchAllocations, value))
                    return;

                UnsubscribeFromBatchAllocations();

                batchAllocations = value ??
                    new ObservableCollection<SaleInvoiceBatchAllocation>();

                SubscribeToBatchAllocations();

                OnPropertyChanged();
                NotifyBatchAllocationChanged();
            }
        }

        // ==========================================
        // الخصومات
        // ==========================================

        public double MaxDiscountPercentage
        {
            get { return maxDiscountPercentage; }
            set
            {
                if (maxDiscountPercentage == value)
                    return;

                maxDiscountPercentage = value;
                OnPropertyChanged();
            }
        }

        public double DiscountPercentage
        {
            get { return discountPercentage; }
            set
            {
                if (discountPercentage == value)
                    return;

                discountPercentage = value;
                OnPropertyChanged();
            }
        }

        // يحدد هل المستخدم عدّل خصم هذا الصنف يدويًا.
        // عند التعديل اليدوي يُستثنى الصنف من الخصم العام.
        public bool HasManualDiscountOverride { get; set; } = false;

        public double DiscountAmount
        {
            get { return discountAmount; }
            set
            {
                if (discountAmount == value)
                    return;

                discountAmount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NetTotal));
                OnPropertyChanged(nameof(AppliedDiscountPercentage));
            }
        }

        public double Total
        {
            get { return total; }
            set
            {
                if (total == value)
                    return;

                total = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NetTotal));
                OnPropertyChanged(nameof(AppliedDiscountPercentage));
            }
        }

        public double NetTotal
        {
            get
            {
                double result = Total - DiscountAmount;
                return result < 0 ? 0 : result;
            }
        }

        // النسبة الفعلية للخصم بعد احتساب الخصم اليدوي والخصم العام.
        public double AppliedDiscountPercentage
        {
            get
            {
                if (Total <= 0)
                    return 0;

                return (DiscountAmount / Total) * 100.0;
            }
        }

        // ==========================================
        // بيانات الوحدة والكمية
        // ==========================================

        public string UnitName
        {
            get { return unitName; }
            set
            {
                if (unitName == value)
                    return;

                unitName = value;
                OnPropertyChanged();
            }
        }

        public bool IsSmallUnit
        {
            get { return isSmallUnit; }
            set
            {
                if (isSmallUnit == value)
                    return;

                isSmallUnit = value;
                OnPropertyChanged();
            }
        }

        public double Quantity
        {
            get { return quantity; }
            set
            {
                if (quantity == value)
                    return;

                quantity = value;
                OnPropertyChanged();
            }
        }

        public double UnitPrice
        {
            get { return unitPrice; }
            set
            {
                if (unitPrice == value)
                    return;

                unitPrice = value;
                OnPropertyChanged();
            }
        }

        public double PackageQuantity
        {
            get { return packageQuantity; }
            set
            {
                if (packageQuantity == value)
                    return;

                packageQuantity = value;
                OnPropertyChanged();

                // تغيير الكمية المطلوبة يؤثر على اكتمال التوزيع.
                NotifyBatchAllocationChanged();
            }
        }

        // ==========================================
        // حساب توزيع التشغيلات
        // ==========================================

        // إجمالي الكمية التي وزعها المستخدم على التشغيلات.
        public double AllocatedQuantity
        {
            get
            {
                double allocated = 0;

                if (BatchAllocations != null)
                {
                    foreach (var allocation in BatchAllocations)
                    {
                        if (allocation != null)
                            allocated += allocation.AllocatedQuantity;
                    }
                }

                return allocated;
            }
        }

        // هل التوزيع اليدوي يساوي الكمية المطلوبة؟
        public bool IsBatchAllocationComplete
        {
            get
            {
                return Math.Abs(
                    AllocatedQuantity - PackageQuantity) < 0.0000001;
            }
        }

        // الكمية التي لم يوزعها المستخدم بعد.
        public double RemainingAllocationQuantity
        {
            get
            {
                double remaining =
                    PackageQuantity - AllocatedQuantity;

                return remaining > 0 ? remaining : 0;
            }
        }

        // ==========================================
        // إشعارات التغيير
        // ==========================================

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

        public void NotifyBatchAllocationChanged()
        {
            OnPropertyChanged(nameof(AllocatedQuantity));
            OnPropertyChanged(nameof(RemainingAllocationQuantity));
            OnPropertyChanged(nameof(IsBatchAllocationComplete));
        }

        // ==========================================
        // الاشتراك في تغييرات التشغيلات
        // ==========================================

        private void SubscribeToBatchAllocations()
        {
            if (batchAllocations == null)
                return;

            batchAllocations.CollectionChanged +=
                BatchAllocations_CollectionChanged;

            foreach (var allocation in batchAllocations)
                SubscribeToAllocation(allocation);
        }

        private void UnsubscribeFromBatchAllocations()
        {
            if (batchAllocations == null)
                return;

            batchAllocations.CollectionChanged -=
                BatchAllocations_CollectionChanged;

            foreach (var allocation in batchAllocations)
                UnsubscribeFromAllocation(allocation);
        }

        private void BatchAllocations_CollectionChanged(
            object sender,
            NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (SaleInvoiceBatchAllocation allocation in e.OldItems)
                    UnsubscribeFromAllocation(allocation);
            }

            if (e.NewItems != null)
            {
                foreach (SaleInvoiceBatchAllocation allocation in e.NewItems)
                    SubscribeToAllocation(allocation);
            }

            // عند استبدال عناصر المجموعة بالكامل.
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                if (batchAllocations != null)
                {
                    foreach (var allocation in batchAllocations)
                        SubscribeToAllocation(allocation);
                }
            }

            NotifyBatchAllocationChanged();
        }

        private void SubscribeToAllocation(
            SaleInvoiceBatchAllocation allocation)
        {
            if (allocation == null)
                return;

            allocation.PropertyChanged -=
                Allocation_PropertyChanged;

            allocation.PropertyChanged +=
                Allocation_PropertyChanged;
        }

        private void UnsubscribeFromAllocation(
            SaleInvoiceBatchAllocation allocation)
        {
            if (allocation == null)
                return;

            allocation.PropertyChanged -=
                Allocation_PropertyChanged;
        }

        private void Allocation_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(
                SaleInvoiceBatchAllocation.AllocatedQuantity))
            {
                NotifyBatchAllocationChanged();
            }
        }
    }

    // ==========================================
    // مكون ضريبي واحد داخل بند الفاتورة
    // ==========================================

    public class SaleInvoiceItemTaxComponent
    {
        public int RateId { get; set; }

        public string ComponentCode { get; set; }

        public string ComponentName { get; set; }

        public string ComponentType { get; set; }

        public double RatePercent { get; set; }

        public int CalculationOrder { get; set; }

        public bool IsCompound { get; set; }

        public double TaxableBase { get; set; }

        public double TaxAmount { get; set; }

        public SaleInvoiceItemTaxComponent()
        {
            ComponentCode = string.Empty;
            ComponentName = string.Empty;
            ComponentType = string.Empty;
        }
    }

    // يمثل كمية صرف يحددها المستخدم من تشغيلة واحدة.
    public class SaleInvoiceBatchAllocation : INotifyPropertyChanged
    {
        private double allocatedQuantity;

        public int BatchId { get; set; }

        public string BatchNumber { get; set; }

        public string ExpiryDate { get; set; }

        // الرصيد المتاح من التشغيلة وقت تحميلها.
        public double AvailableQuantity { get; set; }

        // الكمية التي اختار المستخدم صرفها من هذه التشغيلة.
        public double AllocatedQuantity
        {
            get { return allocatedQuantity; }
            set
            {
                if (allocatedQuantity == value)
                    return;

                allocatedQuantity = value;
                OnPropertyChanged();
            }
        }

        public string DisplayName
        {
            get
            {
                string batch = string.IsNullOrWhiteSpace(BatchNumber)
                    ? "بدون رقم تشغيلة"
                    : BatchNumber;

                string expiry = string.IsNullOrWhiteSpace(ExpiryDate)
                    ? "بدون تاريخ صلاحية"
                    : ExpiryDate;

                return batch + " | الصلاحية: " + expiry;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(
            [CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }
    }
}
