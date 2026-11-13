
using Ocore.desktop.Models.Printing;

namespace Ocore.desktop.Services.Printing
{
    public static class PrinterConfigurationDefaults
    {
        public static PrinterConfiguration Create(string printerType)
        {
            var configuration = new PrinterConfiguration
            {
                PrinterType = printerType,
                PrinterName = string.Empty,
                PrintMode = "WindowsDriver",
                FontScale = 1.0,
                Orientation = "Portrait"
            };

            switch (printerType)
            {
                case PrinterType.Cashier:
                    configuration.PaperSize = "Thermal";
                    configuration.PaperWidth = 0;
                    configuration.PrintWidthDots = 0;
                    configuration.AutoCut = false;
                    configuration.OpenCashDrawer = false;
                    break;

                case PrinterType.Normal:
                    configuration.PaperSize = "A4";
                    configuration.PaperWidth = 0;
                    configuration.PaperHeight = 0;
                    break;

                case PrinterType.BarcodeRoll:
                    configuration.PrintMode = "WindowsDriver";
                    configuration.PaperSize = "Roll";
                    configuration.BarcodeLabelWidth = 35;
                    configuration.BarcodeLabelHeight = 12;
                    configuration.BarcodeGap = 3;
                    configuration.BarcodeOffsetX = 2;
                    configuration.BarcodeOffsetY = 2;
                    configuration.BarcodeShowPrice = true;
                    configuration.BarcodeShowProductCode = true;
                    break;

                default:
                    throw new System.ArgumentException(
                        "نوع الطابعة غير معروف.",
                        nameof(printerType));
            }

            return configuration;
        }
    }
}
