using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>One durable first-arrival announcement per order and tenant.</summary>
public sealed class KitchenVoiceEvent : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = "new";
}
