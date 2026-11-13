
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ocore.desktop.Models
{
    public class PurchaseInvoiceItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private int productId;
        private int? drugCatalogId;
        private string productCode;
        private string itemName;
        private string manufacturer;

        private string unitName;
        private string packageUnitName;
        private string smallUnitName;
        private int unitsPerPackage = 1;

        private string batchNumber;
        private string expiryDateText;
        private DateTime? expiryDate;

        private double quantity;
        private double freeQuantity;
        private double unitPrice;
        private double discountPercent;
        private double taxPercent;
        private string notes;

        public int ProductId
        {
            get => productId;
            set => SetProperty(ref productId, value);
        }

        public int? DrugCatalogId
        {
            get => drugCatalogId;
            set => SetProperty(ref drugCatalogId, value);
        }

        public string ProductCode
        {
            get => productCode;
            set => SetProperty(ref productCode, value);
        }

        public string ItemName
        {
            get => itemName;
            set => SetProperty(ref itemName, value);
        }

        public string Manufacturer
        {
            get => manufacturer;
            set => SetProperty(ref manufacturer, value);
        }

        public string UnitName
        {
            get => unitName;
            set
            {
                if (SetProperty(ref unitName, value))
                    NotifyCalculatedProperties();
            }
        }

        public string PackageUnitName
        {
            get => packageUnitName;
            set
            {
                if (SetProperty(ref packageUnitName, value))
                    OnPropertyChanged(nameof(AvailableUnits));
            }
        }

        public string SmallUnitName
        {
            get => smallUnitName;
            set
            {
                if (SetProperty(ref smallUnitName, value))
                {
                    OnPropertyChanged(nameof(AvailableUnits));
                    NotifyCalculatedProperties();
                }
            }
        }

        public int UnitsPerPackage
        {
            get => unitsPerPackage;
            set
            {
                if (SetProperty(ref unitsPerPackage, value))
                    NotifyCalculatedProperties();
            }
        }

        public List<string> AvailableUnits
        {
            get
            {
                var units = new List<string>();

                if (!string.IsNullOrWhiteSpace(PackageUnitName))
                    units.Add(PackageUnitName.Trim());

                if (!string.IsNullOrWhiteSpace(SmallUnitName))
                {
                    string small = SmallUnitName.Trim();

                    bool alreadyExists = units.Exists(
                        unit => string.Equals(
                            unit,
                            small,
                            StringComparison.OrdinalIgnoreCase));

                    if (!alreadyExists)
                        units.Add(small);
                }

                return units;
            }
        }

        public string BatchNumber
        {
            get => batchNumber;
            set => SetProperty(ref batchNumber, value);
        }

        public string ExpiryDateText
        {
            get => expiryDateText;
            set
            {
                if (SetProperty(ref expiryDateText, value))
                {
                    DateTime parsedDate;

                    if (DateTime.TryParse(
                        value,
                        out parsedDate))
                    {
                        if (expiryDate?.Date != parsedDate.Date)
                        {
                            expiryDate = parsedDate.Date;
                            OnPropertyChanged(nameof(ExpiryDate));
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(value)
                             && expiryDate.HasValue)
                    {
                        expiryDate = null;
                        OnPropertyChanged(nameof(ExpiryDate));
                    }
                }
            }
        }

        public DateTime? ExpiryDate
        {
            get => expiryDate;
            set
            {
                DateTime? normalizedValue =
                    value.HasValue
                        ? value.Value.Date
                        : (DateTime?)null;

                if (SetProperty(ref expiryDate, normalizedValue))
                {
                    string newText = normalizedValue.HasValue
                        ? normalizedValue.Value.ToString("yyyy-MM-dd")
                        : "";

                    SetProperty(
                        ref expiryDateText,
                        newText,
                        nameof(ExpiryDateText));
                }
            }
        }

        public double Quantity
        {
            get => quantity;
            set
            {
                if (SetProperty(ref quantity, value))
                    NotifyCalculatedProperties();
            }
        }

        public double FreeQuantity
        {
            get => freeQuantity;
            set => SetProperty(ref freeQuantity, value);
        }

        public double UnitPrice
        {
            get => unitPrice;
            set
            {
                if (SetProperty(ref unitPrice, value))
                    NotifyCalculatedProperties();
            }
        }

        public double DiscountPercent
        {
            get => discountPercent;
            set
            {
                if (SetProperty(ref discountPercent, value))
                    NotifyCalculatedProperties();
            }
        }

        public double DiscountAmount
        {
            get
            {
                return RoundMoney(
                    UnitPrice * DiscountPercent / 100.0);
            }
        }

        public double DiscountedUnitPrice
        {
            get
            {
                return RoundMoney(
                    UnitPrice - DiscountAmount);
            }
        }

        public double TaxPercent
        {
            get => taxPercent;
            set
            {
                if (SetProperty(ref taxPercent, value))
                    NotifyCalculatedProperties();
            }
        }

        public double TaxAmount
        {
            get
            {
                return RoundMoney(
                    DiscountedUnitPrice * TaxPercent / 100.0);
            }
        }

        public double NetUnitPrice
        {
            get
            {
                return RoundMoney(
                    DiscountedUnitPrice + TaxAmount);
            }
        }

        public string Notes
        {
            get => notes;
            set => SetProperty(ref notes, value);
        }

        public double PackageQuantity
        {
            get
            {
                if (IsSmallUnit())
                {
                    if (UnitsPerPackage <= 0)
                        return 0;

                    return Math.Round(
                        Quantity / UnitsPerPackage,
                        8,
                        MidpointRounding.AwayFromZero);
                }

                return Quantity;
            }
        }

        public double PackageUnitPrice
        {
            get
            {
                if (IsSmallUnit())
                {
                    if (UnitsPerPackage <= 0)
                        return 0;

                    return RoundMoney(
                        UnitPrice * UnitsPerPackage);
                }

                return UnitPrice;
            }
        }

        public double Total
        {
            get
            {
                return RoundMoney(
                    Quantity * NetUnitPrice);
            }
        }

        private bool IsSmallUnit()
        {
            return !string.IsNullOrWhiteSpace(UnitName)
                && !string.IsNullOrWhiteSpace(SmallUnitName)
                && string.Equals(
                    UnitName.Trim(),
                    SmallUnitName.Trim(),
                    StringComparison.OrdinalIgnoreCase);
        }

        private void NotifyCalculatedProperties()
        {
            OnPropertyChanged(nameof(DiscountAmount));
            OnPropertyChanged(nameof(DiscountedUnitPrice));
            OnPropertyChanged(nameof(TaxAmount));
            OnPropertyChanged(nameof(NetUnitPrice));
            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(PackageQuantity));
            OnPropertyChanged(nameof(PackageUnitPrice));
        }

        private bool SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);

            return true;
        }

        private void OnPropertyChanged(
            [CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

        private static double RoundMoney(double value)
        {
            return Math.Round(
                value,
                2,
                MidpointRounding.AwayFromZero);
        }
    }
}
