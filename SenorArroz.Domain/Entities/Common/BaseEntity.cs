namespace SenorArroz.Domain.Entities.Common;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public interface ITenantOwned
{
    int TenantId { get; set; }
}

public abstract class TenantOwnedEntity : BaseEntity, ITenantOwned
{
    public int TenantId { get; set; }
}
