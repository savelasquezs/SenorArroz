using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class CommercialProfile : BaseEntity
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Ingredients { get; set; }
    public string? PhotoUrl { get; set; }
    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
