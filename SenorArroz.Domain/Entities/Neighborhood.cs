using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Neighborhood : TenantOwnedEntity
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeliveryFee { get; set; }
    public bool Active { get; set; } = true;

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual ICollection<Address> Addresses { get; set; } = new List<Address>();
    public virtual ICollection<AddressBranch> AddressServices { get; set; } = new List<AddressBranch>();
}
