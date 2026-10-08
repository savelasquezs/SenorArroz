using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class DeliveryTrackingEvidencePolicyTests
{
    private static readonly DateTime Start = new(2026, 10, 8, 17, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    [InlineData(90, true)]
    public void GpsRequiresIndependentAndroidEvidenceAndOneMinute(int seconds, bool expected)
    {
        var e = new DeliveryDeviceEvent { EventType = DeliveryDeviceEventType.GpsDisabled,
            GpsEnabled = false, Details = $"evidence_version=2;source=android_state;location_enabled=false;observed_seconds={seconds}" };
        Assert.Equal(expected, DeliveryTrackingEvidencePolicy.ConfirmsGpsDisabled(e));
        e.Details = "serviceEnabled=false;permission=denied;readFailed=true";
        Assert.False(DeliveryTrackingEvidencePolicy.ConfirmsGpsDisabled(e));
        e.Details = $"evidence_version=2;source=android_state;location_enabled=false;observed_seconds={seconds}";
        e.GpsEnabled = true;
        Assert.False(DeliveryTrackingEvidencePolicy.ConfirmsGpsDisabled(e));
    }

    [Fact]
    public void PermissionLossDoesNotFollowFromGpsDisabled()
    {
        var e = new DeliveryDeviceEvent { EventType = DeliveryDeviceEventType.LocationPermissionRevoked,
            LocationPermissionGranted = false, GpsEnabled = false,
            Details = "serviceEnabled=false;permission=always;accuracy=precise" };
        Assert.False(DeliveryTrackingEvidencePolicy.ConfirmsPermissionLoss(e));
        e.Details = "evidence_version=2;source=android_state;permission_usable=false";
        Assert.True(DeliveryTrackingEvidencePolicy.ConfirmsPermissionLoss(e));
    }

    [Theory]
    [InlineData(33, "10", false)] // Android before 14 also uses 10 for updates.
    [InlineData(34, "10", true)]
    [InlineData(35, "11", false)] // Android user/profile stopped, not app force-stop.
    [InlineData(35, "16", false)] // Package updated.
    public void UserStopRequiresUnambiguousExitReason(int sdk, string reason, bool expected)
    {
        var e = new DeliveryDeviceEvent { EventType = DeliveryDeviceEventType.AppStopped,
            Details = $"evidence_version=2;source=android_exit_info;sdk={sdk};exit_reason={reason}" };
        Assert.Equal(expected, DeliveryTrackingEvidencePolicy.ConfirmsUserStop(e));
        e.Details = "detected_on_next_launch";
        Assert.False(DeliveryTrackingEvidencePolicy.ConfirmsUserStop(e));
        Assert.Equal(DeliveryInterruptionCertainty.NotDetermined,
            DeliveryTrackingEvidencePolicy.ClassifyInterruption(e, false).Certainty);
    }

    [Fact]
    public void IsolatedOutliersDoNotResetTenMinuteStay()
    {
        var points = Samples();
        points[6] = points[6] with { Latitude = 6.255m };
        points[12] = points[12] with { Latitude = 6.255m };
        var stays = DeliveryStayDetectionService.Detect(points, 10, 20);
        var stay = Assert.Single(stays);
        Assert.Equal(600, stay.DurationSeconds);
        Assert.Equal(21, stay.PointCount);
        Assert.True(stay.RadiusMeters <= 20);
    }

    [Fact]
    public void LessThanNinetyPercentCannotConfirmStay()
    {
        var points = Samples();
        foreach (var i in new[] { 5, 10, 15 }) points[i] = points[i] with { Latitude = 6.255m };
        Assert.Empty(DeliveryStayDetectionService.Detect(points, 10, 20));
    }

    [Fact]
    public void InaccurateReadingsAreNeutralButInsufficientCoverageIsNotProof()
    {
        var points = Samples();
        points[6] = points[6] with { Latitude = 6.28m, AccuracyMeters = 60 };
        points[12] = points[12] with { Latitude = 6.28m, AccuracyMeters = 60 };
        Assert.Single(DeliveryStayDetectionService.Detect(points, 10, 20));
        foreach (var i in Enumerable.Range(1, 18)) points[i] = points[i] with { AccuracyMeters = 60 };
        Assert.Empty(DeliveryStayDetectionService.Detect(points, 10, 20));
    }

    [Fact]
    public void MissingCapturedPointsDoNotBecomeStationaryTime()
    {
        var points = new[] { Samples()[0], Samples()[1], Samples()[20] };
        Assert.Empty(DeliveryStayDetectionService.Detect(points, 10, 20));
        Assert.Single(DeliveryStayDetectionService.Detect(Samples().AsEnumerable().Reverse().ToList(), 10, 20));
    }

    [Fact]
    public void LightTrackingWithoutRouteRemainsEligible_ExplicitlyDeferredGate()
    {
        var points = new[] { 0, 5, 10 }.Select((minutes, i) => new DeliveryStayPoint(
            i + 1, 6.25m, -75.57m, 8, true, null, Start.AddMinutes(minutes), 300)).ToList();
        Assert.Single(DeliveryStayDetectionService.Detect(points, 10, 20));
    }

    [Fact]
    public void CenterIsNotAnchoredToFirstAnomalousPoint()
    {
        var points = Samples(23);
        points[0] = points[0] with { Latitude = 6.255m };
        var stay = Assert.Single(DeliveryStayDetectionService.Detect(points, 10, 20));
        Assert.Equal(6.25m, stay.CenterLatitude);
        Assert.True(stay.StartedAt >= Start.AddSeconds(30));
    }

    [Fact]
    public void LongSlowDriftCannotMoveTheStationaryCircleIndefinitely()
    {
        var points = Samples(100).Select((p, i) => p with { Latitude = 6.25m + i * .000018m }).ToList();
        Assert.All(DeliveryStayDetectionService.Detect(points, 10, 20), stay =>
            Assert.True(stay.DurationSeconds < 1500));
    }

    private static List<DeliveryStayPoint> Samples(int count = 21) => Enumerable.Range(0, count)
        .Select(i => new DeliveryStayPoint(i + 1, 6.25m, -75.57m, 8, true, 20, Start.AddSeconds(i * 30), 30)).ToList();
}
