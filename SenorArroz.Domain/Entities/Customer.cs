using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Customer : BaseEntity
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone1 { get; set; } = string.Empty;
    public string? Phone2 { get; set; }
    public bool Active { get; set; } = true;

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<Address> Addresses { get; set; } = new List<Address>();
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
