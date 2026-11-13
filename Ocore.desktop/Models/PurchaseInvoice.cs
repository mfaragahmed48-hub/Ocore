using System;
using System.Collections.Generic;

namespace Ocore.desktop.Models
{
    public class PurchaseInvoice
    {
        public int Id { get; set; }

        public string InvoiceNumber { get; set; }

        public int SupplierId { get; set; }

        public string SupplierName { get; set; }

        public int UserId { get; set; }

        public double Total { get; set; }

        public string Status { get; set; }

        public DateTime InvoiceDate { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<PurchaseInvoiceItem> Items { get; set; }
            = new List<PurchaseInvoiceItem>();
    }
}