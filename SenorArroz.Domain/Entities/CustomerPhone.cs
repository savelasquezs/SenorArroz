using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class CustomerPhone : TenantOwnedEntity
{
    public int CustomerId { get; set; }
    public string PhoneNormalized { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool Active { get; set; } = true;

    public Tenant Tenant { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
}
