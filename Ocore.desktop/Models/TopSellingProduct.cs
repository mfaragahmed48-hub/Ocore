namespace Ocore.desktop.Models
{
    public class TopSellingProduct
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; }

        public double TotalQuantity { get; set; }

        public double TotalSales { get; set; }

        public int InvoiceCount { get; set; }

        public TopSellingProduct()
        {
            ProductName = string.Empty;
            TotalQuantity = 0;
            TotalSales = 0;
            InvoiceCount = 0;
        }
    }
}