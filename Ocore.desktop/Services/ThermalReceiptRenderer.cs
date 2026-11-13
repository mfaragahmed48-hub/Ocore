
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ocore.desktop.Services
{
    public class ThermalReceiptRenderer
    {
        private const int DefaultDpi = 203;
        private const byte DefaultThreshold = 180;

        private const int MaximumRasterDimension = 65535;
        private const int RasterChunkHeight = 1024;
        private const long MaximumPixelCount = 50000000;

        public byte[] RenderToEscPos(
            FlowDocument document,
            double paperWidthMm,
            int dpi = DefaultDpi,
            byte threshold = DefaultThreshold)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            BitmapSource bitmap = RenderDocument(
                document,
                paperWidthMm,
                dpi);

            return RenderToEscPos(
                bitmap,
                paperWidthMm,
                dpi,
                threshold);
        }

        public byte[] RenderToEscPos(
            BitmapSource bitmap,
            double paperWidthMm,
            int dpi = DefaultDpi,
            byte threshold = DefaultThreshold)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            ValidatePaperWidth(paperWidthMm);
            ValidateDpi(dpi);

            if (!bitmap.IsFrozen)
            {
                throw new InvalidOperationException(
                    "يجب تجميد صورة الإيصال Freeze قبل معالجتها في Thread منفصل.");
            }

            byte[] rasterData = ConvertToMonochromeRaster(
                bitmap,
                threshold);

            return BuildRasterCommand(
                rasterData,
                bitmap.PixelWidth,
                bitmap.PixelHeight);
        }

        public Task<byte[]> RenderToEscPosAsync(
            BitmapSource bitmap,
            double paperWidthMm,
            int dpi = DefaultDpi,
            byte threshold = DefaultThreshold)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            ValidatePaperWidth(paperWidthMm);
            ValidateDpi(dpi);

            if (!bitmap.IsFrozen)
            {
                throw new InvalidOperationException(
                    "يجب تجميد صورة الإيصال Freeze قبل معالجتها في Thread منفصل.");
            }

            return Task.Run(
                () => RenderToEscPos(
                    bitmap,
                    paperWidthMm,
                    dpi,
                    threshold));
        }

        public BitmapSource RenderDocument(
            FlowDocument document,
            double paperWidthMm,
            int dpi = DefaultDpi)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            ValidatePaperWidth(paperWidthMm);
            ValidateDpi(dpi);

            double widthDip = document.PageWidth;
            double heightDip = document.PageHeight;

            if (double.IsNaN(widthDip) ||
                double.IsInfinity(widthDip) ||
                widthDip <= 0)
            {
                throw new InvalidOperationException(
                    "عرض مستند الفاتورة غير صالح.");
            }

            if (double.IsNaN(heightDip) ||
                double.IsInfinity(heightDip) ||
                heightDip <= 0)
            {
                throw new InvalidOperationException(
                    "ارتفاع مستند الفاتورة غير صالح.");
            }

            double calculatedPixelWidth =
                Math.Ceiling(widthDip * dpi / 96.0);

            double calculatedPixelHeight =
                Math.Ceiling(heightDip * dpi / 96.0);

            if (calculatedPixelWidth > int.MaxValue ||
                calculatedPixelHeight > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "أبعاد صورة الفاتورة تتجاوز الحدود الرقمية المدعومة.");
            }

            int pixelWidth = (int)calculatedPixelWidth;
            int pixelHeight = (int)calculatedPixelHeight;

            ValidateDimensions(
                pixelWidth,
                pixelHeight);

            IDocumentPaginatorSource paginatorSource =
                document;

            DocumentPaginator paginator =
                paginatorSource.DocumentPaginator;

            paginator.PageSize = new Size(
                widthDip,
                heightDip);

            paginator.ComputePageCount();

            int pageCount = paginator.PageCount;

            if (pageCount != 1)
            {
                throw new InvalidOperationException(
                    "الإيصال لم يتم تجهيزه في صفحة واحدة. " +
                    "عدد الصفحات الحالي: " + pageCount +
                    ". راجع ارتفاع المستند قبل الطباعة.");
            }

            DocumentPage page =
                paginator.GetPage(0);

            if (page == null ||
                page == DocumentPage.Missing ||
                page.Visual == null)
            {
                throw new InvalidOperationException(
                    "تعذر تجهيز صورة الإيصال للطباعة.");
            }

            RenderTargetBitmap bitmap =
                new RenderTargetBitmap(
                    pixelWidth,
                    pixelHeight,
                    dpi,
                    dpi,
                    PixelFormats.Pbgra32);

            bitmap.Render(page.Visual);
            bitmap.Freeze();

            return bitmap;
        }

        /// <summary>
        /// قص المساحة البيضاء الزائدة من أسفل صورة الإيصال.
        /// هذه الدالة لا تعدّل الصورة الأصلية.
        /// </summary>
        public BitmapSource CropBottomWhitespace(
            BitmapSource bitmap,
            byte whiteThreshold = 250,
            int bottomPaddingPixels = 16)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            if (!bitmap.IsFrozen)
            {
                throw new InvalidOperationException(
                    "يجب تجميد صورة الإيصال Freeze قبل قص المساحة البيضاء.");
            }

            if (bitmap.Format != PixelFormats.Pbgra32)
            {
                throw new InvalidOperationException(
                    "صيغة صورة الإيصال غير مدعومة لقص المساحة البيضاء.");
            }

            if (bottomPaddingPixels < 0)
                bottomPaddingPixels = 0;

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            ValidateDimensions(width, height);

            int stride = checked(width * 4);

            byte[] row = new byte[stride];

            int lastContentRow = -1;

            for (int y = height - 1; y >= 0; y--)
            {
                bitmap.CopyPixels(
                    new Int32Rect(0, y, width, 1),
                    row,
                    stride,
                    0);

                bool containsContent = false;

                for (int x = 0; x < width; x++)
                {
                    int index = x * 4;

                    byte blue = row[index];
                    byte green = row[index + 1];
                    byte red = row[index + 2];
                    byte alpha = row[index + 3];

                    int adjustedRed =
                        CompositePremultipliedOnWhite(red, alpha);

                    int adjustedGreen =
                        CompositePremultipliedOnWhite(green, alpha);

                    int adjustedBlue =
                        CompositePremultipliedOnWhite(blue, alpha);

                    int luminance =
                        (299 * adjustedRed +
                         587 * adjustedGreen +
                         114 * adjustedBlue) / 1000;

                    if (luminance < whiteThreshold)
                    {
                        containsContent = true;
                        break;
                    }
                }

                if (containsContent)
                {
                    lastContentRow = y;
                    break;
                }
            }

            if (lastContentRow < 0)
                return bitmap;

            int croppedHeight = Math.Min(
                height,
                lastContentRow + 1 + bottomPaddingPixels);

            if (croppedHeight >= height)
                return bitmap;

            CroppedBitmap croppedBitmap =
                new CroppedBitmap(
                    bitmap,
                    new Int32Rect(
                        0,
                        0,
                        width,
                        croppedHeight));

            croppedBitmap.Freeze();

            return croppedBitmap;
        }

        private byte[] ConvertToMonochromeRaster(
            BitmapSource bitmap,
            byte threshold)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            ValidateDimensions(width, height);

            if (bitmap.Format != PixelFormats.Pbgra32)
            {
                throw new InvalidOperationException(
                    "صيغة صورة الإيصال غير متوافقة مع المعالجة.");
            }

            int bytesPerPixel = 4;

            int sourceStride = checked(
                width * bytesPerPixel);

            byte[] sourceRow = new byte[sourceStride];

            int bytesPerRow = checked(
                (width + 7) / 8);

            byte[] raster = new byte[
                checked(bytesPerRow * height)];

            for (int y = 0; y < height; y++)
            {
                bitmap.CopyPixels(
                    new Int32Rect(0, y, width, 1),
                    sourceRow,
                    sourceStride,
                    0);

                int targetRow = y * bytesPerRow;

                for (int x = 0; x < width; x++)
                {
                    int sourceIndex =
                        x * bytesPerPixel;

                    byte blue = sourceRow[sourceIndex];
                    byte green = sourceRow[sourceIndex + 1];
                    byte red = sourceRow[sourceIndex + 2];
                    byte alpha = sourceRow[sourceIndex + 3];

                    int adjustedRed =
                        CompositePremultipliedOnWhite(red, alpha);

                    int adjustedGreen =
                        CompositePremultipliedOnWhite(green, alpha);

                    int adjustedBlue =
                        CompositePremultipliedOnWhite(blue, alpha);

                    int luminance =
                        (299 * adjustedRed +
                         587 * adjustedGreen +
                         114 * adjustedBlue) / 1000;

                    if (luminance < threshold)
                    {
                        int targetIndex =
                            targetRow + (x / 8);

                        int bitPosition =
                            7 - (x % 8);

                        raster[targetIndex] |=
                            (byte)(1 << bitPosition);
                    }
                }
            }

            return raster;
        }

        private static int CompositePremultipliedOnWhite(
            byte premultipliedColor,
            byte alpha)
        {
            int result =
                premultipliedColor + 255 - alpha;

            if (result < 0)
                return 0;

            if (result > 255)
                return 255;

            return result;
        }

        private byte[] BuildRasterCommand(
            byte[] rasterData,
            int width,
            int height)
        {
            if (rasterData == null)
                throw new ArgumentNullException(nameof(rasterData));

            ValidateDimensions(width, height);

            int bytesPerRow = checked(
                (width + 7) / 8);

            if (bytesPerRow > MaximumRasterDimension)
            {
                throw new InvalidOperationException(
                    "عرض الصورة أكبر من الحدود المدعومة لأمر ESC/POS.");
            }

            int expectedLength = checked(
                bytesPerRow * height);

            if (rasterData.Length != expectedLength)
            {
                throw new InvalidOperationException(
                    "حجم بيانات الصورة غير متوافق مع أبعادها.");
            }

            using (MemoryStream output = new MemoryStream())
            {
                output.WriteByte(0x1B);
                output.WriteByte(0x40);

                int currentY = 0;

                while (currentY < height)
                {
                    int chunkHeight = Math.Min(
                        RasterChunkHeight,
                        height - currentY);

                    int chunkLength = checked(
                        bytesPerRow * chunkHeight);

                    int sourceOffset = checked(
                        currentY * bytesPerRow);

                    byte[] header = new byte[8];

                    header[0] = 0x1D;
                    header[1] = 0x76;
                    header[2] = 0x30;
                    header[3] = 0x00;

                    header[4] = (byte)(bytesPerRow & 0xFF);
                    header[5] = (byte)(
                        (bytesPerRow >> 8) & 0xFF);

                    header[6] = (byte)(chunkHeight & 0xFF);
                    header[7] = (byte)(
                        (chunkHeight >> 8) & 0xFF);

                    output.Write(
                        header,
                        0,
                        header.Length);

                    output.Write(
                        rasterData,
                        sourceOffset,
                        chunkLength);

                    currentY += chunkHeight;
                }

                return output.ToArray();
            }
        }

        private static void ValidatePaperWidth(
            double paperWidthMm)
        {
            if (double.IsNaN(paperWidthMm) ||
                double.IsInfinity(paperWidthMm) ||
                (paperWidthMm != 58 && paperWidthMm != 80))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(paperWidthMm),
                    "عرض الورق المدعوم هو 58 أو 80 مم.");
            }
        }

        private static void ValidateDpi(int dpi)
        {
            if (dpi < 72 || dpi > 600)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dpi),
                    "دقة الطباعة يجب أن تكون بين 72 و600 DPI.");
            }
        }

        private static void ValidateDimensions(
            int width,
            int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException(
                    "أبعاد صورة الفاتورة غير صالحة.");
            }

            if (width > MaximumRasterDimension ||
                height > MaximumRasterDimension)
            {
                throw new InvalidOperationException(
                    "أبعاد صورة الفاتورة تتجاوز الحد المسموح.");
            }

            long pixelCount =
                (long)width * height;

            if (pixelCount > MaximumPixelCount)
            {
                throw new InvalidOperationException(
                    "صورة الفاتورة كبيرة جدًا لمعالجتها بأمان.");
            }
        }
    }
}
