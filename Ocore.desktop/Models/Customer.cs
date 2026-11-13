using System;

namespace Ocore.desktop.Models
{
    public class Customer
    {
        public int Id { get; set; }

        public string CustomerCode { get; set; }

        public string Name { get; set; }

        public string Phone { get; set; }

        // حقل العنوان القديم محفوظ للتوافق مع الأجزاء الأخرى من البرنامج.
        public string Address { get; set; }

        public string HouseNumber { get; set; }

        public string Street { get; set; }

        public string Area { get; set; }

        public string Floor { get; set; }

        public string Apartment { get; set; }

        public string Notes { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public int? CreatedBy { get; set; }

        public Customer()
        {
            CustomerCode = string.Empty;
            Name = string.Empty;
            Phone = string.Empty;
            Address = string.Empty;
            HouseNumber = string.Empty;
            Street = string.Empty;
            Area = string.Empty;
            Floor = string.Empty;
            Apartment = string.Empty;
            Notes = string.Empty;
            IsActive = true;
            CreatedAt = DateTime.Now;
        }
    }
}