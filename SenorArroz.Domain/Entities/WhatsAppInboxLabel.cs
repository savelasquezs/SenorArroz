using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class WhatsAppInboxLabel : ITenantOwned
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public sealed class WhatsAppConversationLabel : ITenantOwned
{
    public int TenantId { get; set; }
    public int ConversationId { get; set; }
    public int LabelId { get; set; }
}
