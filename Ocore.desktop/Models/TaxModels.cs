
using System;
using System.Collections.Generic;

namespace Ocore.desktop.Models
{
    public class TaxProfile
    {
        public int Id { get; set; }

        public string Code { get; set; }

        public string Name { get; set; }

        // Unclassified / Exempt / Standard / Reduced / Schedule
        public string TaxType { get; set; }

        public string Description { get; set; }

        public bool IsActive { get; set; }

        public bool IsSystem { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime? CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public List<TaxProfileRate> Rates { get; set; }

        public TaxProfile()
        {
            Code = string.Empty;
            Name = string.Empty;
            TaxType = "Unclassified";
            Description = string.Empty;
            Rates = new List<TaxProfileRate>();
        }
    }

    public class TaxProfileRate
    {
        public int Id { get; set; }

        public int TaxProfileId { get; set; }

        public string ComponentCode { get; set; }

        public string ComponentName { get; set; }

        // VAT / Schedule
        public string ComponentType { get; set; }

        public double RatePercent { get; set; }

        public int CalculationOrder { get; set; }

        public bool IsCompound { get; set; }

        public DateTime EffectiveFrom { get; set; }

        public DateTime? EffectiveTo { get; set; }

        public bool IsActive { get; set; }

        public int? CreatedBy { get; set; }

        public DateTime? CreatedAt { get; set; }

        public TaxProfileRate()
        {
            ComponentCode = string.Empty;
            ComponentName = string.Empty;
            ComponentType = "VAT";
            CalculationOrder = 1;
            IsActive = true;
        }
    }

    public class TaxAuditLog
    {
        public int Id { get; set; }

        public int? UserId { get; set; }

        public string ActionType { get; set; }

        public string EntityType { get; set; }

        public int? EntityId { get; set; }

        public string OldValues { get; set; }

        public string NewValues { get; set; }

        public string Reason { get; set; }

        public string DeviceName { get; set; }

        public DateTime CreatedAt { get; set; }

        public TaxAuditLog()
        {
            ActionType = string.Empty;
            EntityType = string.Empty;
            OldValues = string.Empty;
            NewValues = string.Empty;
            Reason = string.Empty;
            DeviceName = string.Empty;
        }
    }
}
