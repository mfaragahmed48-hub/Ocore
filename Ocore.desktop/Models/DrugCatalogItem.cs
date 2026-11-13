
namespace Ocore.desktop.Models
{
    public class DrugCatalogItem
    {
        public int Id { get; set; }

        public string RegistrationNumber { get; set; }

        public string TradeName { get; set; }

        public string TradeNameArabic { get; set; }

        public string GenericName { get; set; }

        public string Strength { get; set; }

        public string DosageForm { get; set; }

        public string RouteOfAdministration { get; set; }

        public string Manufacturer { get; set; }

        public string MarketingAuthorizationHolder { get; set; }

        public string DrugClass { get; set; }

        public string PackageDescription { get; set; }

        public double ReferencePrice { get; set; }

        public string SourceStatus { get; set; }

        public string DisplayName
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(TradeNameArabic)
                    ? TradeName
                    : TradeNameArabic + " - " + TradeName;

                if (!string.IsNullOrWhiteSpace(Strength))
                    name += " - " + Strength;

                if (!string.IsNullOrWhiteSpace(DosageForm))
                    name += " - " + DosageForm;

                return name;
            }
        }
    }
}
