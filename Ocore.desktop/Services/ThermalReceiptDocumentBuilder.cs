
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
        private const double DefaultPadding = 8;
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
                FontSize = 8.5 * fontScale,
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
            AddPayments(invoice);
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
                15,
                FontWeights.Bold,
                TextAlignment.Center,
                3);

            string cashierName =
                string.IsNullOrWhiteSpace(invoice.EmployeeName)
                    ? "-"
                    : invoice.EmployeeName.Trim();

            AddParagraph(
                "الكاشير: " + cashierName,
                8.5,
                FontWeights.Normal,
                TextAlignment.Center,
                3);

            AddParagraph(
                "فاتورة بيع",
                8.5,
                FontWeights.Normal,
                TextAlignment.Center,
                4);
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
        }

        #endregion

        #region Items

        private void AddItems(SaleInvoiceDetails invoice)
        {
            AddParagraph(
                "تفاصيل الأصناف",
                8.5,
                FontWeights.Normal,
                TextAlignment.Right,
                3);

            if (invoice.Items == null ||
                invoice.Items.Count == 0)
            {
                AddParagraph(
                    "لا توجد أصناف في الفاتورة.",
                    8.5,
                    FontWeights.Normal,
                    TextAlignment.Center,
                    3);

                return;
            }

            AddItemRow(
                "الصنف",
                "الكمية",
                "السعر",
                "الخصم",
                "الصافي",
                true);

            foreach (SaleInvoiceItem item in invoice.Items)
            {
                if (item == null)
                    continue;

                string itemName =
                    string.IsNullOrWhiteSpace(item.ItemName)
                        ? "صنف بدون اسم"
                        : item.ItemName.Trim();

                string unitName =
                    string.IsNullOrWhiteSpace(item.UnitName)
                        ? string.Empty
                        : " " + item.UnitName.Trim();

                string quantity =
                    FormatNumber(item.Quantity) + unitName;

                string price =
                    FormatNumber(item.UnitPrice);

                string discount =
                    FormatNumber(item.DiscountAmount);

                string net =
                    FormatNumber(item.NetTotal);

                AddItemRow(
                    itemName,
                    quantity,
                    price,
                    discount,
                    net,
                    false);
            }
        }

        private void AddItemRow(
            string itemName,
            string quantity,
            string price,
            string discount,
            string net,
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
                    Width = new GridLength(36, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(15, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(16, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(15, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(18, GridUnitType.Star)
                });

            double rowFontSize =
                (isHeader ? 7.3 : 7.0) * fontScale;

            AddItemCell(row, itemName, 0, rowFontSize, false);
            AddItemCell(row, quantity, 1, rowFontSize, true);
            AddItemCell(row, price, 2, rowFontSize, true);
            AddItemCell(row, discount, 3, rowFontSize, true);
            AddItemCell(row, net, 4, rowFontSize, true);

            row.Measure(
                new Size(
                    availableWidth,
                    double.PositiveInfinity));

            double rowHeight =
                Math.Max(15 * fontScale, row.DesiredSize.Height);

            BlockUIContainer container =
                new BlockUIContainer(row)
                {
                    Margin = new Thickness(0, 1, 0, 1),
                    TextAlignment = TextAlignment.Right
                };

            document.Blocks.Add(container);

            estimatedHeight += rowHeight + 2;
        }

        private void AddItemCell(
            Grid row,
            string value,
            int column,
            double fontSize,
            bool centered)
        {
            TextBlock textBlock = new TextBlock
            {
                Text = value ?? "-",
                FontFamily = new FontFamily("Arial"),
                FontSize = fontSize,
                FontWeight = FontWeights.Normal,
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = centered
                    ? TextAlignment.Center
                    : TextAlignment.Right,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 1, 0, 1),
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
            AddParagraph(
                "ملخص الفاتورة",
                8.5,
                FontWeights.Normal,
                TextAlignment.Right,
                3);

            double subtotal =
                invoice.Subtotal > 0
                    ? invoice.Subtotal
                    : invoice.Total;

            double netTotal =
                invoice.NetTotal > 0
                    ? invoice.NetTotal
                    : invoice.Total;

            AddLabelValue(
                "إجمالي الأصناف",
                FormatNumber(subtotal));

            if (invoice.DiscountAmount > 0)
            {
                AddLabelValue(
                    "إجمالي الخصومات",
                    FormatNumber(invoice.DiscountAmount));
            }

            if (invoice.DiscountValue > 0)
            {
                AddParagraph(
                    GetDiscountDescription(invoice),
                    8.5,
                    FontWeights.Normal,
                    TextAlignment.Right,
                    2);
            }

            AddLabelValue(
                "صافي الفاتورة",
                FormatNumber(netTotal));

            AddLabelValue(
                "المدفوع",
                FormatNumber(invoice.PaidAmount));

            AddLabelValue(
                "المتبقي",
                FormatNumber(invoice.RemainingAmount));

            AddLabelValue(
                "حالة السداد",
                GetPaymentStatus(invoice.PaymentStatus));
        }

        private string GetDiscountDescription(
            SaleInvoiceDetails invoice)
        {
            if (string.Equals(
                invoice.DiscountType,
                "Percentage",
                StringComparison.OrdinalIgnoreCase))
            {
                return "الخصم العام المطلوب: " +
                       FormatNumber(invoice.DiscountValue) + "%";
            }

            if (string.Equals(
                invoice.DiscountType,
                "Amount",
                StringComparison.OrdinalIgnoreCase))
            {
                return "الخصم العام المطلوب: " +
                       FormatNumber(invoice.DiscountValue);
            }

            return "الخصم العام";
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
                    return "بطاقة";

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
                    8.5,
                    FontWeights.Normal,
                    TextAlignment.Center,
                    3);
            }

            AddParagraph(
                "شكراً لتعاملكم معنا",
                8.5,
                FontWeights.Normal,
                TextAlignment.Center,
                2);

            AddParagraph(
                "نتمنى لكم دوام الصحة والعافية",
                8.5,
                FontWeights.Normal,
                TextAlignment.Center,
                2);
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

            string line =
                label + ": " + safeValue;

            Paragraph paragraph = new Paragraph
            {
                FlowDirection = FlowDirection.RightToLeft,
                TextAlignment = TextAlignment.Right,
                FontSize = 8.5 * fontScale,
                FontWeight = FontWeights.Normal,
                Margin = new Thickness(0, 1, 0, 1)
            };

            paragraph.Inlines.Add(
                new Run(line)
                {
                    FontWeight = FontWeights.Normal
                });

            document.Blocks.Add(paragraph);

            estimatedHeight += EstimateTextHeight(
                line,
                8.5,
                3);
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
                FontSize = fontSize * fontScale,
                FontWeight = weight,
                Margin = new Thickness(0, 1, 0, bottomMargin)
            };

            paragraph.Inlines.Add(
                new Run(text)
                {
                    FontWeight = weight
                });

            document.Blocks.Add(paragraph);

            estimatedHeight += EstimateTextHeight(
                text,
                fontSize,
                bottomMargin + 2);
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
                FontSize = fontSize * fontScale,
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
                measuredHeight =
                    fontSize * fontScale * 1.8;
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
