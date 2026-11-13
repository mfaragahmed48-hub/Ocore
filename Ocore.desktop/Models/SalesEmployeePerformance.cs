namespace Ocore.desktop.Models
{
    public class SalesEmployeePerformance
    {
        public int UserId { get; set; }

        public string EmployeeName { get; set; }

        public int InvoiceCount { get; set; }

        public double TotalSales { get; set; }

        public double AverageInvoiceValue { get; set; }

        public SalesEmployeePerformance()
        {
            EmployeeName = string.Empty;
            InvoiceCount = 0;
            TotalSales = 0;
            AverageInvoiceValue = 0;
        }
    }
}