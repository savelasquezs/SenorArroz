using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class CustomerMergeHistory : TenantOwnedEntity
{
    public int OldCustomerId { get; set; }
    public int NewCustomerId { get; set; }
    public DateTime MergedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid MergeRunId { get; set; }
    public string? DetailsJson { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public Customer OldCustomer { get; set; } = null!;
    public Customer NewCustomer { get; set; } = null!;
}
