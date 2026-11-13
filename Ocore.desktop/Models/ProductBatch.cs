
namespace Ocore.desktop.Models
{
    public class ProductBatch
    {
        public int Id { get; set; }

        public string BatchNumber { get; set; }

        public string ExpiryDate { get; set; }

        public double Quantity { get; set; }

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

                return batch + " | الصلاحية: " + expiry
                    + " | المتاح: " + Quantity;
            }
        }
    }
}
