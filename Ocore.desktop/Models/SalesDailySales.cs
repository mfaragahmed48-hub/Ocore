namespace Ocore.desktop.Models
{
    public class SalesDailySales
    {
        public string SaleDate { get; set; }
        public double TotalSales { get; set; }

        public SalesDailySales()
        {
            SaleDate = string.Empty;
            TotalSales = 0;
        }
    }
}