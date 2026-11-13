
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Ocore.desktop.Data;
using Ocore.desktop.Models;

namespace Ocore.desktop.Services
{
    public class DrugCatalogService
    {
        private readonly string connectionString;

        public DrugCatalogService()
        {
            var database = new DatabaseManager();

            connectionString =
                "Data Source=" + database.GetDatabasePath() + ";Version=3;";
        }

        public List<DrugCatalogItem> SearchDrugs(string searchText)
        {
            var drugs = new List<DrugCatalogItem>();

            if (string.IsNullOrWhiteSpace(searchText))
                return drugs;

            string sql = @"
SELECT
    Id,
    RegistrationNumber,
    TradeName,
    TradeNameArabic,
    GenericName,
    Strength,
    DosageForm,
    RouteOfAdministration,
    Manufacturer,
    MarketingAuthorizationHolder,
    DrugClass,
    PackageDescription,
    ReferencePrice,
    SourceStatus
FROM DrugCatalog
WHERE IsActive = 1
AND (
    TradeName LIKE @Search
    OR TradeNameArabic LIKE @Search
    OR GenericName LIKE @Search
    OR Manufacturer LIKE @Search
    OR Strength LIKE @Search
    OR DosageForm LIKE @Search
)
ORDER BY
    CASE
        WHEN TradeNameArabic = @ExactSearch THEN 0
        WHEN TradeName = @ExactSearch THEN 1
        WHEN GenericName = @ExactSearch THEN 2
        ELSE 3
    END,
    TradeNameArabic,
    TradeName
LIMIT 100;";

            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(sql, connection))
            {
                string search = searchText.Trim();

                command.Parameters.AddWithValue("@Search", "%" + search + "%");
                command.Parameters.AddWithValue("@ExactSearch", search);

                connection.Open();

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        drugs.Add(new DrugCatalogItem
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            RegistrationNumber = reader["RegistrationNumber"].ToString(),
                            TradeName = reader["TradeName"].ToString(),
                            TradeNameArabic = reader["TradeNameArabic"].ToString(),
                            GenericName = reader["GenericName"].ToString(),
                            Strength = reader["Strength"].ToString(),
                            DosageForm = reader["DosageForm"].ToString(),
                            RouteOfAdministration = reader["RouteOfAdministration"].ToString(),
                            Manufacturer = reader["Manufacturer"].ToString(),
                            MarketingAuthorizationHolder = reader["MarketingAuthorizationHolder"].ToString(),
                            DrugClass = reader["DrugClass"].ToString(),
                            PackageDescription = reader["PackageDescription"].ToString(),
                            ReferencePrice = Convert.ToDouble(reader["ReferencePrice"]),
                            SourceStatus = reader["SourceStatus"].ToString()
                        });
                    }
                }
            }

            return drugs;
        }
    }
}
