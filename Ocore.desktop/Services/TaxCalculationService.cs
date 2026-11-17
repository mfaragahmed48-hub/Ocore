
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
            {
                throw new InvalidOperationException(
                    "الصنف غير مرتبط بتصنيف ضريبي.");
            }

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

            decimal amount;

            try
            {
                amount = Convert.ToDecimal(
                    amountAfterDiscount,
                    CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذر تحويل قيمة الصنف إلى قيمة مالية صحيحة.",
                    ex);
            }

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

                    profileCode = Convert.ToString(
                        reader["Code"]);

                    profileName = Convert.ToString(
                        reader["Name"]);

                    taxType = Convert.ToString(
                        reader["TaxType"]);

                    profileIsActive =
                        Convert.ToInt32(reader["IsActive"]) == 1;
                }
            }

            profileCode = (profileCode ?? "").Trim();
            profileName = (profileName ?? "").Trim();
            taxType = (taxType ?? "").Trim();

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

            bool isExempt = string.Equals(
                taxType,
                "Exempt",
                StringComparison.OrdinalIgnoreCase);

            bool isStandard = string.Equals(
                taxType,
                "Standard",
                StringComparison.OrdinalIgnoreCase);

            bool isReduced = string.Equals(
                taxType,
                "Reduced",
                StringComparison.OrdinalIgnoreCase);

            bool isSchedule = string.Equals(
                taxType,
                "Schedule",
                StringComparison.OrdinalIgnoreCase);

            if (!isExempt &&
                !isStandard &&
                !isReduced &&
                !isSchedule)
            {
                throw new InvalidOperationException(
                    "نوع التصنيف الضريبي غير معروف.");
            }

            var rates = new List<TaxCalculationRate>();

            if (!isExempt)
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
                                Convert.ToString(
                                    reader["ComponentType"]);

                            componentType =
                                (componentType ?? "").Trim();

                            if (!string.Equals(
                                    componentType,
                                    "VAT",
                                    StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(
                                    componentType,
                                    "Schedule",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException(
                                    "يوجد مكون ضريبي بنوع غير صحيح.");
                            }

                            if (string.Equals(
                                componentType,
                                "VAT",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                componentType = "VAT";
                            }
                            else
                            {
                                componentType = "Schedule";
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

                                IsCompound =
                                    Convert.ToInt32(
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

            if (isExempt || amount == 0)
            {
                result.TaxableAmount = amount;
                return result;
            }

            decimal taxableBase = amount;

            // استخراج الوعاء الضريبي من السعر الشامل.
            // يتم استخدام المعاملات الدقيقة أثناء استخراج الوعاء.
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

                taxableBase = RoundMoney(
                    amount / totalFactor);
            }

            // حساب مكونات الضريبة بالتتابع.
            // الضريبة المركبة تعتمد على الضرائب السابقة بعد التقريب.
            decimal accumulatedTax = 0m;

            foreach (var rate in rates)
            {
                decimal componentBase =
                    rate.IsCompound
                        ? taxableBase + accumulatedTax
                        : taxableBase;

                componentBase = RoundMoney(componentBase);

                decimal rawTaxAmount =
                    componentBase *
                    rate.RatePercent / 100m;

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

                        TaxableBase = componentBase,

                        TaxAmount = componentTaxAmount
                    });

                accumulatedTax += componentTaxAmount;

                result.TaxRate += rate.RatePercent;
            }

            // السعر الشامل يجب أن يظل مساويًا للمبلغ الأصلي.
            // تتم معالجة فرق التقريب في آخر مكون ضريبي.
            if (isInclusive && result.Components.Count > 0)
            {
                decimal calculatedTax = 0m;

                foreach (var component in result.Components)
                {
                    calculatedTax += component.TaxAmount;
                }

                decimal expectedTax = RoundMoney(
                    amount - taxableBase);

                decimal roundingDifference = RoundMoney(
                    expectedTax - calculatedTax);

                if (roundingDifference != 0m)
                {
                    int lastIndex =
                        result.Components.Count - 1;

                    var lastComponent =
                        result.Components[lastIndex];

                    decimal adjustedTaxAmount = RoundMoney(
                        lastComponent.TaxAmount +
                        roundingDifference);

                    if (adjustedTaxAmount < 0)
                    {
                        throw new InvalidOperationException(
                            "تعذر تسوية فروق تقريب مكونات الضريبة.");
                    }

                    lastComponent.TaxAmount =
                        adjustedTaxAmount;
                }
            }

            // إعادة تجميع الضرائب من القيم النهائية المحفوظة.
            decimal vatTotal = 0m;
            decimal scheduleTotal = 0m;

            foreach (var component in result.Components)
            {
                if (component.ComponentType == "VAT")
                {
                    vatTotal += component.TaxAmount;
                }
                else if (component.ComponentType == "Schedule")
                {
                    scheduleTotal += component.TaxAmount;
                }
            }

            result.VatAmount = RoundMoney(vatTotal);

            result.ScheduleTaxAmount = RoundMoney(
                scheduleTotal);

            result.TaxTotal = RoundMoney(
                result.VatAmount +
                result.ScheduleTaxAmount);

            result.TaxableAmount = taxableBase;

            if (result.TaxableAmount < 0 ||
                result.TaxTotal < 0)
            {
                throw new InvalidOperationException(
                    "نتيجة حساب الوعاء الضريبي غير صحيحة.");
            }

            if (isInclusive)
            {
                decimal reconstructedAmount = RoundMoney(
                    result.TaxableAmount +
                    result.TaxTotal);

                if (reconstructedAmount != amount)
                {
                    throw new InvalidOperationException(
                        "تعذر مطابقة إجمالي السعر الشامل مع مكونات الضريبة.");
                }
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
        {
            get;
            set;
        }

        public TaxCalculationResult()
        {
            TaxProfileCode = string.Empty;

            TaxProfileName = string.Empty;

            TaxType = string.Empty;

            TaxPriceMode = "Exclusive";

            Components =
                new List<TaxComponentCalculation>();
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
