
namespace Ocore.desktop.Models.Printing
{
    /// <summary>
    /// تعريف وظائف الطباعة داخل البرنامج.
    /// كل وظيفة لها إعدادات مستقلة.
    /// </summary>
    public static class PrintingFunction
    {
        // طباعة فواتير البيع
        public const string CashierSales = "CashierSales";

        // طباعة مرتجعات البيع
        public const string CashierReturns = "CashierReturns";

        // طباعة الباركود
        public const string Barcode = "Barcode";

        // طباعة فواتير الشراء
        public const string PurchaseInvoices = "PurchaseInvoices";

        // طباعة التقارير
        public const string Reports = "Reports";

        // طباعة المستندات العامة
        public const string Documents = "Documents";
    }
}
