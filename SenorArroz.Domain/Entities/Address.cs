using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class Address : BaseEntity
{
    public int CustomerId { get; set; }
    public int? NeighborhoodId { get; set; }
    public string? Label { get; set; }
    public string AddressText { get; set; } = string.Empty; // Mapea a "address" en SQL
    public string? AdditionalInfo { get; set; }
    public int DeliveryFee { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsPrimary { get; set; } = false;
    public string? OriginalAddressText { get; set; }
    public string? NormalizedAddressText { get; set; }
    public string? Instructions { get; set; }
    public string? ValidationSource { get; set; }
    public DateTime? ValidatedAt { get; set; }

    // Navigation Properties
    public virtual Customer Customer { get; set; } = null!;
    public virtual Neighborhood? Neighborhood { get; set; }
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
