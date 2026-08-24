using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;

namespace SenorArroz.Application.Features.Branches.Commands;

public class CreateBranchCommand : IRequest<BranchDto>
{
    public string Name { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string? Nit { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Phone1 { get; set; } = string.Empty;
    public string? Phone2 { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    public int MaxFreeDeliveryDiscount { get; set; } = 3000;
    public int PosCopyEtaMinMinutes { get; set; } = 30;
    public int PosCopyEtaRangeMinutes { get; set; } = 15;
    public TimeOnly DeliveryTrackingAutoCloseTime { get; set; } = new(21, 0);
    public int DeliveryTrackingLightIntervalSeconds { get; set; } = 300;
    public int DeliveryTrackingActiveIntervalSeconds { get; set; } = 30;
    public int DeliveryTrackingStayThresholdMinutes { get; set; } = 10;
    public int DeliveryTrackingStayRadiusMeters { get; set; } = 50;
    public int DeliveryTrackingAllowedDistanceMeters { get; set; } = 50;
    public int DeliveryTrackingLocationRetentionDays { get; set; } = 3;
    public int DeliveryTrackingIncidentRetentionDays { get; set; } = 15;
    public bool DeliveryAutoCompleteEnabled { get; set; } = true;
    public int DeliveryAutoCompleteArrivalRadiusMeters { get; set; } = 50;
    public int DeliveryAutoCompleteDepartureRadiusMeters { get; set; } = 120;
    public int DeliveryAutoCompleteMinPresenceSeconds { get; set; } = 15;
}
