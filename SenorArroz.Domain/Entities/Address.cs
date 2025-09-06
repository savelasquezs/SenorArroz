using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Address : BaseEntity
{
    public int CustomerId { get; set; }
    public int NeighborhoodId { get; set; }
    public string AddressText { get; set; } = string.Empty; // Mapea a "address" en SQL
    public string? AdditionalInfo { get; set; }
    public int DeliveryFee { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    // Navigation Properties
    public virtual Customer Customer { get; set; } = null!;
    public virtual Neighborhood Neighborhood { get; set; } = null!;
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}