using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class LoyaltyRule : BaseEntity
{
    public int BranchId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int OrdersNeeded { get; set; } // Mapea a "n_orders_needed"

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}