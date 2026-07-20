using System.ComponentModel.DataAnnotations;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Entities;

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
}
