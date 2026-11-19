
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ocore.desktop.Services
{
    public class GS1BarcodeData
    {
        public string GTIN { get; set; }
        public string BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsGS1 { get; set; }
    }

    public static class GS1BarcodeService
    {
        private const char GroupSeparator = (char)29;

        public static GS1BarcodeData Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("الباركود فارغ.");

            string value = input.Trim();

            // بادئة بعض قارئات GS1 DataMatrix
            if (value.StartsWith("]d2", StringComparison.Ordinal))
                value = value.Substring(3);

            if (value.Contains("(01)"))
                return ParseHumanReadable(value);

            if (IsDigits(value) &&
                (value.Length == 12 ||
                 value.Length == 13 ||
                 value.Length == 14))
            {
                return new GS1BarcodeData
                {
                    GTIN = NormalizeGTIN(value),
                    IsGS1 = false
                };
            }

            return ParseRawGS1(value);
        }

        private static GS1BarcodeData ParseHumanReadable(
            string value)
        {
            var result = new GS1BarcodeData
            {
                IsGS1 = true
            };

            var matches = Regex.Matches(
                value,
                @"\((01|10|17|21)\)");

            for (int i = 0; i < matches.Count; i++)
            {
                Match match = matches[i];

                int start = match.Index + match.Length;

                int end = i + 1 < matches.Count
                    ? matches[i + 1].Index
                    : value.Length;

                string data = value.Substring(
                    start,
                    end - start).Trim();

                switch (match.Groups[1].Value)
                {
                    case "01":
                        if (data.Length != 14 || !IsDigits(data))
                            throw new FormatException("رقم GTIN غير صحيح.");

                        result.GTIN = NormalizeGTIN(data);
                        break;

                    case "10":
                        if (data.Length > 20)
                            throw new FormatException("رقم التشغيلة أطول من المسموح.");

                        result.BatchNumber = data;
                        break;

                    case "17":
                        result.ExpiryDate = ParseExpiryDate(data);
                        break;
                }
            }

            ValidateResult(result);
            return result;
        }

        private static GS1BarcodeData ParseRawGS1(string value)
        {
            var result = new GS1BarcodeData
            {
                IsGS1 = true
            };

            int position = 0;

            while (position < value.Length)
            {
                if (value[position] == GroupSeparator)
                {
                    position++;
                    continue;
                }

                if (position + 2 > value.Length)
                    throw new FormatException("بيانات GS1 غير مكتملة.");

                string ai = value.Substring(position, 2);
                position += 2;

                switch (ai)
                {
                    case "01":
                        if (position + 14 > value.Length)
                            throw new FormatException("بيانات GTIN غير مكتملة.");

                        string gtin = value.Substring(position, 14);
                        position += 14;

                        if (!IsDigits(gtin))
                            throw new FormatException("رقم GTIN غير صحيح.");

                        result.GTIN = NormalizeGTIN(gtin);
                        break;

                    case "17":
                        if (position + 6 > value.Length)
                            throw new FormatException("تاريخ الصلاحية غير مكتمل.");

                        string expiry = value.Substring(position, 6);
                        position += 6;

                        result.ExpiryDate = ParseExpiryDate(expiry);
                        break;

                    case "10":
                        string batch = ReadVariableField(value, ref position);

                        if (batch.Length > 20)
                            throw new FormatException("رقم التشغيلة أطول من المسموح.");

                        result.BatchNumber = batch;
                        break;

                    case "21":
                        // الرقم التسلسلي لا نحتاج تخزينه حاليًا.
                        ReadVariableField(value, ref position);
                        break;

                    default:
                        throw new FormatException(
                            "رمز GS1 غير مدعوم أو بيانات الباركود غير صحيحة: " + ai);
                }
            }

            ValidateResult(result);
            return result;
        }

        private static string ReadVariableField(
            string value,
            ref int position)
        {
            int separatorIndex = value.IndexOf(
                GroupSeparator,
                position);

            int end = separatorIndex >= 0
                ? separatorIndex
                : value.Length;

            string data = value.Substring(
                position,
                end - position);

            position = end;

            if (position < value.Length &&
                value[position] == GroupSeparator)
            {
                position++;
            }

            return data;
        }

        private static DateTime ParseExpiryDate(string value)
        {
            if (value == null ||
                value.Length != 6 ||
                !IsDigits(value))
            {
                throw new FormatException("تاريخ الصلاحية في الباركود غير صحيح.");
            }

            int year = 2000 + int.Parse(
                value.Substring(0, 2),
                CultureInfo.InvariantCulture);

            int month = int.Parse(
                value.Substring(2, 2),
                CultureInfo.InvariantCulture);

            int day = int.Parse(
                value.Substring(4, 2),
                CultureInfo.InvariantCulture);

            if (month < 1 || month > 12)
                throw new FormatException("شهر الصلاحية غير صحيح.");

            // اليوم 00 في GS1 يعني آخر يوم في الشهر.
            if (day == 0)
            {
                return new DateTime(year, month, 1)
                    .AddMonths(1)
                    .AddDays(-1);
            }

            return new DateTime(year, month, day);
        }


        public static string NormalizeGTIN(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !IsDigits(value))
            {
                throw new FormatException(
                    "رقم الباركود يجب أن يكون أرقامًا فقط.");
            }

            if (value.Length != 12 &&
                value.Length != 13 &&
                value.Length != 14)
            {
                throw new FormatException(
                    "طول رقم الباركود غير مدعوم.");
            }

            int sum = 0;
            int weight = 3;

            // حساب رقم التحقق من اليمين إلى اليسار
            // مع استبعاد آخر رقم (Check Digit).
            for (int i = value.Length - 2; i >= 0; i--)
            {
                int digit = value[i] - '0';

                sum += digit * weight;

                weight = weight == 3 ? 1 : 3;
            }

            int expectedCheckDigit =
                (10 - (sum % 10)) % 10;

            int actualCheckDigit =
                value[value.Length - 1] - '0';

            if (expectedCheckDigit != actualCheckDigit)
            {
                throw new FormatException(
                    "رقم الباركود غير صحيح: رقم التحقق Check Digit غير مطابق.");
            }

            if (value.Length == 12 || value.Length == 13)
                return value.PadLeft(14, '0');

            return value;
        }

        private static void ValidateResult(GS1BarcodeData result)
        {
            if (string.IsNullOrWhiteSpace(result.GTIN))
                throw new FormatException("الباركود لا يحتوي على رقم GTIN.");

            if (result.BatchNumber != null &&
                result.BatchNumber.Length == 0)
            {
                throw new FormatException("رقم التشغيلة فارغ.");
            }
        }

        private static bool IsDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (char c in value)
            {
                if (c < '0' || c > '9')
                    return false;
            }

            return true;
        }
    }
}
