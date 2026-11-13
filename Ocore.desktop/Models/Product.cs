
namespace Ocore.desktop.Models
{
    public class Product
    {
        public int Id { get; set; }

        public string ProductCode { get; set; }

        public string Name { get; set; }

        // معرف الدواء المرتبط من دليل الأدوية
        // null للأصناف القديمة أو غير المرتبطة بالدليل
        public int? DrugCatalogId { get; set; }

        public int? CategoryId { get; set; }

        // الوحدة الأساسية مثل علبة أو شريط
        public string UnitName { get; set; }

        // الوحدة الأصغر مثل شريط أو قرص
        public string SmallUnitName { get; set; }

        // عدد الوحدات الصغيرة داخل العبوة
        public int UnitsPerPackage { get; set; } = 1;

        public double SalePrice { get; set; }

        public double PurchasePrice { get; set; }

        // أقصى نسبة خصم مسموح بها لهذا الصنف
        // صفر يعني عدم السماح بأي خصم
        public double MaxDiscountPercentage { get; set; } = 0;

        // الرصيد الصالح للبيع
        public double Quantity { get; set; }

        // إجمالي المخزون بما فيه المنتهي
        public double TotalQuantity { get; set; }

        // الكمية منتهية الصلاحية
        public double ExpiredQuantity { get; set; }

        public bool IsArchived { get; set; }

        public string Barcode { get; set; }
    }
}
