
using Ocore.desktop.Models.Printing;
using Ocore.desktop.Services.Printing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ocore.desktop.Services
{
    public class ShiftClosurePrintingService
    {
        private const int PrintDpi = 203;

        private readonly ShiftService shiftService;
        private readonly ExpenseService expenseService;
        private readonly PrinterConfigurationService printerConfigurationService;
        private readonly ThermalPrinterService thermalPrinterService;
        private readonly ThermalReceiptRenderer receiptRenderer;

        private readonly CultureInfo culture =
            CultureInfo.GetCultureInfo("ar-EG");

        public ShiftClosurePrintingService()
        {
            shiftService = new ShiftService();
            expenseService = new ExpenseService();

            printerConfigurationService =
                new PrinterConfigurationService();

            thermalPrinterService =
                new ThermalPrinterService();

            receiptRenderer =
                new ThermalReceiptRenderer();
        }

        public async Task PrintClosureAsync(int shiftId)
        {
            if (shiftId <= 0)
                throw new ArgumentOutOfRangeException(nameof(shiftId));

            var closure = shiftService.GetShiftClosure(shiftId);

            if (closure == null)
                throw new InvalidOperationException(
                    "لا يوجد تقرير إغلاق محفوظ لهذا الشيفت.");

            var shift = shiftService.GetAllShifts()
                .FirstOrDefault(x => x.Id == shiftId);

            if (shift == null)
                throw new InvalidOperationException(
                    "تعذر العثور على بيانات الشيفت.");

            var configuration =
                printerConfigurationService.GetConfiguration(
                    PrinterType.Cashier);

            if (configuration == null ||
                string.IsNullOrWhiteSpace(configuration.PrinterName))
            {
                throw new InvalidOperationException(
                    "لم يتم تحديد طابعة الكاشير. " +
                    "يرجى اختيارها من إعدادات الطابعات أولاً.");
            }

            string printerName = configuration.PrinterName.Trim();

            if (!thermalPrinterService.IsPrinterInstalled(printerName))
            {
                throw new InvalidOperationException(
                    "طابعة الكاشير المحددة غير مثبتة في Windows:\n" +
                    printerName);
            }

            double paperWidth =
                configuration.PaperWidth == 80 ||
                configuration.PaperWidth == 58
                    ? configuration.PaperWidth
                    : 58;

            double fontScale = configuration.FontScale;

            if (double.IsNaN(fontScale) ||
                double.IsInfinity(fontScale) ||
                fontScale < 0.7 ||
                fontScale > 1.5)
            {
                fontScale = 1.0;
            }

            var expenses = expenseService
                .GetExpensesByShift(shiftId)
                .Where(x => !x.IsVoided)
                .OrderBy(x => x.Id)
                .ToList();

            FlowDocument document = BuildDocument(
                shift,
                closure,
                expenses,
                paperWidth,
                configuration.PrintWidthDots,
                fontScale);

            BitmapSource image = receiptRenderer.RenderDocument(
                document,
                paperWidth,
                PrintDpi);

            if (image == null || !image.IsFrozen)
            {
                throw new InvalidOperationException(
                    "تعذر تجهيز صورة تقرير إغلاق الشيفت.");
            }

            string printMode =
                (configuration.PrintMode ?? "WindowsDriver").Trim();

            if (string.Equals(
                printMode,
                "WindowsDriver",
                StringComparison.OrdinalIgnoreCase))
            {
                await thermalPrinterService.PrintImageWithWindowsDriverAsync(
                    printerName,
                    image,
                    paperWidth);

                return;
            }

            if (string.Equals(
                printMode,
                "EscPos",
                StringComparison.OrdinalIgnoreCase))
            {
                byte[] rasterData =
                    await receiptRenderer.RenderToEscPosAsync(
                        image,
                        paperWidth,
                        PrintDpi);

                if (rasterData == null || rasterData.Length == 0)
                {
                    throw new InvalidOperationException(
                        "تعذر تحويل التقرير إلى بيانات الطباعة الحرارية.");
                }

                var printData = new List<byte>(rasterData);

                // POS-80C:
                // تنفيذ القص الجزئي مباشرة بعد آخر بيانات التقرير
                // دون تغذية ورق إضافية.
                // هذا الأمر خاص بتقرير إغلاق الشيفت فقط.
                printData.AddRange(new byte[]
                {
                    0x1D, 0x56, 0x01
                });

                await thermalPrinterService.SendRawAsync(
                    printerName,
                    printData.ToArray());

                return;
            }

            throw new InvalidOperationException(
                "طريقة طباعة طابعة الكاشير غير مدعومة.");
        }

        private FlowDocument BuildDocument(
            ShiftService.ShiftInfo shift,
            ShiftService.ShiftClosureInfo closure,
            List<ExpenseService.ExpenseInfo> expenses,
            double paperWidth,
            int printWidthDots,
            double fontScale)
        {
            if (printWidthDots < 0 || printWidthDots > 2048)
            {
                throw new InvalidOperationException(
                    "عرض الطباعة المحدد في إعدادات الكاشير غير صالح.");
            }

            double pageWidth = printWidthDots > 0
                ? printWidthDots * 96.0 / PrintDpi
                : paperWidth * 96.0 / 25.4;

            double pageHeight = Math.Min(
                30000,
                Math.Max(900, 950 + expenses.Count * 34));

            var document = new FlowDocument
            {
                FlowDirection = FlowDirection.RightToLeft,
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                PagePadding = new Thickness(8),
                ColumnWidth = pageWidth - 16,
                FontFamily = new FontFamily("Arial"),
                FontSize = 11 * fontScale,
                Foreground = Brushes.Black
            };

            AddParagraph(
                document,
                "O CORE",
                21 * fontScale,
                true,
                TextAlignment.Center);

            AddParagraph(
                document,
                "تقرير إغلاق شيفت",
                15 * fontScale,
                true,
                TextAlignment.Center);

            AddSeparator(document);

            AddParagraph(document, "بيانات الشيفت", 13 * fontScale, true);

            AddInfoLine(
                document,
                "رقم الشيفت",
                string.IsNullOrWhiteSpace(shift.ShiftNumber)
                    ? shift.Id.ToString()
                    : shift.ShiftNumber);

            AddInfoLine(
                document,
                "تاريخ الفتح",
                FormatDate(shift.OpenedAt));

            AddInfoLine(
                document,
                "تاريخ الإغلاق",
                FormatDate(closure.ClosedAt));

            AddInfoLine(
                document,
                "الصيدلي الذي أغلق",
                EmptyAsDash(closure.ClosedByName));

            AddSeparator(document);

            AddParagraph(
                document,
                "أولاً: مقارنة المبالغ",
                13 * fontScale,
                true);

            AddTable(
                document,
                new[] { "وسيلة الدفع", "المتوقع", "الفعلي", "الفرق" },
                new[]
                {
                    new[]
                    {
                        "نقدية",
                        Money(closure.ExpectedCash),
                        Money(closure.ActualCash),
                        Difference(closure.DifferenceCash)
                    },
                    new[]
                    {
                        "فيزا",
                        Money(closure.ExpectedCard),
                        Money(closure.ActualCard),
                        Difference(closure.DifferenceCard)
                    },
                    new[]
                    {
                        "إنستا باي",
                        Money(closure.ExpectedInstaPay),
                        Money(closure.ActualInstaPay),
                        Difference(closure.DifferenceInstaPay)
                    },
                    new[]
                    {
                        "تحويل بنكي",
                        Money(closure.ExpectedTransfer),
                        Money(closure.ActualTransfer),
                        Difference(closure.DifferenceTransfer)
                    },
                    new[]
                    {
                        "الإجمالي",
                        Money(closure.TotalExpected),
                        Money(closure.TotalActual),
                        Difference(closure.TotalDifference)
                    }
                },
                new[] { 1.25, 1.0, 1.0, 1.0 },
                fontScale);

            AddSeparator(document);

            AddParagraph(
                document,
                "ثانياً: المصروفات المصروفة فعلياً",
                13 * fontScale,
                true);

            if (expenses.Count == 0)
            {
                AddParagraph(
                    document,
                    "لا توجد مصروفات فعلية خلال هذا الشيفت.",
                    10 * fontScale,
                    false);
            }
            else
            {
                var expenseRows = expenses.Select(x =>
                    new[]
                    {
                        BuildExpenseName(x),
                        Money(x.Amount)
                    }).ToList();

                expenseRows.Add(new[]
                {
                    "إجمالي المصروفات",
                    Money(expenses.Sum(x => x.Amount))
                });

                AddTable(
                    document,
                    new[] { "بيان المصروف", "المبلغ" },
                    expenseRows,
                    new[] { 2.0, 1.0 },
                    fontScale);
            }

            AddSeparator(document);

            AddParagraph(
                document,
                "ثالثاً: ملخص الإغلاق",
                13 * fontScale,
                true);

            AddInfoLine(
                document,
                "إجمالي المتوقع",
                Money(closure.TotalExpected));

            AddInfoLine(
                document,
                "إجمالي الفعلي",
                Money(closure.TotalActual));

            AddInfoLine(
                document,
                "صافي الفرق",
                Difference(closure.TotalDifference));

            AddInfoLine(
                document,
                "المصروفات المصروفة",
                Money(expenses.Sum(x => x.Amount)));

            AddSeparator(document);

            AddParagraph(
                document,
                "تم إغلاق الشيفت بنجاح",
                13 * fontScale,
                true,
                TextAlignment.Center);

            AddParagraph(
                document,
                "تقرير مخصص للمدير",
                10 * fontScale,
                false,
                TextAlignment.Center);

            AddParagraph(
                document,
                "O CORE Pharmacy System",
                9 * fontScale,
                false,
                TextAlignment.Center);

            return document;
        }

        private void AddTable(
            FlowDocument document,
            string[] headers,
            IEnumerable<string[]> rows,
            double[] columnWeights,
            double fontScale)
        {
            var table = new Table
            {
                CellSpacing = 0,
                FlowDirection = FlowDirection.RightToLeft
            };

            foreach (double weight in columnWeights)
            {
                table.Columns.Add(
                    new TableColumn
                    {
                        Width = new GridLength(
                            weight,
                            GridUnitType.Star)
                    });
            }

            var group = new TableRowGroup();

            group.Rows.Add(
                CreateTableRow(headers, true, fontScale));

            foreach (var row in rows)
            {
                group.Rows.Add(
                    CreateTableRow(row, false, fontScale));
            }

            table.RowGroups.Add(group);
            document.Blocks.Add(table);
        }

        private TableRow CreateTableRow(
            string[] values,
            bool header,
            double fontScale)
        {
            var row = new TableRow();

            foreach (string value in values)
            {
                var paragraph = new Paragraph(
                    new Run(value ?? string.Empty))
                {
                    Margin = new Thickness(1),
                    FontSize = (header ? 9.5 : 9) * fontScale,
                    FontWeight = header
                        ? FontWeights.Bold
                        : FontWeights.Normal,
                    TextAlignment = TextAlignment.Center,
                    FlowDirection = FlowDirection.RightToLeft
                };

                var cell = new TableCell(paragraph)
                {
                    Padding = new Thickness(3),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(0.4)
                };

                row.Cells.Add(cell);
            }

            return row;
        }

        private void AddInfoLine(
            FlowDocument document,
            string label,
            string value)
        {
            AddParagraph(
                document,
                label + ": " + value,
                10.5,
                false);
        }

        private void AddParagraph(
            FlowDocument document,
            string text,
            double fontSize,
            bool bold,
            TextAlignment alignment = TextAlignment.Right)
        {
            var paragraph = new Paragraph(new Run(text ?? string.Empty))
            {
                Margin = new Thickness(0, 3, 0, 3),
                FontSize = fontSize,
                FontWeight = bold
                    ? FontWeights.Bold
                    : FontWeights.Normal,
                TextAlignment = alignment,
                FlowDirection = FlowDirection.RightToLeft
            };

            document.Blocks.Add(paragraph);
        }

        private void AddSeparator(FlowDocument document)
        {
            AddParagraph(
                document,
                "--------------------------------",
                9,
                false,
                TextAlignment.Center);
        }

        private string BuildExpenseName(
            ExpenseService.ExpenseInfo expense)
        {
            string category = EmptyAsDash(expense.Category);
            string description = (expense.Description ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(description))
                return category;

            return category + " - " + description;
        }

        private string Money(double value)
        {
            return value.ToString("N2", culture);
        }

        private string Difference(double value)
        {
            if (value > 0)
                return "+" + Money(value);

            if (value < 0)
                return Money(value);

            return Money(0);
        }

        private string FormatDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";

            DateTime date;

            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
            {
                return date.ToString(
                    "yyyy/MM/dd hh:mm tt",
                    culture);
            }

            return value;
        }

        private string EmptyAsDash(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "—"
                : value.Trim();
        }
    }
}
