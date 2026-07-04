using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public class WhatsAppConversation : BaseEntity
{
    public int BranchId { get; set; }
    public int? CustomerId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public WhatsAppConversationStatus Status { get; set; } = WhatsAppConversationStatus.Open;
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual Customer? Customer { get; set; }
    public virtual ICollection<WhatsAppMessage> Messages { get; set; } = new List<WhatsAppMessage>();
}
