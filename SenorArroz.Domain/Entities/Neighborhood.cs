using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Neighborhood : BaseEntity
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeliveryFee { get; set; }

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<Address> Addresses { get; set; } = new List<Address>();
}