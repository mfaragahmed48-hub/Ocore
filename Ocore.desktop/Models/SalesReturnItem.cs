
using System.Collections.Generic;

namespace Ocore.desktop.Models
{
    public class SalesReturnItem
    {
        public int Id { get; set; }

        public int ReturnId { get; set; }

        // بيانات عملية البيع الأصلية
        public int InvoiceId { get; set; }

        public string InvoiceNumber { get; set; }

        public string InvoiceDate { get; set; }

        public string EmployeeName { get; set; }

        public int InvoiceItemId { get; set; }

        // بيانات الصنف
        public int ProductId { get; set; }

        public string ProductCode { get; set; }

        public string ItemName { get; set; }

        // وحدة البيع الأصلية
        public string UnitName { get; set; }

        public double UnitPrice { get; set; }

        // بيانات الوحدة الصغيرة
        public string SmallUnitName { get; set; }

        public double UnitsPerPackage { get; set; }

        // وحدات المرتجع المتاحة
        public List<string> ReturnUnits { get; set; }

        // وحدة المرتجع المختارة
        public string ReturnUnitName { get; set; }

        public double ReturnUnitPrice { get; set; }

        public double ReturnUnitQuantity { get; set; }

        // الكميات الأصلية
        public double SoldQuantity { get; set; }

        public double ReturnedQuantity { get; set; }

        public double RemainingQuantity { get; set; }

        // الكمية المحولة إلى وحدة البيع الأصلية
        public double Quantity { get; set; }

        // ==========================================
        // بيانات الضريبة المستردة من الفاتورة الأصلية
        // ==========================================

        public double TaxableAmount { get; set; }

        public double VatAmount { get; set; }

        public double ScheduleTaxAmount { get; set; }

        public double TaxTotal { get; set; }

        public string TaxStatus { get; set; }

        // مكونات الضريبة المستردة
        public List<SalesReturnTaxComponent> TaxComponents { get; set; }

        // ==========================================
        // الإجماليات
        // ==========================================

        public double Total { get; set; }

        public double PackageQuantity { get; set; }

        public SalesReturnItem()
        {
            ReturnUnits = new List<string>();

            TaxStatus = string.Empty;

            TaxComponents =
                new List<SalesReturnTaxComponent>();
        }
    }

    // يمثل مكونًا ضريبيًا مستردًا من بند الفاتورة الأصلية
    public class SalesReturnTaxComponent
    {
        public int OriginalRateId { get; set; }

        public string ComponentCode { get; set; }

        public string ComponentName { get; set; }

        public string ComponentType { get; set; }

        public double RatePercent { get; set; }

        public int CalculationOrder { get; set; }

        public bool IsCompound { get; set; }

        public double TaxableBase { get; set; }

        public double TaxAmount { get; set; }

        public SalesReturnTaxComponent()
        {
            ComponentCode = string.Empty;
            ComponentName = string.Empty;
            ComponentType = string.Empty;
        }
    }
}
