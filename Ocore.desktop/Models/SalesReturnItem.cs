
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

        // الإجماليات
        public double Total { get; set; }

        public double PackageQuantity { get; set; }

        public SalesReturnItem()
        {
            ReturnUnits = new List<string>();
        }
    }
}
