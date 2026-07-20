using System.ComponentModel.DataAnnotations;
using SenorArroz.Application.Features.Branches.Commands;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class BranchDeliveryTrackingSettingsTests
{
    [Fact]
    public void Branch_UsesConfirmedDeliveryTrackingDefaults()
    {
        var branch = new Branch();

        Assert.Equal(new TimeOnly(21, 0), branch.DeliveryTrackingAutoCloseTime);
        Assert.Equal(300, branch.DeliveryTrackingLightIntervalSeconds);
        Assert.Equal(30, branch.DeliveryTrackingActiveIntervalSeconds);
        Assert.Equal(10, branch.DeliveryTrackingStayThresholdMinutes);
        Assert.Equal(50, branch.DeliveryTrackingStayRadiusMeters);
        Assert.Equal(50, branch.DeliveryTrackingAllowedDistanceMeters);
        Assert.Equal(3, branch.DeliveryTrackingLocationRetentionDays);
        Assert.Equal(15, branch.DeliveryTrackingIncidentRetentionDays);
    }

    [Fact]
    public void CreateBranchDto_UsesConfirmedDeliveryTrackingDefaults()
    {
        var dto = new CreateBranchDto();

        Assert.Equal(new TimeOnly(21, 0), dto.DeliveryTrackingAutoCloseTime);
        Assert.Equal(300, dto.DeliveryTrackingLightIntervalSeconds);
        Assert.Equal(30, dto.DeliveryTrackingActiveIntervalSeconds);
        Assert.Equal(10, dto.DeliveryTrackingStayThresholdMinutes);
        Assert.Equal(50, dto.DeliveryTrackingStayRadiusMeters);
        Assert.Equal(50, dto.DeliveryTrackingAllowedDistanceMeters);
        Assert.Equal(3, dto.DeliveryTrackingLocationRetentionDays);
        Assert.Equal(15, dto.DeliveryTrackingIncidentRetentionDays);
    }

    [Fact]
    public void UpdateBranchDto_RejectsNonPositiveDeliveryTrackingValues()
    {
        var dto = new UpdateBranchDto
        {
            Name = "Centro",
            Address = "Calle 1 # 2-3",
            Phone1 = "3001234567",
            DeliveryTrackingLightIntervalSeconds = 0,
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(dto, new ValidationContext(dto), results, true);

        Assert.False(valid);
        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(UpdateBranchDto.DeliveryTrackingLightIntervalSeconds)));
    }

    [Fact]
    public void UpdatingAutoCloseTime_RecalculatesActiveSessionCutoff()
    {
        var session = ActiveSession(new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc));
        var now = new DateTime(2026, 7, 20, 19, 0, 0, DateTimeKind.Utc);

        UpdateBranchHandler.ApplyAutoCloseTimeToActiveSessions(
            [session],
            new TimeOnly(22, 0),
            now);

        Assert.Equal(new DateTime(2026, 7, 21, 3, 0, 0, DateTimeKind.Utc), session.AutoCloseAt);
        Assert.Equal(DeliveryWorkSessionStatus.Active, session.Status);
        Assert.Null(session.EndedAt);
    }

    [Fact]
    public void UpdatingAutoCloseTimeToPast_ClosesActiveSessionImmediately()
    {
        var session = ActiveSession(new DateTime(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc));
        var now = new DateTime(2026, 7, 21, 1, 0, 0, DateTimeKind.Utc);

        UpdateBranchHandler.ApplyAutoCloseTimeToActiveSessions(
            [session],
            new TimeOnly(19, 0),
            now);

        Assert.Equal(new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc), session.AutoCloseAt);
        Assert.Equal(DeliveryWorkSessionStatus.Closed, session.Status);
        Assert.Equal(DeliveryWorkSessionEndReason.AutomaticClosure, session.EndReason);
        Assert.Equal(now, session.EndedAt);
    }

    private static DeliveryWorkSession ActiveSession(DateTime startedAt) => new()
    {
        DeliverymanId = 1,
        BranchId = 7,
        DeviceInstallationId = "device-a",
        DevicePlatform = "android",
        StartedAt = startedAt,
        AutoCloseAt = startedAt.AddHours(8),
        LastCommunicationAt = startedAt,
        Status = DeliveryWorkSessionStatus.Active,
    };
}
