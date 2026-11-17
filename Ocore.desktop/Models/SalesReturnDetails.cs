
using System;
using System.Collections.Generic;

namespace Ocore.desktop.Models
{
    public class SalesReturnDetails
    {
        public double RefundAmount { get; set; }
        public string RefundPaymentMethod { get; set; }
        public int Id { get; set; }

        public string ReturnNumber { get; set; }

        public int InvoiceId { get; set; }

        public string InvoiceNumber { get; set; }

        public int UserId { get; set; }

        public string EmployeeName { get; set; }

        // إجمالي البنود قبل الخصم والضريبة
        public double Total { get; set; }

        // الخصم الموزع على بنود المرتجع
        public double DiscountAmount { get; set; }

        // الوعاء الضريبي
        public double TaxableAmount { get; set; }

        // ضريبة القيمة المضافة
        public double VatAmount { get; set; }

        // ضريبة الجدول
        public double ScheduleTaxAmount { get; set; }

        // إجمالي الضرائب
        public double TaxTotal { get; set; }

        // Calculated / Legacy / Mixed
        public string TaxStatus { get; set; }

        public string Reason { get; set; }

        public string Status { get; set; }

        public DateTime CreatedAt { get; set; }

        public List<SalesReturnItem> Items { get; set; }

        public SalesReturnDetails()
        {
            Items = new List<SalesReturnItem>();
            TaxStatus = "Legacy";
        }
    }
}
