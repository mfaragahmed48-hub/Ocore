
namespace Ocore.desktop.Models
{
    public class PharmacySettings
    {
        public int Id { get; set; }

        public string PharmacyName { get; set; }

        public string Address { get; set; }

        public string Phone { get; set; }

        public string TaxNumber { get; set; }

        public string ManagerName { get; set; }

        public string LogoPath { get; set; }

        public string UpdatedAt { get; set; }

        // Thermal Printer Settings
        public string ThermalPrinterName { get; set; }

        public string ThermalPrintMode { get; set; }

        public int ThermalPaperWidth { get; set; }

        // صفر = تحديد العرض تلقائيًا حسب مقاس الرول
        public int ThermalPrintWidthDots { get; set; }

        public bool ThermalAutoCut { get; set; }

        public bool ThermalOpenCashDrawer { get; set; }

        public double ThermalFontScale { get; set; }

        public PharmacySettings()
        {
            PharmacyName = string.Empty;
            Address = string.Empty;
            Phone = string.Empty;
            TaxNumber = string.Empty;
            ManagerName = string.Empty;
            LogoPath = string.Empty;
            UpdatedAt = string.Empty;

            ThermalPrinterName = string.Empty;
            ThermalPrintMode = "WindowsDriver";
            ThermalPaperWidth = 80;
            ThermalPrintWidthDots = 0;
            ThermalAutoCut = false;
            ThermalOpenCashDrawer = false;
            ThermalFontScale = 1.0;
        }
    }
}
