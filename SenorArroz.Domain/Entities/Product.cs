using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Product : BaseEntity
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public int? Stock { get; set; }
    public bool Active { get; set; } = true;

    // Navigation Properties
    public virtual ProductCategory Category { get; set; } = null!;
    public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
}