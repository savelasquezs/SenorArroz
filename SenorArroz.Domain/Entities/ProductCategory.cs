using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class ProductCategory : BaseEntity
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;

    // Navigation Properties
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}