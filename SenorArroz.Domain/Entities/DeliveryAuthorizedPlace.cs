using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class DeliveryAuthorizedPlace : BaseEntity
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int RadiusMeters { get; set; } = 50;
    public bool Active { get; set; } = true;

    public virtual Branch Branch { get; set; } = null!;
}
