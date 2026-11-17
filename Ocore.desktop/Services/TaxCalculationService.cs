
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace Ocore.desktop.Services
{
    public class TaxCalculationService
    {
        public TaxCalculationResult CalculateLine(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            int? taxProfileId,
            string priceMode,
            double amountAfterDiscount,
            DateTime taxDate)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            if (taxProfileId == null || taxProfileId.Value <= 0)
                throw new InvalidOperationException(
                    "الصنف غير مرتبط بتصنيف ضريبي.");

            if (double.IsNaN(amountAfterDiscount) ||
                double.IsInfinity(amountAfterDiscount) ||
                amountAfterDiscount < 0)
            {
                throw new InvalidOperationException(
                    "قيمة الصنف بعد الخصم غير صحيحة.");
            }

            priceMode = (priceMode ?? "").Trim();

            bool isInclusive = string.Equals(
                priceMode,
                "Inclusive",
                StringComparison.OrdinalIgnoreCase);

            if (!isInclusive &&
                !string.Equals(
                    priceMode,
                    "Exclusive",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "طريقة احتساب الضريبة للصنف غير صحيحة.");
            }

            decimal amount = Convert.ToDecimal(
                amountAfterDiscount,
                CultureInfo.InvariantCulture);

            amount = RoundMoney(amount);

            string profileCode;
            string profileName;
            string taxType;
            bool profileIsActive;

            using (var command = new SQLiteCommand(
                @"SELECT
                      Code,
                      Name,
                      TaxType,
                      IsActive
                  FROM TaxProfiles
                  WHERE Id = @Id
                  LIMIT 1;",
                connection,
                transaction))
            {
                command.Parameters.AddWithValue(
                    "@Id",
                    taxProfileId.Value);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        throw new InvalidOperationException(
                            "التصنيف الضريبي المرتبط بالصنف غير موجود.");
                    }

                    profileCode = Convert.ToString(reader["Code"]);
                    profileName = Convert.ToString(reader["Name"]);
                    taxType = Convert.ToString(reader["TaxType"]);

                    profileIsActive =
                        Convert.ToInt32(reader["IsActive"]) == 1;
                }
            }

            if (!profileIsActive)
            {
                throw new InvalidOperationException(
                    "التصنيف الضريبي " + profileName +
                    " غير نشط ولا يمكن استخدامه في البيع.");
            }

            if (string.Equals(
                profileCode,
                "UNCLASSIFIED",
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    taxType,
                    "Unclassified",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "الصنف " + profileName +
                    " غير مصنف ضريبيًا. " +
                    "يجب تحديد معاملته الضريبية قبل البيع.");
            }

            if (taxType != "Exempt" &&
                taxType != "Standard" &&
                taxType != "Reduced" &&
                taxType != "Schedule")
            {
                throw new InvalidOperationException(
                    "نوع التصنيف الضريبي غير معروف.");
            }

            var rates = new List<TaxCalculationRate>();

            if (taxType != "Exempt")
            {
                string effectiveDate = taxDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

                using (var command = new SQLiteCommand(
                    @"SELECT
                          Id,
                          ComponentCode,
                          ComponentName,
                          ComponentType,
                          RatePercent,
                          CalculationOrder,
                          IsCompound
                      FROM TaxProfileRates
                      WHERE TaxProfileId = @ProfileId
                        AND IsActive = 1
                        AND EffectiveFrom <= @EffectiveDate
                        AND (
                            EffectiveTo IS NULL
                            OR EffectiveTo = ''
                            OR EffectiveTo >= @EffectiveDate
                        )
                      ORDER BY CalculationOrder, Id;",
                    connection,
                    transaction))
                {
                    command.Parameters.AddWithValue(
                        "@ProfileId",
                        taxProfileId.Value);

                    command.Parameters.AddWithValue(
                        "@EffectiveDate",
                        effectiveDate);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string componentType =
                                Convert.ToString(reader["ComponentType"]);

                            if (componentType != "VAT" &&
                                componentType != "Schedule")
                            {
                                throw new InvalidOperationException(
                                    "يوجد مكون ضريبي بنوع غير صحيح.");
                            }

                            decimal rate = Convert.ToDecimal(
                                reader["RatePercent"],
                                CultureInfo.InvariantCulture);

                            if (rate < 0 || rate > 100000)
                            {
                                throw new InvalidOperationException(
                                    "توجد نسبة ضريبية غير صحيحة في التصنيف " +
                                    profileName + ".");
                            }

                            rates.Add(new TaxCalculationRate
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                ComponentCode = Convert.ToString(
                                    reader["ComponentCode"]),
                                ComponentName = Convert.ToString(
                                    reader["ComponentName"]),
                                ComponentType = componentType,
                                RatePercent = rate,
                                CalculationOrder = Convert.ToInt32(
                                    reader["CalculationOrder"]),
                                IsCompound = Convert.ToInt32(
                                    reader["IsCompound"]) == 1
                            });
                        }
                    }
                }

                if (rates.Count == 0)
                {
                    throw new InvalidOperationException(
                        "لا توجد نسبة ضريبية سارية بتاريخ " +
                        taxDate.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture) +
                        " للتصنيف " + profileName + ".");
                }
            }

            var result = new TaxCalculationResult
            {
                TaxProfileId = taxProfileId.Value,
                TaxProfileCode = profileCode,
                TaxProfileName = profileName,
                TaxType = taxType,
                TaxPriceMode = isInclusive
                    ? "Inclusive"
                    : "Exclusive",
                TaxableAmount = amount
            };

            if (taxType == "Exempt" || amount == 0)
            {
                result.TaxableAmount = amount;
                return result;
            }

            decimal taxableBase = amount;

            // استخراج الوعاء الضريبي من السعر الشامل.
            // يتم حساب معامل الضريبة المركبة دون تقريب وسيط.
            if (isInclusive)
            {
                decimal accumulatedTaxFactor = 0m;

                foreach (var rate in rates)
                {
                    decimal componentBaseFactor =
                        rate.IsCompound
                            ? 1m + accumulatedTaxFactor
                            : 1m;

                    decimal componentTaxFactor =
                        componentBaseFactor *
                        rate.RatePercent / 100m;

                    accumulatedTaxFactor += componentTaxFactor;
                }

                decimal totalFactor =
                    1m + accumulatedTaxFactor;

                if (totalFactor <= 0)
                {
                    throw new InvalidOperationException(
                        "تعذر استخراج الوعاء الضريبي من السعر الشامل.");
                }

                taxableBase = amount / totalFactor;
            }

            decimal vatTotal = 0m;
            decimal scheduleTotal = 0m;
            decimal accumulatedTax = 0m;

            foreach (var rate in rates)
            {
                decimal componentBase =
                    rate.IsCompound
                        ? taxableBase + accumulatedTax
                        : taxableBase;

                decimal rawTaxAmount =
                    componentBase * rate.RatePercent / 100m;

                decimal componentTaxAmount =
                    RoundMoney(rawTaxAmount);

                result.Components.Add(
                    new TaxComponentCalculation
                    {
                        RateId = rate.Id,
                        ComponentCode = rate.ComponentCode,
                        ComponentName = rate.ComponentName,
                        ComponentType = rate.ComponentType,
                        RatePercent = rate.RatePercent,
                        CalculationOrder = rate.CalculationOrder,
                        IsCompound = rate.IsCompound,
                        TaxableBase = RoundMoney(componentBase),
                        TaxAmount = componentTaxAmount
                    });

                accumulatedTax += rawTaxAmount;

                if (rate.ComponentType == "VAT")
                    vatTotal += componentTaxAmount;
                else
                    scheduleTotal += componentTaxAmount;

                result.TaxRate += rate.RatePercent;
            }

            result.VatAmount = RoundMoney(vatTotal);
            result.ScheduleTaxAmount = RoundMoney(scheduleTotal);

            result.TaxTotal = RoundMoney(
                result.VatAmount +
                result.ScheduleTaxAmount);

            if (isInclusive)
            {
                // السعر الأصلي شامل الضريبة؛ لا نضيف الضريبة مرة أخرى.
                result.TaxableAmount = RoundMoney(
                    amount - result.TaxTotal);
            }
            else
            {
                result.TaxableAmount = RoundMoney(taxableBase);
            }

            if (result.TaxableAmount < 0)
            {
                throw new InvalidOperationException(
                    "نتيجة حساب الوعاء الضريبي غير صحيحة.");
            }

            return result;
        }

        private decimal RoundMoney(decimal value)
        {
            return decimal.Round(
                value,
                2,
                MidpointRounding.AwayFromZero);
        }
    }

    public class TaxCalculationResult
    {
        public int TaxProfileId { get; set; }

        public string TaxProfileCode { get; set; }

        public string TaxProfileName { get; set; }

        public string TaxType { get; set; }

        public string TaxPriceMode { get; set; }

        public decimal TaxRate { get; set; }

        public decimal TaxableAmount { get; set; }

        public decimal VatAmount { get; set; }

        public decimal ScheduleTaxAmount { get; set; }

        public decimal TaxTotal { get; set; }

        public List<TaxComponentCalculation> Components
        { get; set; }

        public TaxCalculationResult()
        {
            TaxProfileCode = string.Empty;
            TaxProfileName = string.Empty;
            TaxType = string.Empty;
            TaxPriceMode = "Exclusive";
            Components = new List<TaxComponentCalculation>();
        }
    }

    public class TaxComponentCalculation
    {
        public int RateId { get; set; }

        public string ComponentCode { get; set; }

        public string ComponentName { get; set; }

        public string ComponentType { get; set; }

        public decimal RatePercent { get; set; }

        public int CalculationOrder { get; set; }

        public bool IsCompound { get; set; }

        public decimal TaxableBase { get; set; }

        public decimal TaxAmount { get; set; }
    }

    internal class TaxCalculationRate
    {
        public int Id { get; set; }

        public string ComponentCode { get; set; }

        public string ComponentName { get; set; }

        public string ComponentType { get; set; }

        public decimal RatePercent { get; set; }

        public int CalculationOrder { get; set; }

        public bool IsCompound { get; set; }
    }
}
