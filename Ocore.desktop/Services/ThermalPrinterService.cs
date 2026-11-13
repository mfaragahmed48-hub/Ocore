
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Ocore.desktop.Services
{
    public class ThermalPrinterService
    {
        private const int ERROR_INVALID_HANDLE = 6;
        private const int DefaultRasterDpi = 203;

        #region Windows Spooler API

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class DOC_INFO_1
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDocName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string pOutputFile;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string pDatatype;
        }

        [DllImport(
            "winspool.drv",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(
            string pPrinterName,
            out IntPtr phPrinter,
            IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(
            IntPtr hPrinter);

        [DllImport(
            "winspool.drv",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        private static extern int StartDocPrinter(
            IntPtr hPrinter,
            int Level,
            [In] DOC_INFO_1 pDocInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(
            IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool AbortPrinter(
            IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(
            IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(
            IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(
            IntPtr hPrinter,
            IntPtr pBytes,
            int dwCount,
            out int dwWritten);

        #endregion

        #region Printer Discovery

        public List<string> GetInstalledPrinters()
        {
            List<string> printers = new List<string>();

            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                if (!string.IsNullOrWhiteSpace(printer))
                {
                    printers.Add(printer);
                }
            }

            return printers;
        }

        public bool IsPrinterInstalled(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                return false;

            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                if (string.Equals(
                    printer,
                    printerName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Windows Driver Printing

        /// <summary>
        /// إرسال صورة الإيصال إلى تعريف الطابعة المثبت في Windows.
        /// لا يتم إرسال أوامر ESC/POS RAW في هذا المسار.
        ///
        /// يجب أن تكون BitmapSource مجمدة Freeze
        /// حتى يمكن استخدامها بأمان داخل Thread منفصل.
        /// </summary>
        public Task PrintImageWithWindowsDriverAsync(
            string printerName,
            BitmapSource receiptImage,
            double paperWidthMm,
            CancellationToken cancellationToken =
                default(CancellationToken))
        {
            ValidateWindowsPrintArguments(
                printerName,
                receiptImage,
                paperWidthMm);

            if (!receiptImage.IsFrozen)
            {
                throw new InvalidOperationException(
                    "يجب تجميد صورة الإيصال Freeze قبل إرسالها للطباعة.");
            }

            BitmapSource imageCopy = receiptImage;

            return Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    PrintImageWithWindowsDriver(
                        printerName,
                        imageCopy,
                        paperWidthMm);
                },
                cancellationToken);
        }

        /// <summary>
        /// الطباعة باستخدام Windows PrintDocument.
        /// مع جمع معلومات تشخيصية عند عدم تنفيذ PrintPage.
        /// </summary>
        private void PrintImageWithWindowsDriver(
            string printerName,
            BitmapSource receiptImage,
            double paperWidthMm)
        {
            if (!IsPrinterInstalled(printerName))
            {
                throw new InvalidOperationException(
                    "الطابعة المحددة غير مثبتة في Windows:\n" +
                    printerName);
            }

            byte[] imageBytes;

            // تحويل صورة WPF إلى PNG قبل الطباعة.
            using (MemoryStream imageStream = new MemoryStream())
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();

                encoder.Frames.Add(
                    BitmapFrame.Create(receiptImage));

                encoder.Save(imageStream);

                imageBytes = imageStream.ToArray();
            }

            if (imageBytes.Length == 0)
            {
                throw new InvalidOperationException(
                    "تعذر تجهيز صورة الإيصال للطباعة.");
            }

            double dpiX = receiptImage.DpiX;
            double dpiY = receiptImage.DpiY;

            if (double.IsNaN(dpiX) ||
                double.IsInfinity(dpiX) ||
                dpiX <= 0)
            {
                dpiX = DefaultRasterDpi;
            }

            if (double.IsNaN(dpiY) ||
                double.IsInfinity(dpiY) ||
                dpiY <= 0)
            {
                dpiY = DefaultRasterDpi;
            }

            // أبعاد الورق الفعلية بوحدة 1/100 بوصة.
            int paperWidthHundredths =
                Convert.ToInt32(
                    Math.Round(paperWidthMm / 25.4 * 100.0));

            // حساب العرض الفيزيائي للصورة من عدد النقاط وDPI.
            int imageWidthHundredths =
                Convert.ToInt32(
                    Math.Round(
                        receiptImage.PixelWidth * 100.0 / dpiX));

            int imageHeightHundredths =
                Convert.ToInt32(
                    Math.Round(
                        receiptImage.PixelHeight * 100.0 / dpiY));

            if (paperWidthHundredths <= 0 ||
                imageWidthHundredths <= 0 ||
                imageHeightHundredths <= 0)
            {
                throw new InvalidOperationException(
                    "أبعاد ورقة الطباعة أو صورة الإيصال غير صالحة.");
            }

            // لا نسمح بتمديد الصورة أو تصغيرها تلقائيًا.
            if (imageWidthHundredths >
                paperWidthHundredths + 1)
            {
                throw new InvalidOperationException(
                    "عرض صورة الإيصال أكبر من عرض الرول المحدد. " +
                    "راجع إعداد ThermalPrintWidthDots أو مقاس الورق.");
            }

            using (MemoryStream stream =
                new MemoryStream(imageBytes))
            using (System.Drawing.Image drawingImage =
                System.Drawing.Image.FromStream(stream))
            using (PrintDocument printDocument =
                new PrintDocument())
            {
                PrinterSettings printerSettings =
                    new PrinterSettings
                    {
                        PrinterName = printerName
                    };

                if (!printerSettings.IsValid)
                {
                    throw new InvalidOperationException(
                        "تعذر استخدام تعريف الطابعة المحدد في Windows.");
                }

                printDocument.PrinterSettings = printerSettings;

                printDocument.DocumentName =
                    "O CORE - Thermal Receipt";

                printDocument.PrintController =
                    new StandardPrintController();

                printDocument.DefaultPageSettings.Landscape = false;

                printDocument.DefaultPageSettings.Margins =
                    new Margins(0, 0, 0, 0);

                PaperSize customPaperSize =
                    new PaperSize(
                        "OCORE Thermal Receipt",
                        paperWidthHundredths,
                        imageHeightHundredths);

                printDocument.DefaultPageSettings.PaperSize =
                    customPaperSize;

                // متغيرات مراقبة مراحل الطباعة.
                bool beginPrintRaised = false;
                bool queryPageSettingsRaised = false;
                bool pagePrinted = false;
                bool endPrintRaised = false;
                bool endPrintCancelled = false;

                printDocument.BeginPrint += (sender, e) =>
                {
                    beginPrintRaised = true;
                };

                printDocument.QueryPageSettings += (sender, e) =>
                {
                    queryPageSettingsRaised = true;
                };

                printDocument.PrintPage += (sender, e) =>
                {
                    if (pagePrinted)
                    {
                        e.HasMorePages = false;
                        return;
                    }

                    // GraphicsUnit.Display = 1/100 بوصة.
                    e.Graphics.PageUnit = GraphicsUnit.Display;

                    // الرسم بالأبعاد الفيزيائية الحقيقية للصورة.
                    e.Graphics.DrawImage(
                        drawingImage,
                        new Rectangle(
                            0,
                            0,
                            imageWidthHundredths,
                            imageHeightHundredths));

                    pagePrinted = true;
                    e.HasMorePages = false;
                };

                printDocument.EndPrint += (sender, e) =>
                {
                    endPrintRaised = true;
                    endPrintCancelled = e.Cancel;
                };

                // تنفيذ الطباعة.
                printDocument.Print();

                if (!pagePrinted)
                {
                    List<string> supportedPaperSizes =
                        new List<string>();

                    foreach (PaperSize paperSize in
                        printerSettings.PaperSizes)
                    {
                        supportedPaperSizes.Add(
                            paperSize.PaperName +
                            " | Width=" + paperSize.Width +
                            " | Height=" + paperSize.Height);
                    }

                    string paperSizesText =
                        supportedPaperSizes.Count == 0
                            ? "لم يُرجع التعريف أي مقاسات ورق."
                            : string.Join(
                                Environment.NewLine,
                                supportedPaperSizes);

                    throw new InvalidOperationException(
                        "لم يتم تنفيذ صفحة الإيصال بواسطة تعريف Windows."
                        + Environment.NewLine + Environment.NewLine +

                        "بيانات التشخيص:"
                        + Environment.NewLine +

                        "الطابعة: " + printerName
                        + Environment.NewLine +

                        "صلاحية التعريف: " + printerSettings.IsValid
                        + Environment.NewLine +

                        "عرض الورق المطلوب: " +
                        paperWidthHundredths
                        + Environment.NewLine +

                        "ارتفاع الورق المطلوب: " +
                        imageHeightHundredths
                        + Environment.NewLine +

                        "BeginPrint: " + beginPrintRaised
                        + Environment.NewLine +

                        "QueryPageSettings: " + queryPageSettingsRaised
                        + Environment.NewLine +

                        "PrintPage: " + pagePrinted
                        + Environment.NewLine +

                        "EndPrint: " + endPrintRaised
                        + Environment.NewLine +

                        "EndPrint Cancelled: " + endPrintCancelled
                        + Environment.NewLine + Environment.NewLine +

                        "مقاسات الورق التي يدعمها التعريف:"
                        + Environment.NewLine +

                        paperSizesText);
                }
            }
        }

        private static void ValidateWindowsPrintArguments(
            string printerName,
            BitmapSource receiptImage,
            double paperWidthMm)
        {
            if (string.IsNullOrWhiteSpace(printerName))
            {
                throw new ArgumentException(
                    "يجب تحديد اسم الطابعة.",
                    nameof(printerName));
            }

            if (receiptImage == null)
            {
                throw new ArgumentNullException(
                    nameof(receiptImage));
            }

            if (receiptImage.PixelWidth <= 0 ||
                receiptImage.PixelHeight <= 0)
            {
                throw new ArgumentException(
                    "صورة الإيصال غير صالحة.",
                    nameof(receiptImage));
            }

            if (double.IsNaN(paperWidthMm) ||
                double.IsInfinity(paperWidthMm) ||
                (paperWidthMm != 58 && paperWidthMm != 80))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(paperWidthMm),
                    "عرض الورق يجب أن يكون 58 أو 80 مم.");
            }
        }

        #endregion

        #region RAW ESC/POS Printing

        /// <summary>
        /// إرسال بيانات RAW بشكل غير متزامن بالنسبة لواجهة المستخدم.
        ///
        /// يمكن إلغاء المهمة قبل بدء الإرسال.
        /// بعد بدء عملية Windows Spooler لا يمكن ضمان إيقافها
        /// باستخدام CancellationToken.
        /// </summary>
        public Task SendRawAsync(
            string printerName,
            byte[] data,
            CancellationToken cancellationToken =
                default(CancellationToken))
        {
            ValidatePrintArguments(printerName, data);

            byte[] dataCopy = (byte[])data.Clone();

            return Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    SendRaw(printerName, dataCopy);
                },
                cancellationToken);
        }

        /// <summary>
        /// إرسال بيانات RAW مباشرة إلى Windows Spooler.
        /// يجب استدعاء هذه الدالة من Thread غير واجهة المستخدم.
        /// </summary>
        public void SendRaw(
            string printerName,
            byte[] data)
        {
            ValidatePrintArguments(printerName, data);

            IntPtr printerHandle = IntPtr.Zero;

            GCHandle pinnedData = default(GCHandle);

            bool documentStarted = false;

            try
            {
                bool opened = OpenPrinter(
                    printerName,
                    out printerHandle,
                    IntPtr.Zero);

                if (!opened)
                {
                    ThrowLastWin32Error(
                        "تعذر فتح الطابعة.");
                }

                DOC_INFO_1 documentInfo = new DOC_INFO_1
                {
                    pDocName = "O CORE - Thermal Receipt",
                    pOutputFile = null,
                    pDatatype = "RAW"
                };

                int jobId = StartDocPrinter(
                    printerHandle,
                    1,
                    documentInfo);

                if (jobId == 0)
                {
                    ThrowLastWin32Error(
                        "تعذر بدء مهمة الطباعة.");
                }

                documentStarted = true;

                if (!StartPagePrinter(printerHandle))
                {
                    ThrowLastWin32Error(
                        "تعذر بدء صفحة الطباعة.");
                }

                pinnedData = GCHandle.Alloc(
                    data,
                    GCHandleType.Pinned);

                IntPtr dataPointer =
                    pinnedData.AddrOfPinnedObject();

                int totalWritten = 0;

                while (totalWritten < data.Length)
                {
                    int remaining =
                        data.Length - totalWritten;

                    int written;

                    IntPtr currentBuffer = IntPtr.Add(
                        dataPointer,
                        totalWritten);

                    bool success = WritePrinter(
                        printerHandle,
                        currentBuffer,
                        remaining,
                        out written);

                    if (!success)
                    {
                        ThrowLastWin32Error(
                            "فشل إرسال بيانات الطباعة.");
                    }

                    if (written <= 0)
                    {
                        throw new InvalidOperationException(
                            "لم يتم إرسال أي بيانات إلى الطابعة.");
                    }

                    if (written > remaining)
                    {
                        throw new InvalidOperationException(
                            "أبلغ Windows Spooler عن حجم إرسال غير صالح.");
                    }

                    totalWritten += written;
                }

                if (!EndPagePrinter(printerHandle))
                {
                    ThrowLastWin32Error(
                        "تعذر إنهاء صفحة الطباعة.");
                }

                if (!EndDocPrinter(printerHandle))
                {
                    ThrowLastWin32Error(
                        "تعذر إنهاء مهمة الطباعة.");
                }

                documentStarted = false;
            }
            finally
            {
                // عند حدوث أي فشل قبل اكتمال المهمة:
                // نلغي المهمة مباشرةً ولا نحاول إنهاء صفحة جزئية.
                if (documentStarted &&
                    printerHandle != IntPtr.Zero)
                {
                    try
                    {
                        AbortPrinter(printerHandle);
                    }
                    catch
                    {
                    }
                }

                if (pinnedData.IsAllocated)
                {
                    pinnedData.Free();
                }

                if (printerHandle != IntPtr.Zero)
                {
                    try
                    {
                        ClosePrinter(printerHandle);
                    }
                    catch
                    {
                    }
                }
            }
        }

        #endregion

        #region ESC/POS Commands

        public byte[] CreateInitializeCommand()
        {
            return new byte[]
            {
                0x1B,
                0x40
            };
        }

        public byte[] CreateFeedCommand(int lines)
        {
            if (lines < 0)
                lines = 0;

            if (lines > 255)
                lines = 255;

            return new byte[]
            {
                0x1B,
                0x64,
                (byte)lines
            };
        }

        public byte[] CreateCutCommand()
        {
            return new byte[]
            {
                0x1D,
                0x56,
                0x00
            };
        }

        public byte[] CreateOpenCashDrawerCommand(
            byte pin = 0,
            byte onTime = 25,
            byte offTime = 250)
        {
            if (pin > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pin));
            }

            return new byte[]
            {
                0x1B,
                0x70,
                pin,
                onTime,
                offTime
            };
        }

        #endregion

        #region Validation

        private static void ValidatePrintArguments(
            string printerName,
            byte[] data)
        {
            if (string.IsNullOrWhiteSpace(printerName))
            {
                throw new ArgumentException(
                    "يجب تحديد اسم الطابعة.",
                    nameof(printerName));
            }

            if (data == null || data.Length == 0)
            {
                throw new ArgumentException(
                    "لا توجد بيانات لإرسالها إلى الطابعة.",
                    nameof(data));
            }
        }

        #endregion

        #region Helpers

        private static void ThrowLastWin32Error(
            string message)
        {
            int errorCode = Marshal.GetLastWin32Error();

            if (errorCode == ERROR_INVALID_HANDLE)
            {
                throw new Win32Exception(
                    errorCode,
                    message + " مقبض الطابعة غير صالح.");
            }

            throw new Win32Exception(
                errorCode,
                message);
        }

        #endregion
    }
}
