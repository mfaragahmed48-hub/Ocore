using System;

namespace Ocore.desktop.Models
{
    public class SalesReportSummary
    {
        public DateTime FromDate { get; set; }

        public DateTime ToDate { get; set; }

        public int InvoiceCount { get; set; }

        public double TotalSales { get; set; }

        public double TotalCost { get; set; }

        public double NetProfit { get; set; }

        public bool IsCostComplete { get; set; }

        public SalesReportSummary()
        {
            InvoiceCount = 0;
            TotalSales = 0;
            TotalCost = 0;
            NetProfit = 0;
            IsCostComplete = true;
        }
    }
}