using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class StorefrontAnalyticsDaily : TenantOwnedEntity
{
    public DateTime EventDate { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string UtmSource { get; set; } = string.Empty;
    public string UtmMedium { get; set; } = string.Empty;
    public string UtmCampaign { get; set; } = string.Empty;
    public string UtmContent { get; set; } = string.Empty;
    public string UtmTerm { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public string FulfillmentType { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public short CoverageState { get; set; }
    public long EventCount { get; set; }
    public decimal ValueSum { get; set; }
}
