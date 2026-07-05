using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class WhatsAppQuickReply : BaseEntity
{
    public int BranchId { get; set; }
    public string Shortcut { get; set; } = string.Empty;
    public string MessageTemplate { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int UsageCount { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public virtual Branch Branch { get; set; } = null!;
}
