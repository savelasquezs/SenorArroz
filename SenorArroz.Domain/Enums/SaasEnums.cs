namespace SenorArroz.Domain.Enums;

public enum TenantStatus
{
    Draft,
    Active,
    Suspended,
    Cancelled
}

public enum PlanVersionStatus
{
    Draft,
    Published,
    Retired
}

public enum TenantSubscriptionStatus
{
    Active,
    Ended
}
