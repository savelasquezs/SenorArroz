using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class WhatsAppQuickReply : TenantOwnedEntity
{
    public const string Send = "send";
    public const string Draft = "draft";
    public const string ChicharronQuote = "chicharron_quote";

    public int BranchId { get; set; }
    public string Shortcut { get; set; } = string.Empty;
    public string MessageTemplate { get; set; } = string.Empty;
    public string ReplyMode { get; set; } = Send;
    public bool IsActive { get; set; } = true;
    public int UsageCount { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public virtual Branch Branch { get; set; } = null!;
}
