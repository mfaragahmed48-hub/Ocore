
namespace Ocore.desktop.Models.Printing
{
    /// <summary>
    /// الأنواع الثلاثة المعتمدة لإعدادات الطابعات.
    /// </summary>
    public static class PrinterType
    {
        /// <summary>
        /// فواتير البيع ومرتجعات البيع.
        /// </summary>
        public const string Cashier = "Cashier";

        /// <summary>
        /// فواتير الشراء والتقارير والمستندات.
        /// </summary>
        public const string Normal = "Normal";

        /// <summary>
        /// ملصقات الباركود بنظام Roll فقط.
        /// </summary>
        public const string BarcodeRoll = "BarcodeRoll";
    }
}
