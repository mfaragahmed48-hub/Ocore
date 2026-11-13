
using Ocore.desktop.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Ocore.desktop.Services
{
    internal class BarcodePrintService
    {
        private const string L0 = "0001101";
        private const string L1 = "0011001";
        private const string L2 = "0010011";
        private const string L3 = "0111101";
        private const string L4 = "0100011";
        private const string L5 = "0110001";
        private const string L6 = "0101111";
        private const string L7 = "0111011";
        private const string L8 = "0110111";
        private const string L9 = "0001011";

        private const string G0 = "0100111";
        private const string G1 = "0110011";
        private const string G2 = "0011011";
        private const string G3 = "0100001";
        private const string G4 = "0011101";
        private const string G5 = "0111001";
        private const string G6 = "0000101";
        private const string G7 = "0010001";
        private const string G8 = "0001001";
        private const string G9 = "0010111";

        private const string R0 = "1110010";
        private const string R1 = "1100110";
        private const string R2 = "1101100";
        private const string R3 = "1000010";
        private const string R4 = "1011100";
        private const string R5 = "1001110";
        private const string R6 = "1010000";
        private const string R7 = "1000100";
        private const string R8 = "1001000";
        private const string R9 = "1110100";

        private static readonly string[] LeftOdd =
        {
            L0, L1, L2, L3, L4,
            L5, L6, L7, L8, L9
        };

        private static readonly string[] LeftEven =
        {
            G0, G1, G2, G3, G4,
            G5, G6, G7, G8, G9
        };

        private static readonly string[] Right =
        {
            R0, R1, R2, R3, R4,
            R5, R6, R7, R8, R9
        };

        private static readonly string[] Parity =
        {
            "LLLLLL",
            "LLGLGG",
            "LLGGLG",
            "LLGGGL",
            "LGLLGG",
            "LGGLLG",
            "LGGGLL",
            "LGLGLG",
            "LGLGGL",
            "LGGLGL"
        };

        #region Label Creation

        public DrawingVisual CreateLabelVisual(
            Product product,
            double widthMm,
            double heightMm,
            bool showPrice,
            bool showProductCode)
        {
            if (product == null)
                throw new ArgumentNullException(nameof(product));

            if (!IsPositiveFinite(widthMm) ||
                !IsPositiveFinite(heightMm))
            {
                throw new ArgumentException("مقاس الملصق غير صحيح.");
            }

            string barcode = product.Barcode;

            if (!IsValidEan13(barcode))
            {
                throw new ArgumentException(
                    "باركود الصنف غير صحيح أو ليس EAN-13.");
            }

            double width = MmToDip(widthMm);
            double height = MmToDip(heightMm);

            DrawingVisual visual = new DrawingVisual();

            using (DrawingContext dc = visual.RenderOpen())
            {
                Rect labelBounds = new Rect(0, 0, width, height);

                dc.DrawRectangle(
                    Brushes.White,
                    null,
                    labelBounds);

                dc.PushClip(new RectangleGeometry(labelBounds));

                double horizontalPadding =
                    Math.Max(3, width * 0.04);

                double contentWidth =
                    width - (horizontalPadding * 2);

                double verticalPadding =
                    Math.Max(2, height * 0.04);

                double availableHeight =
                    height - (verticalPadding * 2);

                // توزيع المساحة:
                // 25% لاسم الدواء.
                // 51% للباركود.
                // 18% للكود والسعر.
                // 3% مسافة بين الاسم والباركود.
                // 3% مسافة بين الباركود والكود والسعر.
                double nameHeight =
                    availableHeight * 0.25;

                double barcodeHeight =
                    availableHeight * 0.51;

                double footerHeight =
                    availableHeight * 0.18;

                double spacing =
                    availableHeight * 0.03;

                double currentY = verticalPadding;

                // 1. اسم الدواء أعلى الملصق.
                DrawProductName(
                    dc,
                    product.Name ?? "",
                    horizontalPadding,
                    currentY,
                    contentWidth,
                    nameHeight,
                    7);

                currentY += nameHeight + spacing;

                // 2. خطوط الباركود بدون الأرقام المكتوبة أسفلها.
                DrawEan13(
                    dc,
                    barcode,
                    horizontalPadding,
                    currentY,
                    contentWidth,
                    barcodeHeight);

                currentY += barcodeHeight + spacing;

                // 3. كود الصنف والسعر بجوار بعضهما أسفل الباركود.
                if (showProductCode && showPrice)
                {
                    double columnGap =
                        Math.Max(2, contentWidth * 0.025);

                    double columnWidth =
                        (contentWidth - columnGap) / 2;

                    string productCode =
                        "كود: " + (product.ProductCode ?? "");

                    string price =
                        "السعر: " +
                        product.SalePrice.ToString(
                            "0.00",
                            CultureInfo.CurrentCulture);

                    // كود الصنف في الناحية اليمنى.
                    DrawText(
                        dc,
                        productCode,
                        columnWidth,
                        horizontalPadding + columnWidth + columnGap,
                        currentY,
                        footerHeight,
                        Math.Min(10, footerHeight * 0.65),
                        TextAlignment.Center,
                        FontWeights.Normal);

                    // السعر في الناحية اليسرى.
                    DrawText(
                        dc,
                        price,
                        columnWidth,
                        horizontalPadding,
                        currentY,
                        footerHeight,
                        Math.Min(10, footerHeight * 0.65),
                        TextAlignment.Center,
                        FontWeights.Bold);
                }
                else if (showProductCode)
                {
                    DrawText(
                        dc,
                        "كود: " + (product.ProductCode ?? ""),
                        contentWidth,
                        horizontalPadding,
                        currentY,
                        footerHeight,
                        Math.Min(10, footerHeight * 0.65),
                        TextAlignment.Center,
                        FontWeights.Normal);
                }
                else if (showPrice)
                {
                    DrawText(
                        dc,
                        "السعر: " +
                        product.SalePrice.ToString(
                            "0.00",
                            CultureInfo.CurrentCulture),
                        contentWidth,
                        horizontalPadding,
                        currentY,
                        footerHeight,
                        Math.Min(10, footerHeight * 0.65),
                        TextAlignment.Center,
                        FontWeights.Bold);
                }

                dc.Pop();
            }

            return visual;
        }

        #endregion

        #region A4 Document

        public FixedDocument CreateA4Document(
            IEnumerable<Tuple<Product, int>> items,
            double labelWidthMm,
            double labelHeightMm,
            double marginMm,
            double gapMm,
            bool showPrice,
            bool showProductCode)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            if (!IsPositiveFinite(labelWidthMm) ||
                !IsPositiveFinite(labelHeightMm))
            {
                throw new ArgumentException(
                    "مقاس الملصق غير صحيح.");
            }

            if (!IsNonNegativeFinite(marginMm) ||
                !IsNonNegativeFinite(gapMm))
            {
                throw new ArgumentException(
                    "الهامش أو المسافة غير صحيحة.");
            }

            List<Tuple<Product, int>> validItems =
                items.Where(x =>
                    x != null &&
                    x.Item1 != null &&
                    x.Item2 > 0).ToList();

            if (validItems.Count == 0)
            {
                throw new InvalidOperationException(
                    "لا توجد ملصقات صالحة للطباعة.");
            }

            double pageWidth = MmToDip(210);
            double pageHeight = MmToDip(297);

            double margin = MmToDip(marginMm);
            double gap = MmToDip(gapMm);

            double labelWidth = MmToDip(labelWidthMm);
            double labelHeight = MmToDip(labelHeightMm);

            double availableWidth = pageWidth - (margin * 2);
            double availableHeight = pageHeight - (margin * 2);

            if (availableWidth <= 0 ||
                availableHeight <= 0 ||
                labelWidth > availableWidth ||
                labelHeight > availableHeight)
            {
                throw new InvalidOperationException(
                    "مقاس الملصق أو الهامش أكبر من المساحة المتاحة في ورقة A4.");
            }

            int columns = (int)Math.Floor(
                (availableWidth + gap) / (labelWidth + gap));

            int rows = (int)Math.Floor(
                (availableHeight + gap) / (labelHeight + gap));

            if (columns < 1 || rows < 1)
            {
                throw new InvalidOperationException(
                    "تعذر توزيع الملصقات على ورقة A4 بهذا المقاس.");
            }

            double usedWidth =
                (columns * labelWidth) + ((columns - 1) * gap);

            double usedHeight =
                (rows * labelHeight) + ((rows - 1) * gap);

            double startX =
                margin + ((availableWidth - usedWidth) / 2);

            double startY =
                margin + ((availableHeight - usedHeight) / 2);

            int labelsPerPage = checked(columns * rows);

            FixedDocument document = new FixedDocument();

            document.DocumentPaginator.PageSize =
                new Size(pageWidth, pageHeight);

            FixedPage currentPage = null;
            int currentSlot = 0;

            foreach (Tuple<Product, int> item in validItems)
            {
                Product product = item.Item1;
                int labelCount = item.Item2;

                for (int i = 0; i < labelCount; i++)
                {
                    if (currentPage == null ||
                        currentSlot >= labelsPerPage)
                    {
                        currentPage = CreateA4Page(
                            document,
                            pageWidth,
                            pageHeight);

                        currentSlot = 0;
                    }

                    int row = currentSlot / columns;
                    int column = currentSlot % columns;

                    double x =
                        pageWidth -
                        startX -
                        labelWidth -
                        (column * (labelWidth + gap));

                    double y =
                        startY +
                        (row * (labelHeight + gap));

                    if (x < 0 ||
                        y < 0 ||
                        x + labelWidth > pageWidth ||
                        y + labelHeight > pageHeight)
                    {
                        throw new InvalidOperationException(
                            "تجاوز أحد الملصقات حدود ورقة A4. راجع إعدادات المقاس والهامش والفاصل.");
                    }

                    DrawingVisual labelVisual =
                        CreateLabelVisual(
                            product,
                            labelWidthMm,
                            labelHeightMm,
                            showPrice,
                            showProductCode);

                    VisualHost host = new VisualHost(
                        labelVisual,
                        labelWidth,
                        labelHeight);

                    FixedPage.SetLeft(host, x);
                    FixedPage.SetTop(host, y);

                    currentPage.Children.Add(host);

                    currentSlot++;
                }
            }

            return document;
        }

        private FixedPage CreateA4Page(
            FixedDocument document,
            double pageWidth,
            double pageHeight)
        {
            FixedPage page = new FixedPage
            {
                Width = pageWidth,
                Height = pageHeight,
                Background = Brushes.White
            };

            PageContent pageContent = new PageContent
            {
                Child = page
            };

            document.Pages.Add(pageContent);

            return page;
        }

        #endregion

        #region Roll Document

        public FixedDocument CreateRollDocument(
            IEnumerable<Tuple<Product, int>> items,
            double labelWidthMm,
            double labelHeightMm,
            bool showPrice,
            bool showProductCode)
        {
            return CreateRollDocument(
                items,
                labelWidthMm,
                labelHeightMm,
                4,
                0,
                0,
                showPrice,
                showProductCode);
        }

        public FixedDocument CreateRollDocument(
            IEnumerable<Tuple<Product, int>> items,
            double labelWidthMm,
            double labelHeightMm,
            double gapMm,
            double offsetXmm,
            double offsetYmm,
            bool showPrice,
            bool showProductCode)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            if (!IsPositiveFinite(labelWidthMm) ||
                !IsPositiveFinite(labelHeightMm))
            {
                throw new ArgumentException(
                    "مقاس ملصق الرول غير صحيح.");
            }

            if (!IsNonNegativeFinite(gapMm) ||
                !IsFinite(offsetXmm) ||
                !IsFinite(offsetYmm))
            {
                throw new ArgumentException(
                    "إعدادات المسافة أو الإزاحة غير صحيحة.");
            }

            if (gapMm > 50 ||
                offsetXmm < -50 || offsetXmm > 50 ||
                offsetYmm < -50 || offsetYmm > 50)
            {
                throw new ArgumentException(
                    "إعدادات الرول خارج النطاق المسموح.");
            }

            List<Tuple<Product, int>> validItems =
                items.Where(x =>
                    x != null &&
                    x.Item1 != null &&
                    x.Item2 > 0).ToList();

            if (validItems.Count == 0)
            {
                throw new InvalidOperationException(
                    "لا توجد ملصقات صالحة للطباعة.");
            }

            double labelWidth = MmToDip(labelWidthMm);
            double labelHeight = MmToDip(labelHeightMm);

            double gap = MmToDip(gapMm);
            double offsetX = MmToDip(offsetXmm);
            double offsetY = MmToDip(offsetYmm);

            double pageWidth = labelWidth;
            double pageHeight = (labelHeight * 2) + gap;

            FixedDocument document = new FixedDocument();

            document.DocumentPaginator.PageSize =
                new Size(pageWidth, pageHeight);

            foreach (Tuple<Product, int> item in validItems)
            {
                Product product = item.Item1;
                int labelCount = item.Item2;

                int pairCount = (labelCount + 1) / 2;

                for (int pairIndex = 0;
                     pairIndex < pairCount;
                     pairIndex++)
                {
                    FixedPage page = new FixedPage
                    {
                        Width = pageWidth,
                        Height = pageHeight,
                        Background = Brushes.White,
                        ClipToBounds = true
                    };

                    DrawingVisual firstLabel =
                        CreateLabelVisual(
                            product,
                            labelWidthMm,
                            labelHeightMm,
                            showPrice,
                            showProductCode);

                    VisualHost firstHost = new VisualHost(
                        firstLabel,
                        labelWidth,
                        labelHeight);

                    FixedPage.SetLeft(firstHost, offsetX);
                    FixedPage.SetTop(firstHost, offsetY);

                    page.Children.Add(firstHost);

                    bool hasSecondLabel =
                        (pairIndex * 2 + 1) < labelCount;

                    if (hasSecondLabel)
                    {
                        DrawingVisual secondLabel =
                            CreateLabelVisual(
                                product,
                                labelWidthMm,
                                labelHeightMm,
                                showPrice,
                                showProductCode);

                        VisualHost secondHost = new VisualHost(
                            secondLabel,
                            labelWidth,
                            labelHeight);

                        FixedPage.SetLeft(secondHost, offsetX);

                        FixedPage.SetTop(
                            secondHost,
                            labelHeight + offsetY);

                        page.Children.Add(secondHost);
                    }

                    PageContent pageContent = new PageContent
                    {
                        Child = page
                    };

                    document.Pages.Add(pageContent);
                }
            }

            return document;
        }

        #endregion

        #region Visual Host

        private sealed class VisualHost : FrameworkElement
        {
            private readonly Visual childVisual;

            public VisualHost(
                Visual visual,
                double width,
                double height)
            {
                childVisual = visual;

                Width = width;
                Height = height;

                Clip = new RectangleGeometry(
                    new Rect(0, 0, width, height));

                AddVisualChild(childVisual);
            }

            protected override int VisualChildrenCount
            {
                get { return 1; }
            }

            protected override Visual GetVisualChild(int index)
            {
                if (index != 0)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return childVisual;
            }

            protected override Size MeasureOverride(
                Size availableSize)
            {
                return new Size(Width, Height);
            }

            protected override Size ArrangeOverride(
                Size finalSize)
            {
                return finalSize;
            }
        }

        #endregion

        #region EAN-13 Validation

        public bool IsValidEan13(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return false;

            if (barcode.Length != 13)
                return false;

            for (int i = 0; i < barcode.Length; i++)
            {
                if (barcode[i] < '0' || barcode[i] > '9')
                    return false;
            }

            int sum = 0;

            for (int i = 0; i < 12; i++)
            {
                int digit = barcode[i] - '0';

                sum += (i % 2 == 0)
                    ? digit
                    : digit * 3;
            }

            int checkDigit = (10 - (sum % 10)) % 10;

            return checkDigit == barcode[12] - '0';
        }

        #endregion

        #region Barcode Drawing

        private void DrawEan13(
            DrawingContext dc,
            string barcode,
            double x,
            double y,
            double width,
            double height)
        {
            string bits = EncodeEan13(barcode);

            const int quietModules = 9;
            const int totalModules = 95 + (quietModules * 2);

            double moduleWidth = width / totalModules;
            double startX = x + (quietModules * moduleWidth);

            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i] != '1')
                    continue;

                bool guard =
                    i < 3 ||
                    (i >= 45 && i < 50) ||
                    i >= 92;

                double barHeight = guard
                    ? height
                    : height * 0.88;

                dc.DrawRectangle(
                    Brushes.Black,
                    null,
                    new Rect(
                        startX + (i * moduleWidth),
                        y,
                        moduleWidth,
                        barHeight));
            }
        }

        private string EncodeEan13(string barcode)
        {
            string pattern = "101";

            int firstDigit = barcode[0] - '0';
            string parity = Parity[firstDigit];

            for (int i = 1; i <= 6; i++)
            {
                int digit = barcode[i] - '0';

                pattern += parity[i - 1] == 'L'
                    ? LeftOdd[digit]
                    : LeftEven[digit];
            }

            pattern += "01010";

            for (int i = 7; i <= 12; i++)
            {
                int digit = barcode[i] - '0';
                pattern += Right[digit];
            }

            pattern += "101";

            return pattern;
        }

        #endregion

        #region Text Drawing

        private void DrawProductName(
            DrawingContext dc,
            string value,
            double x,
            double y,
            double maxWidth,
            double maxHeight,
            double initialFontSize)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            Typeface typeface = new Typeface(
                new FontFamily("Arial"),
                FontStyles.Normal,
                FontWeights.Bold,
                FontStretches.Normal);

            const double minimumFontSize = 4.5;
            const double fontStep = 0.25;

            FormattedText bestText = null;
            double fontSize = Math.Max(
                minimumFontSize,
                initialFontSize);

            // قياس النص أولاً دون تحديد MaxTextHeight،
            // حتى لا يتم قصه أثناء حساب ارتفاعه.
            while (fontSize >= minimumFontSize)
            {
                FormattedText text = new FormattedText(
                    value,
                    CultureInfo.CurrentCulture,
                    FlowDirection.RightToLeft,
                    typeface,
                    fontSize,
                    Brushes.Black,
                    1.0);

                text.MaxTextWidth = Math.Max(1, maxWidth);
                text.TextAlignment = TextAlignment.Center;
                text.Trimming = TextTrimming.CharacterEllipsis;

                bestText = text;

                if (text.Height <= maxHeight)
                    break;

                fontSize -= fontStep;
            }

            if (bestText == null)
                return;

            // التأكد من أن النص المختار مناسب للمساحة.
            if (bestText.Height > maxHeight)
                return;

            // توسيط الاسم رأسياً داخل المساحة المخصصة له.
            double textY =
                y + ((maxHeight - bestText.Height) / 2);

            dc.PushClip(
                new RectangleGeometry(
                    new Rect(x, y, maxWidth, maxHeight)));

            dc.DrawText(
                bestText,
                new Point(x, textY));

            dc.Pop();
        }

        private void DrawText(
            DrawingContext dc,
            string value,
            double maxWidth,
            double x,
            double y,
            double maxHeight,
            double fontSize,
            TextAlignment alignment,
            FontWeight weight)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            Typeface typeface = new Typeface(
                new FontFamily("Arial"),
                FontStyles.Normal,
                weight,
                FontStretches.Normal);

            FormattedText text = new FormattedText(
                value,
                CultureInfo.CurrentCulture,
                FlowDirection.RightToLeft,
                typeface,
                fontSize,
                Brushes.Black,
                1.0);

            text.MaxTextWidth = Math.Max(1, maxWidth);
            text.MaxTextHeight = Math.Max(1, maxHeight);
            text.TextAlignment = alignment;
            text.Trimming = TextTrimming.CharacterEllipsis;

            dc.PushClip(
                new RectangleGeometry(
                    new Rect(x, y, maxWidth, maxHeight)));

            dc.DrawText(
                text,
                new Point(x, y));

            dc.Pop();
        }

        #endregion

        #region Helpers

        private double MmToDip(double millimeters)
        {
            return millimeters * 96.0 / 25.4;
        }

        private bool IsPositiveFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   value > 0;
        }

        private bool IsNonNegativeFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value) &&
                   value >= 0;
        }

        private bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        #endregion
    }
}
