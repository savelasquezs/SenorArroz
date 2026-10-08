using System.Globalization;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

/// Reported OS state is evidence of state, never proof of an employee's intent.
/// Versioned markers prevent older app bugs from being upgraded to confirmed facts.
public static class DeliveryTrackingEvidencePolicy
{
    public const int StayMinutes = 10;
    public const int StayRadiusMeters = 20;
    public const int CustomerStaySeconds = 20 * 60;
    public const double MinimumAgreement = .90;
    public const string RetractedStay = "recomputed_insufficient_evidence";
    public const string CustomerStay = "order_destination_over_20_minutes";

    public static string? Value(string? details, string key)
    {
        var part = details?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(x => x.StartsWith(key + "=", StringComparison.Ordinal));
        return part == null ? null : part[(key.Length + 1)..];
    }

    public static bool IsNativeState(DeliveryDeviceEvent e) =>
        Value(e.Details, "evidence_version") == "2"
        && Value(e.Details, "source") == "android_state";

    public static bool ConfirmsGpsDisabled(DeliveryDeviceEvent e) =>
        e.EventType == DeliveryDeviceEventType.GpsDisabled
        && IsNativeState(e) && e.GpsEnabled == false
        && Value(e.Details, "location_enabled") == "false"
        && int.TryParse(Value(e.Details, "observed_seconds"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var duration) && duration >= 60;

    public static bool ConfirmsPermissionLoss(DeliveryDeviceEvent e) =>
        e.EventType == DeliveryDeviceEventType.LocationPermissionRevoked
        && IsNativeState(e) && e.LocationPermissionGranted == false
        && Value(e.Details, "permission_usable") == "false";

    public static bool ConfirmsUserStop(DeliveryDeviceEvent e) =>
        e.EventType == DeliveryDeviceEventType.AppStopped
        && Value(e.Details, "evidence_version") == "2"
        && Value(e.Details, "source") == "android_exit_info"
        && Value(e.Details, "exit_reason") == "10"
        && int.TryParse(Value(e.Details, "sdk"), out var sdk) && sdk >= 34;

    public static bool IsTechnicalStop(DeliveryDeviceEvent e) =>
        e.EventType == DeliveryDeviceEventType.DeviceRestarted
        || (Value(e.Details, "source") == "android_exit_info" && !ConfirmsUserStop(e)
            && Value(e.Details, "cause") == "technical");

    public static bool IsGenericTransport(DeliveryDeviceEvent e) =>
        Value(e.Details, "source") == "api_transport";

    public static bool SameEpisode(string? start, string? recovery)
    {
        var episode = Value(start, "episode");
        return episode != null && episode == Value(recovery, "episode");
    }

    public static (DeliveryInterruptionCause Cause, DeliveryInterruptionCertainty Certainty, string Reason)
        ClassifyInterruption(DeliveryDeviceEvent? e, bool transportEvidence)
    {
        if (e != null && ConfirmsUserStop(e))
            return (DeliveryInterruptionCause.AppOrTrackingServiceStopped,
                DeliveryInterruptionCertainty.ConfirmedByDevice, "android_user_requested_stop");
        if (e?.EventType == DeliveryDeviceEventType.DeviceRestarted)
            return (DeliveryInterruptionCause.DeviceRestarted,
                DeliveryInterruptionCertainty.TechnicalEvidence, "device_restarted");
        if (e?.EventType is DeliveryDeviceEventType.AppStopped or DeliveryDeviceEventType.LocationServiceRestarted)
            return (DeliveryInterruptionCause.NotDetermined,
                IsTechnicalStop(e) ? DeliveryInterruptionCertainty.TechnicalEvidence : DeliveryInterruptionCertainty.NotDetermined,
                IsTechnicalStop(e) ? "technical_process_exit" : "stop_cause_not_determined");
        if (e?.EventType == DeliveryDeviceEventType.AirplaneModeEnabled)
            return (DeliveryInterruptionCause.AirplaneModeEnabled,
                DeliveryInterruptionCertainty.TechnicalEvidence, "airplane_mode_reported_causality_unknown");
        if (e?.EventType == DeliveryDeviceEventType.WifiDisabled)
            return (DeliveryInterruptionCause.WifiDisabled,
                DeliveryInterruptionCertainty.TechnicalEvidence, "wifi_disabled_internet_not_proven");
        return transportEvidence
            ? (DeliveryInterruptionCause.ConnectivityInterruption, DeliveryInterruptionCertainty.TechnicalEvidence,
                "server_communication_interrupted")
            : (DeliveryInterruptionCause.NotDetermined, DeliveryInterruptionCertainty.NotDetermined,
                "cause_not_determinable");
    }
}
