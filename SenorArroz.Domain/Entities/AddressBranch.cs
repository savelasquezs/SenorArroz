using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class AddressBranch : TenantOwnedEntity
{
    public int AddressId { get; set; }
    public int BranchId { get; set; }
    public int? NeighborhoodId { get; set; }
    public int DeliveryFee { get; set; }
    public bool IsCovered { get; set; } = true;
    public DateTime? ValidatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Address Address { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public Neighborhood? Neighborhood { get; set; }
}
