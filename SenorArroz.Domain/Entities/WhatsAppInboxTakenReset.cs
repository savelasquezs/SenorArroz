using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class WhatsAppInboxTakenReset : TenantOwnedEntity
{
    public int ScopeBranchId { get; set; }
    public DateTime ClearedBefore { get; set; }
}
