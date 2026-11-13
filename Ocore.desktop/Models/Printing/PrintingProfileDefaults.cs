
using Ocore.desktop.Models.Printing;

namespace Ocore.desktop.Models.Printing
{
    /// <summary>
    /// القيم الافتراضية لوظائف الطباعة.
    /// لا تستبدل الإعدادات المحفوظة للمستخدم.
    /// </summary>
    public static class PrintingProfileDefaults
    {
        public static PrintingProfile Create(string functionKey)
        {
            var profile = new PrintingProfile
            {
                FunctionKey = functionKey
            };

            switch (functionKey)
            {
                case PrintingFunction.CashierSales:
                case PrintingFunction.CashierReturns:

                    profile.PrintMode = "WindowsDriver";
                    profile.PaperSize = "Thermal";
                    profile.PaperWidth = 0;
                    profile.PrintWidthDots = 0;
                    profile.FontScale = 1.0;
                    profile.AutoCut = false;
                    profile.OpenCashDrawer = false;
                    break;

                case PrintingFunction.Barcode:

                    profile.PrintMode = "WindowsDriver";
                    profile.PaperSize = "A4";
                    profile.BarcodePrintType = "A4";

                    profile.BarcodeShowPrice = true;
                    profile.BarcodeShowProductCode = true;

                    profile.BarcodeA4LabelWidth = "50";
                    profile.BarcodeA4LabelHeight = "30";
                    profile.BarcodeA4Margin = "5";
                    profile.BarcodeA4Gap = "2";

                    profile.BarcodeRollLabelWidth = "50";
                    profile.BarcodeRollLabelHeight = "30";
                    profile.BarcodeRollGap = "2";
                    profile.BarcodeRollOffsetX = "0";
                    profile.BarcodeRollOffsetY = "0";
                    profile.BarcodeRollOrientation = "Portrait";
                    break;

                case PrintingFunction.PurchaseInvoices:
                case PrintingFunction.Reports:
                case PrintingFunction.Documents:

                    profile.PrintMode = "WindowsDriver";
                    profile.PaperSize = "A4";
                    profile.Orientation = "Portrait";
                    profile.FontScale = 1.0;
                    break;
            }

            return profile;
        }
    }
}
