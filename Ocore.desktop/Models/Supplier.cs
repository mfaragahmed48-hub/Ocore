
namespace Ocore.desktop.Models
{
    public class Supplier
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Phone { get; set; }

        public string Address { get; set; }

        public bool IsActive { get; set; }

        public string StatusText
        {
            get
            {
                return IsActive ? "نشط" : "موقوف";
            }
        }

        public Supplier()
        {
            Name = string.Empty;
            Phone = string.Empty;
            Address = string.Empty;
            IsActive = true;
        }
    }
}
