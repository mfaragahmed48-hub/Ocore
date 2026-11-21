
namespace Ocore.desktop.Models
{
    public class TopSellingProduct
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; }

        // إجمالي الكمية المباعة بوحدة PackageQuantity
        public double TotalQuantity { get; set; }

        // الكمية المرتجعة من مبيعات الفترة
        public double ReturnedQuantity { get; set; }

        // الكمية المباعة بعد خصم المرتجعات
        public double NetQuantity { get; set; }

        // قيمة المبيعات بعد الخصومات وقبل المرتجعات
        public double TotalSales { get; set; }

        // قيمة المرتجعات
        public double ReturnedSales { get; set; }

        // صافي قيمة المبيعات بعد المرتجعات
        public double NetSales { get; set; }

        public int InvoiceCount { get; set; }

        // الرصيد الحالي الصالح للبيع
        public double CurrentStock { get; set; }

        // الرصيد المنتهي الصلاحية
        public double ExpiredQuantity { get; set; }

        public TopSellingProduct()
        {
            ProductName = string.Empty;
            TotalQuantity = 0;
            ReturnedQuantity = 0;
            NetQuantity = 0;
            TotalSales = 0;
            ReturnedSales = 0;
            NetSales = 0;
            InvoiceCount = 0;
            CurrentStock = 0;
            ExpiredQuantity = 0;
        }
    }
}
