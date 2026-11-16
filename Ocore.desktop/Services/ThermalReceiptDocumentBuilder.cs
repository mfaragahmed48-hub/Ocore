
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class ThermalReceiptDocumentBuilder
    {
        private const double DefaultPadding = 5;
        private const double ReceiptFontSize = 7.2;
        private const double MinimumPageHeight = 180;
        private const int DefaultDpi = 203;
        private const double MaximumPageHeight = 30000;
        private const int MaximumLayoutAttempts = 8;

        private readonly CultureInfo culture =
            CultureInfo.GetCultureInfo("ar-EG");

        private double availableWidth;
        private double fontScale;
        private double estimatedHeight;
        private FlowDocument document;

        // الاستدعاء القديم للتوافق مع بقية أجزاء البرنامج.
        public FlowDocument Build(
            SaleInvoiceDetails invoice,
            PharmacySettings settings,
            double paperWidthMm,
            double selectedFontScale = 1.0)
        {
            int legacyPrintWidthDots =
                settings == null
                    ? 0
                    : settings.ThermalPrintWidthDots;

            return Build(
                invoice,
                settings,
                paperWidthMm,
                selectedFontScale,
                legacyPrintWidthDots);
        }

        // الاستدعاء الجديد الذي يستقبل عرض الطباعة من الإعدادات المركزية.
        public FlowDocument Build(
            SaleInvoiceDetails invoice,
            PharmacySettings settings,
            double paperWidthMm,
            double selectedFontScale,
            int printWidthDots)
        {
            if (invoice == null)
                throw new ArgumentNullException(nameof(invoice));

            if (paperWidthMm != 58 && paperWidthMm != 80)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(paperWidthMm),
                    "عرض الورق المدعوم هو 58 أو 80 مم.");
            }

            if (double.IsNaN(selectedFontScale) ||
                double.IsInfinity(selectedFontScale) ||
                selectedFontScale < 0.7 ||
                selectedFontScale > 1.5)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(selectedFontScale),
                    "حجم الخط يجب أن يكون بين 0.7 و1.5.");
            }

            fontScale = selectedFontScale;

            if (printWidthDots < 0 || printWidthDots > 2048)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(printWidthDots),
                    "عرض الطباعة الفعلي يجب أن يكون صفرًا أو من 1 إلى 2048 نقطة.");
            }

            double pageWidth =
                printWidthDots > 0
                    ? printWidthDots * 96.0 / DefaultDpi
                    : paperWidthMm / 25.4 * 96.0;

            availableWidth =
                pageWidth - (DefaultPadding * 2);

            if (availableWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(printWidthDots),
                    "عرض الطباعة المحدد صغير جدًا.");
            }

            estimatedHeight = DefaultPadding * 2;

            document = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                FontFamily = new FontFamily("Arial"),
                FontSize = ReceiptFontSize * fontScale,
                PageWidth = pageWidth,
                PageHeight = MinimumPageHeight,
                PagePadding = new Thickness(DefaultPadding),
                ColumnWidth = double.PositiveInfinity,
                IsColumnWidthFlexible = false,
                TextAlignment = TextAlignment.Right
            };

            AddHeader(settings, invoice);
            AddInvoiceInformation(invoice);
            AddItems(invoice);
            AddFinancialSummary(invoice);
            AddFooter(settings);

            double initialHeight = Math.Max(
                MinimumPageHeight,
                Math.Ceiling(estimatedHeight + 25));

            document.PageHeight = Math.Min(
                initialHeight,
                MaximumPageHeight);

            FitDocumentToSinglePage(pageWidth);

            return document;
        }

        #region Dynamic Page Height

        private void FitDocumentToSinglePage(double pageWidth)
        {
            IDocumentPaginatorSource paginatorSource = document;

            DocumentPaginator paginator =
                paginatorSource.DocumentPaginator;

            for (int attempt = 0;
                 attempt < MaximumLayoutAttempts;
                 attempt++)
            {
                double currentHeight = document.PageHeight;

                paginator.PageSize = new Size(
                    pageWidth,
                    currentHeight);

                paginator.ComputePageCount();

                if (paginator.PageCount == 1)
                    return;

                if (paginator.PageCount <= 0)
                {
                    throw new InvalidOperationException(
                        "تعذر تحديد عدد صفحات الإيصال.");
                }

                if (currentHeight >= MaximumPageHeight)
                    break;

                double nextHeight = Math.Ceiling(
                    Math.Max(
                        currentHeight + 100,
                        currentHeight * 1.5));

                document.PageHeight = Math.Min(
                    nextHeight,
                    MaximumPageHeight);
            }

            paginator.PageSize = new Size(
                pageWidth,
                document.PageHeight);

            paginator.ComputePageCount();

            if (paginator.PageCount != 1)
            {
                throw new InvalidOperationException(
                    "تعذر احتواء الفاتورة بالكامل داخل صفحة واحدة. " +
                    "حجم محتوى الفاتورة تجاوز الحد الآمن لارتفاع الإيصال.");
            }
        }

        #endregion

        #region Header

        private void AddHeader(
            PharmacySettings settings,
            SaleInvoiceDetails invoice)
        {
            string pharmacyName =
                settings == null ||
                string.IsNullOrWhiteSpace(settings.PharmacyName)
                    ? "O CORE"
                    : settings.PharmacyName.Trim();

            AddParagraph(
                pharmacyName,
                ReceiptFontSize,
                FontWeights.Normal,
                TextAlignment.Center,
                1);

            string cashierName =
                string.IsNullOrWhiteSpace(invoice.EmployeeName)
                    ? "-"
                    : invoice.EmployeeName.Trim();

            AddParagraph(
                "الكاشير: " + cashierName,
                ReceiptFontSize,
                FontWeights.Normal,
                TextAlignment.Center,
                1);

            AddParagraph(
                "فاتورة بيع",
                ReceiptFontSize,
                FontWeights.Normal,
                TextAlignment.Center,
                2);
        }

        #endregion

        #region Invoice Information

        private void AddInvoiceInformation(
            SaleInvoiceDetails invoice)
        {
            AddLabelValue(
                "رقم الفاتورة",
                invoice.InvoiceNumber);

            AddLabelValue(
                "العميل",
                string.IsNullOrWhiteSpace(invoice.CustomerName)
                    ? "عميل نقدي"
                    : invoice.CustomerName);

            string dateText =
                invoice.CreatedAt == DateTime.MinValue
                    ? "-"
                    : invoice.CreatedAt.ToString(
                        "dd/MM/yyyy HH:mm",
                        culture);

            AddLabelValue("التاريخ", dateText);
            AddHorizontalRule();
        }

        #endregion

        #region Items

        private void AddItems(SaleInvoiceDetails invoice)
        {
            if (invoice.Items == null || invoice.Items.Count == 0)
            {
                AddParagraph(
                    "لا توجد أصناف في الفاتورة.",
                    ReceiptFontSize,
                    FontWeights.Normal,
                    TextAlignment.Center,
                    2);
                return;
            }

            AddItemRow(
                "الصنف",
                "الكمية",
                "السعر",
                "الإجمالي",
                true);

            AddHorizontalRule();

            foreach (SaleInvoiceItem item in invoice.Items)
            {
                if (item == null)
                    continue;

                string itemName =
                    string.IsNullOrWhiteSpace(item.ItemName)
                        ? "صنف بدون اسم"
                        : item.ItemName.Trim();

                // عند بيع الوحدة الصغيرة تكون Quantity بوحدات صغيرة،
                // بينما PackageQuantity تكون مكافئها من العبوات.
                bool isSmallUnit =
                    Math.Abs(item.Quantity - item.PackageQuantity) > 0.0000001;

                string quantity =
                    (isSmallUnit ? "U " : "P ") +
                    item.Quantity.ToString("0.##", culture);

                AddItemRow(
                    itemName,
                    quantity,
                    FormatNumber(item.UnitPrice),
                    FormatNumber(item.Total),
                    false);
            }

            AddHorizontalRule();
        }

        private void AddItemRow(
            string itemName,
            string quantity,
            string price,
            string total,
            bool isHeader)
        {
            Grid row = new Grid
            {
                Width = availableWidth,
                FlowDirection = FlowDirection.RightToLeft,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(42, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(18, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(18, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(22, GridUnitType.Star)
                });

            AddItemCell(row, itemName, 0, false);
            AddItemCell(row, quantity, 1, true);
            AddItemCell(row, price, 2, true);
            AddItemCell(row, total, 3, true);

            row.Measure(
                new Size(
                    availableWidth,
                    double.PositiveInfinity));

            double rowHeight =
                Math.Max(ReceiptFontSize * 2, row.DesiredSize.Height);

            BlockUIContainer container =
                new BlockUIContainer(row)
                {
                    Margin = new Thickness(0),
                    TextAlignment = TextAlignment.Right
                };

            document.Blocks.Add(container);
            estimatedHeight += rowHeight + 1;
        }

        private void AddItemCell(
            Grid row,
            string value,
            int column,
            bool centered)
        {
            TextBlock textBlock = new TextBlock
            {
                Text = value ?? "-",
                FontFamily = new FontFamily("Arial"),
                FontSize = ReceiptFontSize * fontScale,
                FontWeight = FontWeights.Normal,
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = centered
                    ? TextAlignment.Center
                    : TextAlignment.Right,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0),
                Padding = new Thickness(0)
            };

            Grid.SetColumn(textBlock, column);
            row.Children.Add(textBlock);
        }

        #endregion

        #region Financial Summary

        private void AddFinancialSummary(
            SaleInvoiceDetails invoice)
        {
            double subtotal =
                invoice.Subtotal > 0
                    ? invoice.Subtotal
                    : invoice.Total;

            double netTotal =
                invoice.NetTotal > 0
                    ? invoice.NetTotal
                    : invoice.Total;

            int itemCount = 0;

            if (invoice.Items != null)
            {
                foreach (SaleInvoiceItem item in invoice.Items)
                {
                    if (item != null)
                        itemCount++;
                }
            }

            AddSummaryRow(
                "عدد الأصناف: " + itemCount.ToString(culture),
                "إجمالي الأصناف: " + FormatNumber(subtotal));

            AddSummaryRow(
                "ض.ق.م: -",
                "الخصم: " + FormatNumber(invoice.DiscountAmount));

            AddHorizontalRule();

            string paymentDescription =
                "حالة السداد: " + GetPaymentStatus(invoice.PaymentStatus) +
                " | الدفع: " + GetPaymentMethodsSummary(invoice);

            AddSummaryRow(
                paymentDescription,
                "صافي المطلوب: " + FormatNumber(netTotal));
        }

        private void AddSummaryRow(
            string rightText,
            string leftText)
        {
            Grid row = new Grid
            {
                Width = availableWidth,
                FlowDirection = FlowDirection.RightToLeft,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

            AddSummaryCell(row, rightText, 0, false);
            AddSummaryCell(row, leftText, 1, true);

            row.Measure(
                new Size(
                    availableWidth,
                    double.PositiveInfinity));

            double rowHeight =
                Math.Max(ReceiptFontSize * 2, row.DesiredSize.Height);

            document.Blocks.Add(
                new BlockUIContainer(row)
                {
                    Margin = new Thickness(0, 1, 0, 1),
                    TextAlignment = TextAlignment.Right
                });

            estimatedHeight += rowHeight + 2;
        }

        private void AddSummaryCell(
            Grid row,
            string value,
            int column,
            bool centered)
        {
            TextBlock textBlock = new TextBlock
            {
                Text = value ?? "-",
                FontFamily = new FontFamily("Arial"),
                FontSize = ReceiptFontSize * fontScale,
                FontWeight = FontWeights.Normal,
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = centered
                    ? TextAlignment.Center
                    : TextAlignment.Right,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                Padding = new Thickness(0)
            };

            Grid.SetColumn(textBlock, column);
            row.Children.Add(textBlock);
        }

        private string GetPaymentMethodsSummary(
            SaleInvoiceDetails invoice)
        {
            if (invoice.Payments == null ||
                invoice.Payments.Count == 0)
            {
                return "غير محدد";
            }

            var methods = new System.Collections.Generic.List<string>();

            foreach (SaleInvoicePaymentDetails payment in invoice.Payments)
            {
                if (payment == null ||
                    payment.Amount <= 0)
                    continue;

                string method = GetPaymentMethod(payment.PaymentMethod);

                if (!string.IsNullOrWhiteSpace(method) &&
                    method != "-" &&
                    !methods.Contains(method))
                {
                    methods.Add(method);
                }
            }

            return methods.Count == 0
                ? "غير محدد"
                : string.Join(" + ", methods);
        }

        #endregion

        #region Payments

        private void AddPayments(SaleInvoiceDetails invoice)
        {
            if (invoice.Payments == null ||
                invoice.Payments.Count == 0)
            {
                return;
            }

            AddParagraph(
                "تفاصيل الدفعات",
                8.5,
                FontWeights.Normal,
                TextAlignment.Right,
                3);

            for (int i = 0; i < invoice.Payments.Count; i++)
            {
                SaleInvoicePaymentDetails payment =
                    invoice.Payments[i];

                if (payment == null)
                    continue;

                AddParagraph(
                    "دفعة " + (i + 1),
                    8.5,
                    FontWeights.Normal,
                    TextAlignment.Right,
                    2);

                AddLabelValue(
                    "طريقة الدفع",
                    GetPaymentMethod(payment.PaymentMethod));

                AddLabelValue(
                    "المبلغ",
                    FormatNumber(payment.Amount));

                if (!string.IsNullOrWhiteSpace(
                    payment.ReferenceNumber))
                {
                    AddLabelValue(
                        "رقم المرجع",
                        payment.ReferenceNumber);
                }

                if (!string.IsNullOrWhiteSpace(payment.Notes))
                {
                    AddParagraph(
                        "ملاحظات: " + payment.Notes,
                        8.5,
                        FontWeights.Normal,
                        TextAlignment.Right,
                        2);
                }
            }
        }

        private string GetPaymentMethod(string method)
        {
            switch ((method ?? "").Trim())
            {
                case "Cash":
                    return "نقدي";

                case "Card":
                    return "فيزا";

                case "InstaPay":
                    return "إنستا";

                case "Transfer":
                    return "تحويل";

                default:
                    return string.IsNullOrWhiteSpace(method)
                        ? "-"
                        : method;
            }
        }

        private string GetPaymentStatus(string status)
        {
            switch ((status ?? "").Trim())
            {
                case "Paid":
                    return "مدفوع بالكامل";

                case "Partial":
                    return "مدفوع جزئيًا";

                case "Unpaid":
                    return "غير مدفوع";

                case "Legacy":
                    return "فاتورة قديمة";

                default:
                    return string.IsNullOrWhiteSpace(status)
                        ? "غير محدد"
                        : status;
            }
        }

        #endregion

        #region Footer

        private void AddFooter(PharmacySettings settings)
        {
            if (settings != null &&
                !string.IsNullOrWhiteSpace(settings.Phone))
            {
                AddParagraph(
                    "هاتف الصيدلية: " + settings.Phone.Trim(),
                    ReceiptFontSize,
                    FontWeights.Normal,
                    TextAlignment.Center,
                    1);
            }

            AddParagraph(
                "شكراً لتعاملكم معنا",
                ReceiptFontSize,
                FontWeights.Normal,
                TextAlignment.Center,
                1);
        }

        #endregion

        #region Document Helpers

        private void AddLabelValue(
            string label,
            string value)
        {
            string safeValue =
                string.IsNullOrWhiteSpace(value)
                    ? "-"
                    : value;

            string line = label + ": " + safeValue;

            Paragraph paragraph = new Paragraph
            {
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = TextAlignment.Right,
                FontSize = ReceiptFontSize * fontScale,
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(0)
            };

            paragraph.Inlines.Add(
                new Run(line)
                {
                    FontWeight = FontWeights.Normal
                });

            document.Blocks.Add(paragraph);

            estimatedHeight += EstimateTextHeight(
                line,
                ReceiptFontSize,
                1);
        }

        private void AddParagraph(
            string text,
            double fontSize,
            FontWeight weight,
            TextAlignment alignment,
            double bottomMargin)
        {
            if (text == null)
                text = string.Empty;

            Paragraph paragraph = new Paragraph
            {
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = alignment,
                FontSize = ReceiptFontSize * fontScale,
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, bottomMargin)
            };

            paragraph.Inlines.Add(
                new Run(text)
                {
                    FontWeight = FontWeights.Normal
                });

            document.Blocks.Add(paragraph);

            estimatedHeight += EstimateTextHeight(
                text,
                ReceiptFontSize,
                bottomMargin + 1);
        }

        private void AddHorizontalRule()
        {
            Border rule = new Border
            {
                Width = availableWidth,
                Height = 1,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 0, 0, 0.7),
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            document.Blocks.Add(
                new BlockUIContainer(rule)
                {
                    Margin = new Thickness(0),
                    TextAlignment = TextAlignment.Right
                });

            estimatedHeight += 5;
        }

        private double EstimateTextHeight(
            string text,
            double fontSize,
            double extraHeight)
        {
            TextBlock measureBlock = new TextBlock
            {
                Text = text ?? string.Empty,
                FontFamily = new FontFamily("Arial"),
                FontSize = ReceiptFontSize * fontScale,
                FlowDirection = FlowDirection.RightToLeft,
                TextWrapping = TextWrapping.Wrap
            };

            measureBlock.Measure(
                new Size(
                    availableWidth,
                    double.PositiveInfinity));

            double measuredHeight =
                measureBlock.DesiredSize.Height;

            if (double.IsNaN(measuredHeight) ||
                double.IsInfinity(measuredHeight) ||
                measuredHeight <= 0)
            {
                measuredHeight = ReceiptFontSize * fontScale * 1.8;
            }

            return measuredHeight + extraHeight;
        }

        private string FormatNumber(double value)
        {
            return value.ToString("N2", culture);
        }

        #endregion
    }
}
