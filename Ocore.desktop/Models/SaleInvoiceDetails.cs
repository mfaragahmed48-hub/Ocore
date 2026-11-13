
using System;
using System.Collections.Generic;

namespace Ocore.desktop.Models
{
    public class SaleInvoiceDetails
    {
        public long Id { get; set; }

        public string InvoiceNumber { get; set; }

        public string EmployeeName { get; set; }

        public string CustomerName { get; set; }

        public string Status { get; set; }

        public DateTime CreatedAt { get; set; }

        // إجمالي الأصناف قبل الخصم
        public double Subtotal { get; set; }

        // بيانات الخصم
        public string DiscountType { get; set; }

        public double DiscountValue { get; set; }

        public double DiscountAmount { get; set; }

        // صافي الفاتورة بعد الخصم
        public double NetTotal { get; set; }

        // إجمالي المدفوع والمتبقي
        public double PaidAmount { get; set; }

        public double RemainingAmount { get; set; }

        public string PaymentStatus { get; set; }

        // التوافق مع الفواتير القديمة
        public double Total { get; set; }

        public List<SaleInvoiceItem> Items { get; set; }

        public List<SaleInvoicePaymentDetails> Payments { get; set; }

        public SaleInvoiceDetails()
        {
            InvoiceNumber = string.Empty;
            EmployeeName = string.Empty;
            CustomerName = string.Empty;
            Status = string.Empty;
            DiscountType = string.Empty;
            PaymentStatus = string.Empty;

            Items = new List<SaleInvoiceItem>();
            Payments = new List<SaleInvoicePaymentDetails>();
        }
    }

    public class SaleInvoicePaymentDetails
    {
        public long Id { get; set; }

        public string PaymentMethod { get; set; }

        public double Amount { get; set; }

        public string ReferenceNumber { get; set; }

        public string Notes { get; set; }

        public DateTime CreatedAt { get; set; }

        public SaleInvoicePaymentDetails()
        {
            PaymentMethod = string.Empty;
            ReferenceNumber = string.Empty;
            Notes = string.Empty;
        }
    }
}
