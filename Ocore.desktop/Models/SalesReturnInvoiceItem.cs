namespace Ocore.desktop.Models
{
    public class SalesReturnInvoiceItem
    {
        public int InvoiceItemId { get; set; }

        public int ProductId { get; set; }

        public string ItemName { get; set; }

        public string UnitName { get; set; }

        public double SoldQuantity { get; set; }

        public double ReturnedQuantity { get; set; }

        public double RemainingQuantity { get; set; }

        public double Quantity { get; set; }

        public double UnitPrice { get; set; }

        public double Total { get; set; }

        public double PackageQuantity { get; set; }
    }
}