
using System;

namespace Ocore.desktop.Models
{
    public class User
    {
        public int Id { get; set; }

        public string Username { get; set; }

        public string FullName { get; set; }

        public string Role { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public string DisplayName
        {
            get
            {
                return string.IsNullOrWhiteSpace(FullName)
                    ? Username
                    : FullName;
            }
        }
        public string StatusText { get; set; }
    }
}
