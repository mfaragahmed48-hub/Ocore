
using System;
using System.Collections.Generic;

namespace Ocore.desktop.Models
{
    public class SalesReturnDetails
    {
        public int Id { get; set; }

        public string ReturnNumber { get; set; }

        public int InvoiceId { get; set; }

        public string InvoiceNumber { get; set; }

        public int UserId { get; set; }

        public string EmployeeName { get; set; }

        public double Total { get; set; }

        public string Reason { get; set; }

        public string Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<SalesReturnItem> Items { get; set; }

        public SalesReturnDetails()
        {
            Items = new List<SalesReturnItem>();
        }
    }
}
