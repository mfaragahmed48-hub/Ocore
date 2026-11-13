namespace Ocore.desktop.Models
{
    public class SalesReturnHistoryItem
    {
        public int Id { get; set; }

        public string ReturnNumber { get; set; }

        public string InvoiceNumber { get; set; }

        public string EmployeeName { get; set; }

        public double Total { get; set; }

        public string Reason { get; set; }

        public string Status { get; set; }

        public string CreatedAt { get; set; }
    }
}