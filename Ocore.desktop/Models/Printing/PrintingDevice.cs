
namespace Ocore.desktop.Models.Printing
{
    /// <summary>
    /// تعريف طابعة Windows المثبتة.
    /// لا يحتوي على إعدادات وظيفة الطباعة.
    /// </summary>
    public class PrintingDevice
    {
        public string PrinterName { get; set; }

        public bool IsDefaultPrinter { get; set; }

        public bool IsInstalled { get; set; }

        public PrintingDevice()
        {
            PrinterName = string.Empty;
            IsDefaultPrinter = false;
            IsInstalled = false;
        }

        public override string ToString()
        {
            return PrinterName;
        }
    }
}
