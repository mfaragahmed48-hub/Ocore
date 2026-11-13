
namespace Ocore.desktop.Models
{
    public class SalesProductSearchResult
    {
        // بيانات الصنف الفعلي في المخزون
        public Product Product { get; set; }

        // بيانات الدواء من الدليل
        public int? CatalogId { get; set; }

        public string ProductCode { get; set; }

        public string Name { get; set; }

        public string TradeNameArabic { get; set; }

        public string GenericName { get; set; }

        public string Strength { get; set; }

        public string DosageForm { get; set; }

        public string RouteOfAdministration { get; set; }

        public string Manufacturer { get; set; }

        public string DrugClass { get; set; }

        public string UnitName { get; set; }

        public string SmallUnitName { get; set; }

        public int UnitsPerPackage { get; set; } = 1;

        public double SalePrice { get; set; }

        public double Quantity { get; set; }

        public string Barcode { get; set; }

        // هل الدواء مضاف فعليًا إلى المخزون؟
        public bool IsInInventory
        {
            get { return Product != null; }
        }

        public string InventoryStatus
        {
            get
            {
                return IsInInventory
                    ? "مضاف للمخزون"
                    : "غير مضاف للمخزون";
            }
        }

        public string DisplayPrice
        {
            get
            {
                return IsInInventory
                    ? SalePrice.ToString("0.00")
                    : "غير مسجل";
            }
        }
    }
}
