using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Deliverymen.DTOs;

public class DeliveryTrackingConfigurationDto
{
    public TimeOnly AutoCloseTime { get; set; }
    public int LightIntervalSeconds { get; set; }
    public int ActiveIntervalSeconds { get; set; }
    public int StayThresholdMinutes { get; set; }
    public int StayRadiusMeters { get; set; }
    public int AllowedDistanceMeters { get; set; }
    public int LocationRetentionDays { get; set; }
    public int IncidentRetentionDays { get; set; }
}

public class DeliveryWorkSessionDto
{
    public int Id { get; set; }
    public int DeliverymanId { get; set; }
    public int BranchId { get; set; }
    public string DeviceInstallationId { get; set; } = string.Empty;
    public string DevicePlatform { get; set; } = string.Empty;
    public string? DeviceDescription { get; set; }
    public string? AppVersion { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime AutoCloseAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DeliveryWorkSessionEndReason? EndReason { get; set; }
    public DeliveryWorkSessionStatus Status { get; set; }
    public DateTime LastCommunicationAt { get; set; }
    public DeliveryTrackingConfigurationDto Tracking { get; set; } = new();
}

internal static class DeliveryWorkSessionDtoMapper
{
    public static DeliveryWorkSessionDto Map(DeliveryWorkSession session, Branch branch) => new()
    {
        Id = session.Id,
        DeliverymanId = session.DeliverymanId,
        BranchId = session.BranchId,
        DeviceInstallationId = session.DeviceInstallationId,
        DevicePlatform = session.DevicePlatform,
        DeviceDescription = session.DeviceDescription,
        AppVersion = session.AppVersion,
        StartedAt = session.StartedAt,
        AutoCloseAt = session.AutoCloseAt,
        EndedAt = session.EndedAt,
        EndReason = session.EndReason,
        Status = session.Status,
        LastCommunicationAt = session.LastCommunicationAt,
        Tracking = new DeliveryTrackingConfigurationDto
        {
            AutoCloseTime = branch.DeliveryTrackingAutoCloseTime,
            LightIntervalSeconds = branch.DeliveryTrackingLightIntervalSeconds,
            ActiveIntervalSeconds = branch.DeliveryTrackingActiveIntervalSeconds,
            StayThresholdMinutes = branch.DeliveryTrackingStayThresholdMinutes,
            StayRadiusMeters = branch.DeliveryTrackingStayRadiusMeters,
            AllowedDistanceMeters = branch.DeliveryTrackingAllowedDistanceMeters,
            LocationRetentionDays = branch.DeliveryTrackingLocationRetentionDays,
            IncidentRetentionDays = branch.DeliveryTrackingIncidentRetentionDays,
        },
    };
}
