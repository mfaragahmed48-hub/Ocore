
namespace Ocore.desktop.Models.Printing
{
    /// <summary>
    /// ملف إعدادات مستقل لوظيفة طباعة واحدة.
    /// يحتفظ بإعدادات الوظيفة وإعدادات الباركود الخاصة بها.
    /// </summary>
    public class PrintingProfile
    {
        public int Id { get; set; }

        public string FunctionKey { get; set; }

        // إعدادات الطابعة العامة
        public string PrinterName { get; set; }

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

        public bool AutoCut { get; set; }

        public bool OpenCashDrawer { get; set; }

        // إعدادات الباركود العامة
        public string BarcodePrinter { get; set; }

        public string BarcodePrintType { get; set; }

        public bool BarcodeShowPrice { get; set; }

        public bool BarcodeShowProductCode { get; set; }

        // إعدادات الباركود A4
        public string BarcodeA4Printer { get; set; }

        public string BarcodeA4LabelWidth { get; set; }

        public string BarcodeA4LabelHeight { get; set; }

        public string BarcodeA4Margin { get; set; }

        public string BarcodeA4Gap { get; set; }

        // إعدادات الباركود Roll
        public string BarcodeRollPrinter { get; set; }

        public string BarcodeRollLabelWidth { get; set; }

        public string BarcodeRollLabelHeight { get; set; }

        public string BarcodeRollGap { get; set; }

        public string BarcodeRollOffsetX { get; set; }

        public string BarcodeRollOffsetY { get; set; }

        public string BarcodeRollOrientation { get; set; }

        public PrintingProfile()
        {
            FunctionKey = string.Empty;

            PrinterName = string.Empty;
            PrintMode = string.Empty;
            PaperSize = string.Empty;

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

            BarcodePrinter = string.Empty;
            BarcodePrintType = "A4";

            BarcodeShowPrice = true;
            BarcodeShowProductCode = true;

            BarcodeA4Printer = string.Empty;
            BarcodeA4LabelWidth = "50";
            BarcodeA4LabelHeight = "30";
            BarcodeA4Margin = "5";
            BarcodeA4Gap = "2";

            BarcodeRollPrinter = string.Empty;
            BarcodeRollLabelWidth = "50";
            BarcodeRollLabelHeight = "30";
            BarcodeRollGap = "2";
            BarcodeRollOffsetX = "0";
            BarcodeRollOffsetY = "0";
            BarcodeRollOrientation = "Portrait";
        }
    }
}
