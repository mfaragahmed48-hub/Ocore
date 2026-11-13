using System;

namespace Ocore.desktop.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        public int? UserId { get; set; }

        public string Username { get; set; }

        public string ActionType { get; set; }

        public string EntityType { get; set; }

        public int? EntityId { get; set; }

        public string Details { get; set; }

        public string DeviceName { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}