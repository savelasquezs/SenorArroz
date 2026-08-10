namespace SenorArroz.Domain.Enums;

public enum DeliveryRoutingPlanStatus
{
    Active,
    Superseded,
    Consumed,
    Failed
}

public enum DeliveryRouteProposalStatus
{
    Available,
    Claimed,
    Superseded,
    Expired
}

public enum DeliveryRouteRecommendation
{
    LeaveNow,
    Wait,
    Next
}

public enum RoutingMatrixSource
{
    Approximate,
    Cache,
    Google
}

public enum GoogleRouteValidationStatus
{
    NotRequested,
    Validated,
    Failed,
    Degraded
}
