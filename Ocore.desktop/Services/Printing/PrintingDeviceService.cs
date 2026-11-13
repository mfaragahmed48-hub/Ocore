
using Ocore.desktop.Models.Printing;
using System.Collections.Generic;
using System.Drawing.Printing;

namespace Ocore.desktop.Services.Printing
{
    /// <summary>
    /// قراءة الطابعات المثبتة في Windows.
    /// لا يغيّر إعدادات الطابعات ولا ينفذ أي عملية طباعة.
    /// </summary>
    public class PrintingDeviceService
    {
        public List<PrintingDevice> GetInstalledPrinters()
        {
            var printers = new List<PrintingDevice>();

            string defaultPrinterName = new PrinterSettings().PrinterName;

            foreach (string printerName in PrinterSettings.InstalledPrinters)
            {
                if (string.IsNullOrWhiteSpace(printerName))
                    continue;

                printers.Add(new PrintingDevice
                {
                    PrinterName = printerName,
                    IsDefaultPrinter =
                        printerName == defaultPrinterName,
                    IsInstalled = true
                });
            }

            return printers;
        }

        public bool IsPrinterInstalled(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                return false;

            foreach (string installedPrinter
                in PrinterSettings.InstalledPrinters)
            {
                if (string.Equals(
                    installedPrinter,
                    printerName,
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
