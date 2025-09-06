using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class OrderDetail : BaseEntity
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; } = 1;
    public int UnitPrice { get; set; } = 0;
    public int Discount { get; set; } = 0;
    public int? Subtotal { get; set; } // Nullable porque puede ser calculado por trigger
    public string? Notes { get; set; }

    // Navigation Properties
    public virtual Order Order { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
}