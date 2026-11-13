
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class SalesService
    {
        private readonly string connectionString;

        private const double Epsilon = 0.00000001;
        private const double PriceTolerance = 0.000001;
        private const double MoneyTolerance = 0.005;

        public SalesService()
        {
            var database = new DatabaseManager();
            database.InitializeDatabase();

            connectionString =
                "Data Source=" + database.GetDatabasePath() +
                ";Version=3;";
        }

        public string SaveInvoice(
            int userId,
            List<SaleInvoiceItem> items)
        {
            return SaveInvoice(
                userId,
                items,
                null,
                "Amount",
                0,
                -1,
                "Cash",
                string.Empty);
        }

        public string SaveInvoice(
            int userId,
            List<SaleInvoiceItem> items,
            int? customerId,
            string discountType,
            double discountValue,
            double paidAmount,
            string paymentMethod,
            string paymentReference,
            double cardAmount = -1,
            string cardReference = "",
            double cashAmount = -1,
            double instaPayAmount = -1)
        {
            if (items == null || items.Count == 0)
                throw new InvalidOperationException("الفاتورة لا تحتوي على أصناف.");

            if (double.IsNaN(discountValue) ||
                double.IsInfinity(discountValue) ||
                discountValue < 0)
                throw new InvalidOperationException("قيمة الخصم غير صحيحة.");

            discountType = string.IsNullOrWhiteSpace(discountType)
                ? "Amount"
                : discountType.Trim();

            if (!string.Equals(discountType, "Amount", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(discountType, "Percentage", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("نوع الخصم غير صحيح.");

            bool legacyCall = paidAmount < 0;

            if (!legacyCall &&
                (double.IsNaN(paidAmount) || double.IsInfinity(paidAmount)))
                throw new InvalidOperationException("قيمة المبلغ المدفوع غير صحيحة.");

            paymentReference = (paymentReference ?? string.Empty).Trim();
            paymentMethod = (paymentMethod ?? string.Empty).Trim();

            if (double.IsNaN(cardAmount) || double.IsInfinity(cardAmount))
                throw new InvalidOperationException("مبلغ الفيزا غير صحيح.");

            if (cardAmount < 0 && cardAmount != -1)
                throw new InvalidOperationException("مبلغ الفيزا لا يمكن أن يكون سالبًا.");

            if (double.IsNaN(cashAmount) || double.IsInfinity(cashAmount))
                throw new InvalidOperationException("مبلغ النقدي غير صحيح.");

            if (cashAmount < 0 && cashAmount != -1)
                throw new InvalidOperationException("مبلغ النقدي لا يمكن أن يكون سالبًا.");

            if (double.IsNaN(instaPayAmount) || double.IsInfinity(instaPayAmount))
                throw new InvalidOperationException("مبلغ إنستا باي غير صحيح.");

            if (instaPayAmount < 0 && instaPayAmount != -1)
                throw new InvalidOperationException("مبلغ إنستا باي لا يمكن أن يكون سالبًا.");

            bool explicitPaymentSplit =
                cashAmount >= 0 || instaPayAmount >= 0;

            if (explicitPaymentSplit &&
                (cashAmount < 0 || cardAmount < 0 || instaPayAmount < 0))
                throw new InvalidOperationException("بيانات تقسيم الدفع غير مكتملة.");

            bool automaticCardSplit =
                cardAmount >= 0 && !explicitPaymentSplit;

            cardReference = (cardReference ?? string.Empty).Trim();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var pragma = new SQLiteCommand(
                    "PRAGMA foreign_keys = ON;", connection))
                {
                    pragma.ExecuteNonQuery();
                }

                using (var transaction = connection.BeginTransaction(
                    IsolationLevel.Serializable))
                {
                    try
                    {
                        string username;
                        string role;

                        ValidateUserAndPermission(
                            connection, transaction, userId,
                            out username, out role);

                        if (customerId.HasValue)
                            ValidateCustomer(connection, transaction, customerId.Value);

                        string invoiceNumber =
                            "INV-" +
                            DateTime.Now.ToString(
                                "yyyyMMdd-HHmmss",
                                CultureInfo.InvariantCulture) +
                            "-" +
                            Guid.NewGuid().ToString("N").Substring(0, 8);

                        string createdAt = DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss",
                            CultureInfo.InvariantCulture);

                        using (var command = new SQLiteCommand(
                            @"INSERT INTO SalesInvoices
                              (
                                  InvoiceNumber, UserId, CustomerId,
                                  Subtotal, DiscountType, DiscountValue,
                                  DiscountAmount, NetTotal, PaidAmount,
                                  RemainingAmount, PaymentStatus, Total,
                                  Status, CreatedAt
                              )
                              VALUES
                              (
                                  @number, @userId, @customerId,
                                  0, @discountType, @discountValue,
                                  0, 0, 0, 0, 'Unpaid', 0,
                                  'Completed', @createdAt
                              );",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue("@number", invoiceNumber);
                            command.Parameters.AddWithValue("@userId", userId);
                            command.Parameters.AddWithValue(
                                "@customerId",
                                customerId.HasValue
                                    ? (object)customerId.Value
                                    : DBNull.Value);
                            command.Parameters.AddWithValue("@discountType", discountType);
                            command.Parameters.AddWithValue("@discountValue", discountValue);
                            command.Parameters.AddWithValue("@createdAt", createdAt);
                            command.ExecuteNonQuery();
                        }

                        long invoiceId;

                        using (var command = new SQLiteCommand(
                            "SELECT last_insert_rowid();", connection, transaction))
                        {
                            invoiceId = Convert.ToInt64(command.ExecuteScalar());
                        }

                        var preparedItems = new List<PreparedSaleLine>();
                        double subtotal = 0;

                        foreach (var item in items)
                        {
                            if (item == null ||
                                item.ProductId <= 0 ||
                                item.Quantity <= 0 ||
                                double.IsNaN(item.Quantity) ||
                                double.IsInfinity(item.Quantity))
                                throw new InvalidOperationException(
                                    "يوجد صنف أو كمية غير صحيحة في الفاتورة.");

                            if (Math.Abs(item.Quantity - Math.Round(item.Quantity)) > Epsilon)
                                throw new InvalidOperationException(
                                    "كمية الصنف " + item.ItemName +
                                    " يجب أن تكون عددًا صحيحًا.");

                            ProductSaleInfo product = GetProduct(
                                connection, transaction, item.ProductId);

                            if (product == null)
                                throw new InvalidOperationException("أحد الأصناف غير موجود.");

                            if (product.IsArchived)
                                throw new InvalidOperationException(
                                    "الصنف " + product.Name + " مؤرشف ولا يمكن بيعه.");

                            if (product.UnitsPerPackage < 1)
                                throw new InvalidOperationException(
                                    "بيانات وحدات الصنف " + product.Name + " غير صحيحة.");

                            if (double.IsNaN(product.SalePrice) ||
                                double.IsInfinity(product.SalePrice) ||
                                product.SalePrice < 0)
                                throw new InvalidOperationException(
                                    "سعر بيع الصنف " + product.Name + " غير صحيح.");

                            if (double.IsNaN(product.MaxDiscountPercentage) ||
                                double.IsInfinity(product.MaxDiscountPercentage) ||
                                product.MaxDiscountPercentage < 0 ||
                                product.MaxDiscountPercentage > 100)
                                throw new InvalidOperationException(
                                    "الحد الأقصى للخصم المسجل للصنف " +
                                    product.Name + " غير صحيح.");

                            double packageQuantity;
                            double unitPrice;

                            if (string.Equals(
                                item.UnitName, product.UnitName,
                                StringComparison.Ordinal))
                            {
                                packageQuantity = item.Quantity;
                                unitPrice = product.SalePrice;
                            }
                            else if (!string.IsNullOrWhiteSpace(product.SmallUnitName) &&
                                     string.Equals(
                                         item.UnitName, product.SmallUnitName,
                                         StringComparison.Ordinal))
                            {
                                packageQuantity =
                                    item.Quantity / product.UnitsPerPackage;

                                unitPrice =
                                    product.SalePrice / product.UnitsPerPackage;
                            }
                            else
                            {
                                throw new InvalidOperationException(
                                    "وحدة البيع للصنف " + product.Name + " غير صحيحة.");
                            }

                            if (double.IsNaN(item.UnitPrice) ||
                                double.IsInfinity(item.UnitPrice) ||
                                item.UnitPrice < 0)
                                throw new InvalidOperationException(
                                    "سعر الصنف " + product.Name +
                                    " غير صحيح داخل الفاتورة.");

                            if (Math.Abs(item.UnitPrice - unitPrice) > PriceTolerance)
                                throw new InvalidOperationException(
                                    "تغير سعر الصنف " + product.Name +
                                    " بعد إضافته للفاتورة.\n" +
                                    "السعر الحالي للوحدة: " +
                                    unitPrice.ToString("0.00", CultureInfo.InvariantCulture) +
                                    "\nيرجى تحديث الفاتورة وإضافة الصنف مرة أخرى.");

                            packageQuantity = Math.Round(packageQuantity, 8);

                            if (packageQuantity <= 0 ||
                                double.IsNaN(packageQuantity) ||
                                double.IsInfinity(packageQuantity))
                                throw new InvalidOperationException("كمية البيع غير صحيحة.");

                            // التحقق من اكتمال التوزيع اليدوي قبل تجهيز الفاتورة.
                            ValidateBatchAllocations(
                                item,
                                packageQuantity,
                                product.Name);

                            double lineTotal = RoundMoney(item.Quantity * unitPrice);

                            if (double.IsNaN(lineTotal) ||
                                double.IsInfinity(lineTotal) ||
                                lineTotal < 0)
                                throw new InvalidOperationException(
                                    "إجمالي الصنف " + product.Name + " غير صحيح.");

                            double itemDiscountPercentage = item.DiscountPercentage;

                            if (double.IsNaN(itemDiscountPercentage) ||
                                double.IsInfinity(itemDiscountPercentage) ||
                                itemDiscountPercentage < 0)
                                throw new InvalidOperationException(
                                    "نسبة خصم الصنف " + product.Name + " غير صحيحة.");

                            if (itemDiscountPercentage >
                                product.MaxDiscountPercentage + Epsilon)
                                throw new InvalidOperationException(
                                    "نسبة الخصم المطلوبة للصنف " + product.Name +
                                    " تتجاوز الحد الأقصى المسموح به (" +
                                    product.MaxDiscountPercentage.ToString(
                                        "0.##", CultureInfo.InvariantCulture) + "%).");

                            if (itemDiscountPercentage > 100)
                                throw new InvalidOperationException(
                                    "نسبة الخصم لا يمكن أن تتجاوز 100%.");

                            double itemDiscountAmount = RoundMoney(
                                lineTotal * itemDiscountPercentage / 100.0);

                            long lineTotalCents = ToMoneyCents(lineTotal);

                            long maxDiscountCents = ToMoneyCents(RoundMoney(
                                lineTotal * product.MaxDiscountPercentage / 100.0));

                            long itemDiscountCents = ToMoneyCents(itemDiscountAmount);

                            if (itemDiscountCents > maxDiscountCents)
                                throw new InvalidOperationException(
                                    "خصم الصنف " + product.Name +
                                    " يتجاوز الحد الأقصى المسموح به بعد التقريب.");

                            preparedItems.Add(new PreparedSaleLine
                            {
                                Product = product,
                                OriginalItem = item,
                                UnitName = item.UnitName,
                                Quantity = item.Quantity,
                                UnitPrice = unitPrice,
                                PackageQuantity = packageQuantity,
                                BatchAllocations =
                                    CopyBatchAllocations(item.BatchAllocations),
                                LineTotal = lineTotal,
                                LineTotalCents = lineTotalCents,
                                MaxDiscountCents = maxDiscountCents,
                                ItemDiscountCents = itemDiscountCents,
                                GeneralDiscountCents = 0
                            });

                            subtotal += lineTotal;

                            if (double.IsNaN(subtotal) || double.IsInfinity(subtotal))
                                throw new InvalidOperationException(
                                    "إجمالي الفاتورة غير صحيح.");
                        }

                        subtotal = RoundMoney(subtotal);

                        long subtotalCents = ToMoneyCents(subtotal);
                        long manualDiscountTotalCents = 0;

                        foreach (var line in preparedItems)
                            manualDiscountTotalCents += line.ItemDiscountCents;

                        long subtotalAfterManualDiscountCents =
                            subtotalCents - manualDiscountTotalCents;

                        if (subtotalAfterManualDiscountCents < 0)
                            throw new InvalidOperationException(
                                "إجمالي الخصومات اليدوية غير صحيح.");

                        // الخصم النسبي يُحسب على صافي الفاتورة
                        // بعد الخصومات اليدوية، وليس على الإجمالي الأصلي.
                        double requestedGeneralDiscount;

                        if (string.Equals(
                            discountType, "Percentage",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            if (discountValue > 100)
                                throw new InvalidOperationException(
                                    "نسبة الخصم العام لا يمكن أن تتجاوز 100%.");

                            requestedGeneralDiscount = RoundMoney(
                                FromMoneyCents(subtotalAfterManualDiscountCents) *
                                discountValue / 100.0);
                        }
                        else
                        {
                            long requestedAmountCents =
                                ToMoneyCents(RoundMoney(discountValue));

                            if (requestedAmountCents >
                                subtotalAfterManualDiscountCents)
                                throw new InvalidOperationException(
                                    "قيمة الخصم العام لا يمكن أن تتجاوز إجمالي الأصناف بعد الخصم اليدوي.");

                            requestedGeneralDiscount = RoundMoney(discountValue);
                        }

                        long requestedGeneralDiscountCents =
                            ToMoneyCents(requestedGeneralDiscount);

                        AllocateGeneralDiscount(
                            preparedItems,
                            discountType,
                            discountValue,
                            requestedGeneralDiscountCents);

                        bool hasAnyDiscount = false;
                        long totalItemDiscountCents = 0;
                        long totalGeneralDiscountCents = 0;

                        foreach (var line in preparedItems)
                        {
                            totalItemDiscountCents += line.ItemDiscountCents;
                            totalGeneralDiscountCents += line.GeneralDiscountCents;

                            if (line.ItemDiscountCents > 0 ||
                                line.GeneralDiscountCents > 0)
                                hasAnyDiscount = true;
                        }

                        if (hasAnyDiscount)
                            ValidateDiscountPermission(connection, transaction, role);

                        long totalDiscountCents =
                            totalItemDiscountCents + totalGeneralDiscountCents;

                        if (totalDiscountCents > subtotalCents)
                            throw new InvalidOperationException(
                                "إجمالي الخصومات أكبر من إجمالي الفاتورة.");

                        double itemDiscountTotal =
                            FromMoneyCents(totalItemDiscountCents);

                        double generalDiscountAmount =
                            FromMoneyCents(totalGeneralDiscountCents);

                        double discountAmount =
                            FromMoneyCents(totalDiscountCents);

                        double netTotal = FromMoneyCents(
                            subtotalCents - totalDiscountCents);

                        double cashPaymentAmount = 0;
                        double cardPaymentAmount = 0;
                        double instaPayPaymentAmount = 0;

                        if (legacyCall && !explicitPaymentSplit)
                        {
                            paidAmount = netTotal;
                            paymentMethod = "Cash";
                        }

                        if (explicitPaymentSplit)
                        {
                            if (cashAmount > netTotal + MoneyTolerance ||
                                cardAmount > netTotal + MoneyTolerance ||
                                instaPayAmount > netTotal + MoneyTolerance)
                                throw new InvalidOperationException(
                                    "أحد مبالغ الدفع يتجاوز صافي الفاتورة.");

                            cashPaymentAmount = RoundMoney(cashAmount);
                            cardPaymentAmount = RoundMoney(cardAmount);
                            instaPayPaymentAmount = RoundMoney(instaPayAmount);

                            double splitTotal = RoundMoney(
                                cashPaymentAmount +
                                cardPaymentAmount +
                                instaPayPaymentAmount);

                            if (Math.Abs(splitTotal - netTotal) > MoneyTolerance)
                                throw new InvalidOperationException(
                                    "مجموع مبالغ النقدي والفيزا وإنستا باي يجب أن يساوي صافي الفاتورة.");

                            ValidateSplitPaymentMethod(
                                paymentMethod,
                                cashPaymentAmount,
                                cardPaymentAmount,
                                instaPayPaymentAmount,
                                netTotal);

                            if (cardPaymentAmount > 0 &&
                                string.IsNullOrWhiteSpace(cardReference))
                                throw new InvalidOperationException(
                                    "يجب إدخال رقم عملية الفيزا.");

                            if (cardPaymentAmount <= 0 &&
                                !string.IsNullOrWhiteSpace(cardReference))
                                throw new InvalidOperationException(
                                    "لا يمكن إدخال رقم عملية فيزا دون وجود مبلغ فيزا.");

                            paidAmount = netTotal;
                        }
                        else if (automaticCardSplit)
                        {
                            if (cardAmount > netTotal + MoneyTolerance)
                                throw new InvalidOperationException(
                                    "مبلغ الفيزا لا يمكن أن يتجاوز صافي الفاتورة.");

                            cardPaymentAmount = RoundMoney(
                                Math.Min(cardAmount, netTotal));

                            if (cardPaymentAmount > 0 &&
                                string.IsNullOrWhiteSpace(cardReference))
                                throw new InvalidOperationException(
                                    "يجب إدخال رقم عملية الفيزا.");

                            if (cardPaymentAmount <= 0 &&
                                !string.IsNullOrWhiteSpace(cardReference))
                                throw new InvalidOperationException(
                                    "لا يمكن إدخال رقم عملية فيزا دون وجود مبلغ فيزا.");

                            cashPaymentAmount = RoundMoney(
                                netTotal - cardPaymentAmount);

                            paidAmount = RoundMoney(
                                cashPaymentAmount + cardPaymentAmount);
                        }

                        if (paidAmount < 0 ||
                            paidAmount > netTotal + MoneyTolerance)
                            throw new InvalidOperationException(
                                "المبلغ المدفوع يجب أن يكون بين صفر وصافي الفاتورة.");

                        paidAmount = RoundMoney(Math.Min(paidAmount, netTotal));

                        double remainingAmount = RoundMoney(netTotal - paidAmount);

                        if (remainingAmount < MoneyTolerance)
                            remainingAmount = 0;

                        if (remainingAmount > 0 && !customerId.HasValue)
                            throw new InvalidOperationException(
                                "لا يمكن حفظ فاتورة عليها مبلغ متبقٍ دون اختيار عميل مسجل.\n" +
                                "اختر العميل لتسجيل المديونية على حسابه.");

                        if (paidAmount > 0 &&
                            !automaticCardSplit &&
                            !explicitPaymentSplit)
                            ValidatePaymentMethod(paymentMethod);

                        string paymentStatus;

                        if (remainingAmount == 0)
                            paymentStatus = "Paid";
                        else if (paidAmount > 0)
                            paymentStatus = "Partial";
                        else
                            paymentStatus = "Unpaid";

                        foreach (var line in preparedItems)
                        {
                            long finalDiscountCents =
                                line.ItemDiscountCents + line.GeneralDiscountCents;

                            double finalDiscountAmount =
                                FromMoneyCents(finalDiscountCents);

                            double manualDiscountPercentage =
                                line.OriginalItem.DiscountPercentage;

                            if (finalDiscountCents > line.MaxDiscountCents)
                                throw new InvalidOperationException(
                                    "الخصم النهائي للصنف " + line.Product.Name +
                                    " تجاوز الحد الأقصى المسموح به.");

                            long invoiceItemId;

                            using (var command = new SQLiteCommand(
                                @"INSERT INTO SalesInvoiceItems
                                  (
                                      InvoiceId, ProductId, ItemName, UnitName,
                                      Quantity, UnitPrice, Total, PackageQuantity,
                                      DiscountPercentage, DiscountAmount
                                  )
                                  VALUES
                                  (
                                      @invoiceId, @productId, @name, @unitName,
                                      @quantity, @price, @total, @packageQuantity,
                                      @discountPercentage, @discountAmount
                                  );",
                                connection, transaction))
                            {
                                command.Parameters.AddWithValue("@invoiceId", invoiceId);
                                command.Parameters.AddWithValue("@productId", line.Product.Id);
                                command.Parameters.AddWithValue("@name", line.Product.Name);
                                command.Parameters.AddWithValue("@unitName", line.UnitName);
                                command.Parameters.AddWithValue("@quantity", line.Quantity);
                                command.Parameters.AddWithValue("@price", line.UnitPrice);
                                command.Parameters.AddWithValue("@total", line.LineTotal);
                                command.Parameters.AddWithValue(
                                    "@packageQuantity", line.PackageQuantity);
                                command.Parameters.AddWithValue(
                                    "@discountPercentage", manualDiscountPercentage);
                                command.Parameters.AddWithValue(
                                    "@discountAmount", finalDiscountAmount);
                                command.ExecuteNonQuery();
                            }

                            using (var command = new SQLiteCommand(
                                "SELECT last_insert_rowid();", connection, transaction))
                            {
                                invoiceItemId = Convert.ToInt64(command.ExecuteScalar());
                            }

                            // الخصم من التشغيلات التي حددها المستخدم فقط.
                            DeductFromSelectedBatches(
                                connection,
                                transaction,
                                invoiceItemId,
                                line.Product.Id,
                                line.PackageQuantity,
                                line.BatchAllocations);
                        }

                        using (var command = new SQLiteCommand(
                            @"UPDATE SalesInvoices
                              SET
                                  CustomerId = @customerId,
                                  Subtotal = @subtotal,
                                  DiscountType = @discountType,
                                  DiscountValue = @discountValue,
                                  DiscountAmount = @discountAmount,
                                  NetTotal = @netTotal,
                                  PaidAmount = @paidAmount,
                                  RemainingAmount = @remainingAmount,
                                  PaymentStatus = @paymentStatus,
                                  Total = @netTotal
                              WHERE Id = @id;",
                            connection, transaction))
                        {
                            command.Parameters.AddWithValue(
                                "@customerId",
                                customerId.HasValue
                                    ? (object)customerId.Value
                                    : DBNull.Value);
                            command.Parameters.AddWithValue("@subtotal", subtotal);
                            command.Parameters.AddWithValue("@discountType", discountType);
                            command.Parameters.AddWithValue("@discountValue", discountValue);
                            command.Parameters.AddWithValue("@discountAmount", discountAmount);
                            command.Parameters.AddWithValue("@netTotal", netTotal);
                            command.Parameters.AddWithValue("@paidAmount", paidAmount);
                            command.Parameters.AddWithValue("@remainingAmount", remainingAmount);
                            command.Parameters.AddWithValue("@paymentStatus", paymentStatus);
                            command.Parameters.AddWithValue("@id", invoiceId);
                            command.ExecuteNonQuery();
                        }

                        if (explicitPaymentSplit)
                        {
                            InsertInvoicePayment(
                                connection, transaction, invoiceId,
                                "Cash", cashPaymentAmount, string.Empty,
                                userId, createdAt);

                            InsertInvoicePayment(
                                connection, transaction, invoiceId,
                                "Card", cardPaymentAmount, cardReference,
                                userId, createdAt);

                            InsertInvoicePayment(
                                connection, transaction, invoiceId,
                                "InstaPay", instaPayPaymentAmount, string.Empty,
                                userId, createdAt);
                        }
                        else if (automaticCardSplit)
                        {
                            InsertInvoicePayment(
                                connection, transaction, invoiceId,
                                "Cash", cashPaymentAmount, string.Empty,
                                userId, createdAt);

                            InsertInvoicePayment(
                                connection, transaction, invoiceId,
                                "Card", cardPaymentAmount, cardReference,
                                userId, createdAt);
                        }
                        else if (paidAmount > 0)
                        {
                            InsertInvoicePayment(
                                connection, transaction, invoiceId,
                                paymentMethod, paidAmount, paymentReference,
                                userId, createdAt);
                        }

                        if (customerId.HasValue && remainingAmount > 0)
                        {
                            using (var command = new SQLiteCommand(
                                @"INSERT INTO CustomerAccountTransactions
                                  (
                                      CustomerId, TransactionType, Amount,
                                      ReferenceType, ReferenceId, PaymentMethod,
                                      ReferenceNumber, Notes, UserId, CreatedAt
                                  )
                                  VALUES
                                  (
                                      @customerId, 'SaleInvoice', @amount,
                                      'SalesInvoice', @invoiceId, '',
                                      '', @notes, @userId, @createdAt
                                  );",
                                connection, transaction))
                            {
                                command.Parameters.AddWithValue("@customerId", customerId.Value);
                                command.Parameters.AddWithValue("@amount", remainingAmount);
                                command.Parameters.AddWithValue("@invoiceId", invoiceId);
                                command.Parameters.AddWithValue(
                                    "@notes", "مديونية فاتورة بيع رقم " + invoiceNumber);
                                command.Parameters.AddWithValue("@userId", userId);
                                command.Parameters.AddWithValue("@createdAt", createdAt);
                                command.ExecuteNonQuery();
                            }
                        }

                        WriteAuditLog(
                            connection, transaction, userId, username,
                            invoiceId, invoiceNumber, subtotal,
                            itemDiscountTotal, generalDiscountAmount,
                            discountAmount, netTotal, createdAt);

                        transaction.Commit();
                        return invoiceNumber;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        // يتحقق من أن المستخدم وزع كامل الكمية المطلوبة يدويًا.
        // يسمح بالتشغيلات غير المختارة التي تكون كميتها صفرًا.
        private void ValidateBatchAllocations(
            SaleInvoiceItem item,
            double requiredQuantity,
            string productName)
        {
            if (item.BatchAllocations == null ||
                item.BatchAllocations.Count == 0)
            {
                throw new InvalidOperationException(
                    "يجب توزيع كمية الصنف " + productName +
                    " على التشغيلات يدويًا قبل حفظ الفاتورة.");
            }

            double allocatedTotal = 0;
            var usedBatchIds = new HashSet<int>();

            foreach (var allocation in item.BatchAllocations)
            {
                if (allocation == null ||
                    allocation.BatchId <= 0 ||
                    double.IsNaN(allocation.AllocatedQuantity) ||
                    double.IsInfinity(allocation.AllocatedQuantity) ||
                    allocation.AllocatedQuantity < 0)
                {
                    throw new InvalidOperationException(
                        "يوجد توزيع غير صحيح في تشغيلات الصنف " +
                        productName + ".");
                }

                // التشغيلة ذات الكمية صفر غير مختارة.
                if (allocation.AllocatedQuantity <= Epsilon)
                    continue;

                if (!usedBatchIds.Add(allocation.BatchId))
                {
                    throw new InvalidOperationException(
                        "تم اختيار نفس التشغيلة أكثر من مرة للصنف " +
                        productName + ".");
                }

                allocatedTotal = Math.Round(
                    allocatedTotal + allocation.AllocatedQuantity, 8);
            }

            if (Math.Abs(allocatedTotal - requiredQuantity) > Epsilon)
            {
                double remaining = Math.Round(
                    requiredQuantity - allocatedTotal, 8);

                if (remaining > Epsilon)
                {
                    throw new InvalidOperationException(
                        "توزيع الصنف " + productName + " غير مكتمل.\n" +
                        "الكمية المطلوبة: " +
                        requiredQuantity.ToString(
                            "0.########", CultureInfo.InvariantCulture) +
                        "\nالكمية الموزعة: " +
                        allocatedTotal.ToString(
                            "0.########", CultureInfo.InvariantCulture) +
                        "\nالكمية المتبقية: " +
                        remaining.ToString(
                            "0.########", CultureInfo.InvariantCulture));
                }

                throw new InvalidOperationException(
                    "الكمية الموزعة للصنف " + productName +
                    " أكبر من الكمية المطلوبة.");
            }
        }

        private List<SaleInvoiceBatchAllocation> CopyBatchAllocations(
            System.Collections.ObjectModel.ObservableCollection<SaleInvoiceBatchAllocation> allocations)
        {
            var result = new List<SaleInvoiceBatchAllocation>();

            if (allocations == null)
                return result;

            foreach (var allocation in allocations)
            {
                if (allocation == null)
                    continue;

                result.Add(new SaleInvoiceBatchAllocation
                {
                    BatchId = allocation.BatchId,
                    BatchNumber = allocation.BatchNumber,
                    ExpiryDate = allocation.ExpiryDate,
                    AvailableQuantity = allocation.AvailableQuantity,
                    AllocatedQuantity = allocation.AllocatedQuantity
                });
            }

            return result;
        }

        // يخصم من التشغيلات التي اختارها المستخدم فقط.
        // يتجاهل التشغيلات ذات الكمية صفر.
        // جميع عمليات التحقق والخصم والتسجيل داخل معاملة الفاتورة.
        private void DeductFromSelectedBatches(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long invoiceItemId,
            int productId,
            double requiredQuantity,
            List<SaleInvoiceBatchAllocation> allocations)
        {
            if (allocations == null || allocations.Count == 0)
                throw new InvalidOperationException(
                    "لم يتم تحديد تشغيلات لصرف الصنف.");

            string today = DateTime.Today.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture);

            double allocatedTotal = 0;
            var usedBatchIds = new HashSet<int>();

            foreach (var allocation in allocations)
            {
                if (allocation == null ||
                    allocation.BatchId <= 0 ||
                    double.IsNaN(allocation.AllocatedQuantity) ||
                    double.IsInfinity(allocation.AllocatedQuantity) ||
                    allocation.AllocatedQuantity < 0)
                {
                    throw new InvalidOperationException(
                        "بيانات إحدى التشغيلات المحددة غير صحيحة.");
                }

                double quantityToTake = Math.Round(
                    allocation.AllocatedQuantity, 8);

                // لا تخصم ولا تسجل التشغيلات التي لم يحدد المستخدم منها كمية.
                if (quantityToTake <= Epsilon)
                    continue;

                if (!usedBatchIds.Add(allocation.BatchId))
                    throw new InvalidOperationException(
                        "لا يمكن تكرار التشغيلة نفسها في توزيع الصنف.");

                int batchId = allocation.BatchId;

                string expiryDate;
                double currentQuantity;

                using (var command = new SQLiteCommand(
                    @"SELECT ExpiryDate, Quantity
                      FROM Batches
                      WHERE Id = @batchId
                        AND ProductId = @productId
                      LIMIT 1;",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@batchId", batchId);
                    command.Parameters.AddWithValue("@productId", productId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new InvalidOperationException(
                                "إحدى التشغيلات المحددة لا تتبع الصنف الحالي.");

                        expiryDate =
                            reader["ExpiryDate"] == DBNull.Value
                                ? string.Empty
                                : reader["ExpiryDate"].ToString().Trim();

                        currentQuantity = ReadDouble(
                            reader["Quantity"], 0);
                    }
                }

                if (!string.IsNullOrWhiteSpace(expiryDate) &&
                    string.Compare(
                        expiryDate, today,
                        StringComparison.Ordinal) < 0)
                {
                    throw new InvalidOperationException(
                        "لا يمكن البيع من تشغيلة منتهية الصلاحية: " +
                        (allocation.BatchNumber ?? batchId.ToString()));
                }

                if (currentQuantity + Epsilon < quantityToTake)
                {
                    throw new InvalidOperationException(
                        "رصيد التشغيلة " +
                        (string.IsNullOrWhiteSpace(allocation.BatchNumber)
                            ? batchId.ToString()
                            : allocation.BatchNumber) +
                        " غير كافٍ.\n" +
                        "المتاح حاليًا: " +
                        currentQuantity.ToString(
                            "0.########", CultureInfo.InvariantCulture) +
                        "\nالمطلوب صرفه: " +
                        quantityToTake.ToString(
                            "0.########", CultureInfo.InvariantCulture));
                }

                using (var command = new SQLiteCommand(
                    @"UPDATE Batches
                      SET Quantity =
                          CASE
                              WHEN Quantity - @quantity <= @epsilon THEN 0
                              ELSE Quantity - @quantity
                          END
                      WHERE Id = @batchId
                        AND ProductId = @productId
                        AND Quantity + @epsilon >= @quantity
                        AND (
                            ExpiryDate = ''
                            OR ExpiryDate IS NULL
                            OR ExpiryDate >= @today
                        );",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@quantity", quantityToTake);
                    command.Parameters.AddWithValue("@epsilon", Epsilon);
                    command.Parameters.AddWithValue("@batchId", batchId);
                    command.Parameters.AddWithValue("@productId", productId);
                    command.Parameters.AddWithValue("@today", today);

                    if (command.ExecuteNonQuery() != 1)
                    {
                        throw new InvalidOperationException(
                            "تعذر خصم الكمية من التشغيلة المحددة. " +
                            "قد يكون رصيدها تغير أو انتهت صلاحيتها. أعد مراجعة التوزيع.");
                    }
                }

                using (var command = new SQLiteCommand(
                    @"INSERT INTO SalesInvoiceBatchAllocations
                      (InvoiceItemId, BatchId, Quantity)
                      VALUES (@itemId, @batchId, @quantity);",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@itemId", invoiceItemId);
                    command.Parameters.AddWithValue("@batchId", batchId);
                    command.Parameters.AddWithValue("@quantity", quantityToTake);
                    command.ExecuteNonQuery();
                }

                allocatedTotal = Math.Round(
                    allocatedTotal + quantityToTake, 8);
            }

            if (Math.Abs(allocatedTotal - requiredQuantity) > Epsilon)
            {
                throw new InvalidOperationException(
                    "مجموع الكميات المصروفة من التشغيلات لا يساوي كمية الصنف المطلوبة.");
            }
        }

        private void ValidateSplitPaymentMethod(
            string paymentMethod,
            double cashAmount,
            double cardAmount,
            double instaPayAmount,
            double netTotal)
        {
            bool hasCash = cashAmount > 0;
            bool hasCard = cardAmount > 0;
            bool hasInstaPay = instaPayAmount > 0;

            bool valid = false;

            switch ((paymentMethod ?? string.Empty).Trim())
            {
                case "Cash":
                    valid = hasCash &&
                            !hasCard &&
                            !hasInstaPay &&
                            Math.Abs(cashAmount - netTotal) <= MoneyTolerance;
                    break;

                case "Card":
                    valid = !hasCash &&
                            hasCard &&
                            !hasInstaPay &&
                            Math.Abs(cardAmount - netTotal) <= MoneyTolerance;
                    break;

                case "InstaPay":
                    valid = !hasCash &&
                            !hasCard &&
                            hasInstaPay &&
                            Math.Abs(instaPayAmount - netTotal) <= MoneyTolerance;
                    break;

                case "CashCard":
                    valid = hasCash && hasCard && !hasInstaPay;
                    break;

                case "CashInstaPay":
                    valid = hasCash && !hasCard && hasInstaPay;
                    break;

                case "CardInstaPay":
                    valid = !hasCash && hasCard && hasInstaPay;
                    break;

                case "CashCardInstaPay":
                    valid = hasCash && hasCard && hasInstaPay;
                    break;
            }

            if (!valid)
                throw new InvalidOperationException(
                    "مبالغ الدفع لا تتوافق مع طريقة الدفع المختارة.");
        }

        private void InsertInvoicePayment(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long invoiceId,
            string paymentMethod,
            double amount,
            string reference,
            int userId,
            string createdAt)
        {
            if (amount <= 0)
                return;

            using (var command = new SQLiteCommand(
                @"INSERT INTO SalesInvoicePayments
                  (
                      InvoiceId, PaymentMethod, Amount,
                      ReferenceNumber, Notes, UserId, CreatedAt
                  )
                  VALUES
                  (
                      @invoiceId, @method, @amount,
                      @reference, '', @userId, @createdAt
                  );",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@invoiceId", invoiceId);
                command.Parameters.AddWithValue("@method", paymentMethod);
                command.Parameters.AddWithValue("@amount", amount);
                command.Parameters.AddWithValue(
                    "@reference",
                    reference ?? string.Empty);
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@createdAt", createdAt);

                command.ExecuteNonQuery();
            }
        }

        // توزيع الخصم العام بالقروش على أساس قيمة كل صنف بعد الخصم اليدوي.
        // لا يعاد توزيع المبلغ الذي يتجاوز سقف أي صنف.
        private void AllocateGeneralDiscount(
            List<PreparedSaleLine> lines,
            string discountType,
            double discountValue,
            long requestedDiscountCents)
        {
            if (requestedDiscountCents <= 0 ||
                lines == null ||
                lines.Count == 0)
                return;

            var eligibleLines = new List<PreparedSaleLine>();
            long totalEligibleWeight = 0;

            foreach (var line in lines)
            {
                long remainingCapacity =
                    line.MaxDiscountCents - line.ItemDiscountCents;

                long allocationWeight = Math.Max(
                    0,
                    line.LineTotalCents - line.ItemDiscountCents);

                if (remainingCapacity <= 0 || allocationWeight <= 0)
                    continue;

                line.GeneralDiscountCapacityCents = remainingCapacity;
                line.GeneralDiscountCents = 0;
                line.AllocationWeight = allocationWeight;

                eligibleLines.Add(line);
                totalEligibleWeight += allocationWeight;
            }

            if (eligibleLines.Count == 0 || totalEligibleWeight <= 0)
                return;

            var fractions = new List<DiscountFraction>();
            long rawBaseSharesTotal = 0;

            for (int i = 0; i < eligibleLines.Count; i++)
            {
                PreparedSaleLine line = eligibleLines[i];

                decimal exactShare =
                    (decimal)requestedDiscountCents *
                    line.AllocationWeight /
                    totalEligibleWeight;

                long rawBaseShare = (long)Math.Floor(exactShare);
                rawBaseSharesTotal += rawBaseShare;

                line.GeneralDiscountCents = Math.Min(
                    rawBaseShare,
                    line.GeneralDiscountCapacityCents);

                fractions.Add(new DiscountFraction
                {
                    Line = line,
                    Fraction = exactShare - Math.Floor(exactShare),
                    OriginalOrder = i
                });
            }

            // هذا الفرق خاص بقروش التقريب فقط، وليس بالمبالغ
            // التي لم تسمح حدود الأصناف بتطبيقها.
            long roundingRemainder =
                requestedDiscountCents - rawBaseSharesTotal;

            fractions.Sort(delegate (
                DiscountFraction first,
                DiscountFraction second)
            {
                int fractionComparison =
                    second.Fraction.CompareTo(first.Fraction);

                if (fractionComparison != 0)
                    return fractionComparison;

                return first.OriginalOrder.CompareTo(second.OriginalOrder);
            });

            foreach (var fraction in fractions)
            {
                if (roundingRemainder <= 0)
                    break;

                PreparedSaleLine line = fraction.Line;

                if (line.GeneralDiscountCents >=
                    line.GeneralDiscountCapacityCents)
                    continue;

                line.GeneralDiscountCents++;
                roundingRemainder--;
            }
        }

        private static long ToMoneyCents(double value)
        {
            return Convert.ToInt64(Math.Round(
                value * 100.0, 0, MidpointRounding.AwayFromZero));
        }

        private static double FromMoneyCents(long cents)
        {
            return cents / 100.0;
        }

        private static double RoundMoney(double value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private void ValidateDiscountPermission(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            string role)
        {
            if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
                return;

            using (var command = new SQLiteCommand(
                @"SELECT COUNT(*)
                  FROM RolePermissions rp
                  INNER JOIN Roles r ON r.Id = rp.RoleId
                  INNER JOIN Permissions p ON p.Id = rp.PermissionId
                  WHERE r.RoleCode = @role
                    AND p.PermissionCode = 'Sales.Discount'
                    AND rp.IsAllowed = 1;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@role", role);

                if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية منح خصم على فاتورة البيع.");
            }
        }

        private void ValidateCustomer(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int customerId)
        {
            using (var command = new SQLiteCommand(
                @"SELECT COUNT(*) FROM Customers
                  WHERE Id = @id AND IsActive = 1;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@id", customerId);

                if (Convert.ToInt32(command.ExecuteScalar()) != 1)
                    throw new InvalidOperationException(
                        "العميل المحدد غير موجود أو غير نشط.");
            }
        }

        private void ValidatePaymentMethod(string paymentMethod)
        {
            if (!string.Equals(paymentMethod, "Cash", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(paymentMethod, "Card", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(paymentMethod, "Transfer", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "يجب اختيار طريقة دفع صحيحة للمبلغ المدفوع.");
        }

        public SaleInvoiceDetails GetInvoiceByNumber(
            int userId,
            string invoiceNumber)
        {
            if (string.IsNullOrWhiteSpace(invoiceNumber))
                throw new ArgumentException("رقم الفاتورة غير صحيح.", "invoiceNumber");

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetUserRoleForViewing(connection, userId);
                ValidateSalesViewPermission(connection, role);

                var details = new SaleInvoiceDetails();

                using (var command = new SQLiteCommand(
                    @"SELECT
                          si.Id, si.InvoiceNumber, si.Total, si.Status,
                          si.CreatedAt, si.Subtotal, si.DiscountType,
                          si.DiscountValue, si.DiscountAmount, si.NetTotal,
                          si.PaidAmount, si.RemainingAmount, si.PaymentStatus,
                          u.FullName, COALESCE(c.Name, '') AS CustomerName
                      FROM SalesInvoices si
                      INNER JOIN Users u ON u.Id = si.UserId
                      LEFT JOIN Customers c ON c.Id = si.CustomerId
                      WHERE si.InvoiceNumber = @number
                      LIMIT 1;",
                    connection))
                {
                    command.Parameters.AddWithValue("@number", invoiceNumber);

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;

                        details.Id = Convert.ToInt64(reader["Id"]);
                        details.InvoiceNumber = reader["InvoiceNumber"].ToString();
                        details.Total = ReadDouble(reader["Total"], 0);
                        details.Status = reader["Status"].ToString();
                        details.EmployeeName = reader["FullName"].ToString();
                        details.CustomerName = reader["CustomerName"].ToString();
                        details.Subtotal = ReadDouble(reader["Subtotal"], details.Total);
                        details.DiscountType =
                            reader["DiscountType"] == DBNull.Value
                                ? string.Empty
                                : reader["DiscountType"].ToString();
                        details.DiscountValue = ReadDouble(reader["DiscountValue"], 0);
                        details.DiscountAmount = ReadDouble(reader["DiscountAmount"], 0);
                        details.NetTotal = ReadDouble(reader["NetTotal"], details.Total);
                        details.PaidAmount = ReadDouble(reader["PaidAmount"], 0);
                        details.RemainingAmount = ReadDouble(reader["RemainingAmount"], 0);
                        details.PaymentStatus =
                            reader["PaymentStatus"] == DBNull.Value
                                ? "Legacy"
                                : reader["PaymentStatus"].ToString();
                        details.CreatedAt = ParseDate(reader["CreatedAt"].ToString());
                    }
                }

                // تحميل بنود الفاتورة مع الاحتفاظ بمعرف كل بند
                // لربط التشغيلات المصروفة به.
                var invoiceItemsById =
                    new Dictionary<int, SaleInvoiceItem>();

                using (var command = new SQLiteCommand(
                    @"SELECT Id, ProductId, ItemName, UnitName, Quantity,
                             UnitPrice, Total, PackageQuantity,
                             DiscountPercentage, DiscountAmount
                      FROM SalesInvoiceItems
                      WHERE InvoiceId = @invoiceId
                      ORDER BY Id ASC;",
                    connection))
                {
                    command.Parameters.AddWithValue("@invoiceId", details.Id);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int invoiceItemId =
                                Convert.ToInt32(reader["Id"]);

                            var invoiceItem = new SaleInvoiceItem
                            {
                                InvoiceItemId = invoiceItemId,
                                ProductId = Convert.ToInt32(reader["ProductId"]),
                                ItemName = reader["ItemName"].ToString(),
                                UnitName = reader["UnitName"].ToString(),
                                Quantity = Convert.ToDouble(reader["Quantity"]),
                                UnitPrice = Convert.ToDouble(reader["UnitPrice"]),
                                Total = Convert.ToDouble(reader["Total"]),
                                PackageQuantity = Convert.ToDouble(reader["PackageQuantity"]),
                                DiscountPercentage = ReadDouble(reader["DiscountPercentage"], 0),
                                DiscountAmount = ReadDouble(reader["DiscountAmount"], 0)
                            };

                            details.Items.Add(invoiceItem);
                            invoiceItemsById.Add(invoiceItemId, invoiceItem);
                        }
                    }
                }

                // تحميل التشغيلات والكميات المصروفة لكل بند.
                using (var command = new SQLiteCommand(
                    @"SELECT
                          a.InvoiceItemId,
                          a.BatchId,
                          a.Quantity,
                          COALESCE(b.BatchNumber, '') AS BatchNumber,
                          COALESCE(b.ExpiryDate, '') AS ExpiryDate
                      FROM SalesInvoiceBatchAllocations a
                      INNER JOIN SalesInvoiceItems i
                          ON i.Id = a.InvoiceItemId
                      LEFT JOIN Batches b
                          ON b.Id = a.BatchId
                      WHERE i.InvoiceId = @invoiceId
                      ORDER BY a.Id ASC;",
                    connection))
                {
                    command.Parameters.AddWithValue("@invoiceId", details.Id);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int invoiceItemId =
                                Convert.ToInt32(reader["InvoiceItemId"]);

                            SaleInvoiceItem invoiceItem;

                            if (!invoiceItemsById.TryGetValue(
                                invoiceItemId, out invoiceItem))
                                continue;

                            invoiceItem.BatchAllocations.Add(
                                new SaleInvoiceBatchAllocation
                                {
                                    BatchId = Convert.ToInt32(reader["BatchId"]),
                                    BatchNumber =
                                        reader["BatchNumber"] == DBNull.Value
                                            ? string.Empty
                                            : reader["BatchNumber"].ToString(),
                                    ExpiryDate =
                                        reader["ExpiryDate"] == DBNull.Value
                                            ? string.Empty
                                            : reader["ExpiryDate"].ToString(),
                                    AllocatedQuantity =
                                        ReadDouble(reader["Quantity"], 0)
                                });
                        }
                    }
                }

                using (var command = new SQLiteCommand(
                    @"SELECT Id, PaymentMethod, Amount, ReferenceNumber,
                             Notes, CreatedAt
                      FROM SalesInvoicePayments
                      WHERE InvoiceId = @invoiceId
                      ORDER BY Id ASC;",
                    connection))
                {
                    command.Parameters.AddWithValue("@invoiceId", details.Id);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            details.Payments.Add(new SaleInvoicePaymentDetails
                            {
                                Id = Convert.ToInt64(reader["Id"]),
                                PaymentMethod =
                                    reader["PaymentMethod"] == DBNull.Value
                                        ? string.Empty
                                        : reader["PaymentMethod"].ToString(),
                                Amount = ReadDouble(reader["Amount"], 0),
                                ReferenceNumber =
                                    reader["ReferenceNumber"] == DBNull.Value
                                        ? string.Empty
                                        : reader["ReferenceNumber"].ToString(),
                                Notes =
                                    reader["Notes"] == DBNull.Value
                                        ? string.Empty
                                        : reader["Notes"].ToString(),
                                CreatedAt = ParseDate(reader["CreatedAt"].ToString())
                            });
                        }
                    }
                }

                return details;
            }
        }

        public List<SaleInvoiceDetails> GetInvoices(
            int userId,
            string searchText,
            DateTime? fromDate,
            DateTime? toDate)
        {
            var invoices = new List<SaleInvoiceDetails>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                string role = GetUserRoleForViewing(connection, userId);
                ValidateSalesViewPermission(connection, role);

                string sql = @"
                    SELECT si.Id, si.InvoiceNumber, si.Total,
                           si.Status, si.CreatedAt, u.FullName
                    FROM SalesInvoices si
                    INNER JOIN Users u ON u.Id = si.UserId
                    WHERE 1 = 1 ";

                if (!string.IsNullOrWhiteSpace(searchText))
                    sql += @" AND (
                                si.InvoiceNumber LIKE @search
                                OR u.FullName LIKE @search
                             ) ";

                if (fromDate.HasValue)
                    sql += " AND date(si.CreatedAt) >= date(@fromDate) ";

                if (toDate.HasValue)
                    sql += " AND date(si.CreatedAt) <= date(@toDate) ";

                sql += " ORDER BY si.Id DESC;";

                using (var command = new SQLiteCommand(sql, connection))
                {
                    if (!string.IsNullOrWhiteSpace(searchText))
                        command.Parameters.AddWithValue(
                            "@search", "%" + searchText.Trim() + "%");

                    if (fromDate.HasValue)
                        command.Parameters.AddWithValue(
                            "@fromDate", fromDate.Value.ToString("yyyy-MM-dd"));

                    if (toDate.HasValue)
                        command.Parameters.AddWithValue(
                            "@toDate", toDate.Value.ToString("yyyy-MM-dd"));

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var invoice = new SaleInvoiceDetails();

                            invoice.Id = Convert.ToInt64(reader["Id"]);
                            invoice.InvoiceNumber = reader["InvoiceNumber"].ToString();
                            invoice.Total = Convert.ToDouble(reader["Total"]);
                            invoice.Status = reader["Status"].ToString();
                            invoice.EmployeeName = reader["FullName"].ToString();

                            DateTime parsedDate;
                            string createdAt = reader["CreatedAt"].ToString();

                            if (DateTime.TryParseExact(
                                createdAt, "yyyy-MM-dd HH:mm:ss",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out parsedDate))
                                invoice.CreatedAt = parsedDate;
                            else if (DateTime.TryParse(
                                createdAt, CultureInfo.CurrentCulture,
                                DateTimeStyles.None, out parsedDate))
                                invoice.CreatedAt = parsedDate;

                            invoices.Add(invoice);
                        }
                    }
                }
            }

            return invoices;
        }

        private static double ReadDouble(object value, double defaultValue)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            double result;

            if (double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out result) &&
                !double.IsNaN(result) && !double.IsInfinity(result))
                return result;

            return defaultValue;
        }

        private static DateTime ParseDate(string value)
        {
            DateTime parsedDate;

            if (DateTime.TryParseExact(
                value, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsedDate))
                return parsedDate;

            if (DateTime.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.None,
                out parsedDate))
                return parsedDate;

            return DateTime.MinValue;
        }

        private string GetUserRoleForViewing(
            SQLiteConnection connection,
            int userId)
        {
            using (var command = new SQLiteCommand(
                @"SELECT Role FROM Users
                  WHERE Id = @id AND IsActive = 1;",
                connection))
            {
                command.Parameters.AddWithValue("@id", userId);
                object result = command.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                    throw new UnauthorizedAccessException(
                        "المستخدم غير موجود أو غير نشط.");

                return result.ToString();
            }
        }

        private void ValidateSalesViewPermission(
            SQLiteConnection connection,
            string role)
        {
            if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
                return;

            using (var command = new SQLiteCommand(
                @"SELECT COUNT(*)
                  FROM RolePermissions rp
                  INNER JOIN Roles r ON r.Id = rp.RoleId
                  INNER JOIN Permissions p ON p.Id = rp.PermissionId
                  WHERE r.RoleCode = @role
                    AND p.PermissionCode = 'Sales.View'
                    AND rp.IsAllowed = 1;",
                connection))
            {
                command.Parameters.AddWithValue("@role", role);

                if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية عرض فواتير البيع.");
            }
        }

        private void ValidateUserAndPermission(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            out string username,
            out string role)
        {
            username = null;
            role = null;

            using (var command = new SQLiteCommand(
                @"SELECT Username, Role
                  FROM Users
                  WHERE Id = @id AND IsActive = 1;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@id", userId);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        throw new UnauthorizedAccessException(
                            "المستخدم غير موجود أو غير نشط.");

                    username = reader["Username"].ToString();
                    role = reader["Role"].ToString();
                }
            }

            if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
                return;

            using (var command = new SQLiteCommand(
                @"SELECT COUNT(*)
                  FROM RolePermissions rp
                  INNER JOIN Roles r ON r.Id = rp.RoleId
                  INNER JOIN Permissions p ON p.Id = rp.PermissionId
                  WHERE r.RoleCode = @role
                    AND p.PermissionCode = 'Sales.Create'
                    AND rp.IsAllowed = 1;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@role", role);

                if (Convert.ToInt32(command.ExecuteScalar()) == 0)
                    throw new UnauthorizedAccessException(
                        "ليس لديك صلاحية إنشاء فاتورة بيع.");
            }
        }

        private ProductSaleInfo GetProduct(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int productId)
        {
            using (var command = new SQLiteCommand(
                @"SELECT
                      Id, Name, UnitName, SmallUnitName,
                      UnitsPerPackage, SalePrice, IsArchived,
                      MaxDiscountPercentage
                  FROM Products
                  WHERE Id = @id;",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@id", productId);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return new ProductSaleInfo
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        Name = reader["Name"].ToString(),
                        UnitName = reader["UnitName"].ToString(),
                        SmallUnitName = reader["SmallUnitName"].ToString(),
                        UnitsPerPackage = Convert.ToInt32(reader["UnitsPerPackage"]),
                        SalePrice = Convert.ToDouble(reader["SalePrice"]),
                        IsArchived = Convert.ToInt32(reader["IsArchived"]) == 1,
                        MaxDiscountPercentage =
                            ReadDouble(reader["MaxDiscountPercentage"], 0)
                    };
                }
            }
        }

        private void WriteAuditLog(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int userId,
            string username,
            long invoiceId,
            string invoiceNumber,
            double subtotal,
            double itemDiscount,
            double generalDiscount,
            double totalDiscount,
            double netTotal,
            string createdAt)
        {
            using (var command = new SQLiteCommand(
                @"INSERT INTO AuditLogs
                  (
                      UserId, Username, ActionType, EntityType,
                      EntityId, Details, DeviceName, CreatedAt
                  )
                  VALUES
                  (
                      @userId, @username, 'Create', 'SalesInvoice',
                      @entityId, @details, @device, @createdAt
                  );",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@username", username);
                command.Parameters.AddWithValue("@entityId", invoiceId);

                command.Parameters.AddWithValue(
                    "@details",
                    "إنشاء فاتورة رقم " + invoiceNumber +
                    " | إجمالي الأصناف: " +
                    subtotal.ToString("0.00", CultureInfo.InvariantCulture) +
                    " | خصم البنود: " +
                    itemDiscount.ToString("0.00", CultureInfo.InvariantCulture) +
                    " | الخصم العام الفعلي: " +
                    generalDiscount.ToString("0.00", CultureInfo.InvariantCulture) +
                    " | إجمالي الخصم: " +
                    totalDiscount.ToString("0.00", CultureInfo.InvariantCulture) +
                    " | الصافي: " +
                    netTotal.ToString("0.00", CultureInfo.InvariantCulture));

                command.Parameters.AddWithValue("@device", Environment.MachineName);
                command.Parameters.AddWithValue("@createdAt", createdAt);
                command.ExecuteNonQuery();
            }
        }

        private class ProductSaleInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string UnitName { get; set; }
            public string SmallUnitName { get; set; }
            public int UnitsPerPackage { get; set; }
            public double SalePrice { get; set; }
            public bool IsArchived { get; set; }
            public double MaxDiscountPercentage { get; set; }
        }

        private class PreparedSaleLine
        {
            public ProductSaleInfo Product { get; set; }
            public SaleInvoiceItem OriginalItem { get; set; }
            public string UnitName { get; set; }
            public double Quantity { get; set; }
            public double UnitPrice { get; set; }
            public double PackageQuantity { get; set; }

            public List<SaleInvoiceBatchAllocation> BatchAllocations
            { get; set; }

            public double LineTotal { get; set; }
            public long LineTotalCents { get; set; }
            public long MaxDiscountCents { get; set; }
            public long ItemDiscountCents { get; set; }
            public long GeneralDiscountCapacityCents { get; set; }
            public long GeneralDiscountCents { get; set; }
            public long AllocationWeight { get; set; }
        }

        private class DiscountFraction
        {
            public PreparedSaleLine Line { get; set; }
            public decimal Fraction { get; set; }
            public int OriginalOrder { get; set; }
        }
    }
}
