
namespace Ocore.desktop.Models.Printing
{
    /// <summary>
    /// إعدادات نوع واحد من أنواع الطابعات.
    /// يتم حفظ ثلاثة سجلات مستقلة في قاعدة بيانات الجهاز.
    /// </summary>
    public class PrinterConfiguration
    {
        public int Id { get; set; }

        public string PrinterType { get; set; }

        public string PrinterName { get; set; }

        // إعدادات الطباعة العامة
        public string PrintMode { get; set; }

        public string PaperSize { get; set; }

        public double PaperWidth { get; set; }

        public double PaperHeight { get; set; }

        public int PrintWidthDots { get; set; }

        public double MarginLeft { get; set; }

        public double MarginRight { get; set; }

        public double MarginTop { get; set; }

        public double MarginBottom { get; set; }

        public string Orientation { get; set; }

        public double FontScale { get; set; }

        // إعدادات الكاشير
        public bool AutoCut { get; set; }

        public bool OpenCashDrawer { get; set; }

        // إعدادات ملصقات الباركود Roll
        public double BarcodeLabelWidth { get; set; }

        public double BarcodeLabelHeight { get; set; }

        public double BarcodeGap { get; set; }

        public double BarcodeOffsetX { get; set; }

        public double BarcodeOffsetY { get; set; }

        public bool BarcodeShowPrice { get; set; }

        public bool BarcodeShowProductCode { get; set; }

        public string UpdatedAt { get; set; }

        public PrinterConfiguration()
        {
            PrinterType = string.Empty;
            PrinterName = string.Empty;

            PrintMode = "WindowsDriver";
            PaperSize = "A4";

            PaperWidth = 0;
            PaperHeight = 0;
            PrintWidthDots = 0;

            MarginLeft = 0;
            MarginRight = 0;
            MarginTop = 0;
            MarginBottom = 0;

            Orientation = "Portrait";
            FontScale = 1.0;

            AutoCut = false;
            OpenCashDrawer = false;

            BarcodeLabelWidth = 35;
            BarcodeLabelHeight = 12;
            BarcodeGap = 3;
            BarcodeOffsetX = 2;
            BarcodeOffsetY = 2;

            BarcodeShowPrice = true;
            BarcodeShowProductCode = true;

            UpdatedAt = string.Empty;
        }
    }
}
