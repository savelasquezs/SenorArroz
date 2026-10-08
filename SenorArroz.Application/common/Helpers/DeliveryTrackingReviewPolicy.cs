using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

public static class DeliveryTrackingReviewPolicy
{
    public const string NotificationType = "delivery_tracking_review";
    public const string NotificationTitle = "Revisión de seguimiento";
    public const string NotificationChannelId = "delivery_tracking_reviews";

    public static readonly DeliveryTrackingAlertType[] IncludedAlertTypes =
    [
        DeliveryTrackingAlertType.GpsDisabled,
        DeliveryTrackingAlertType.LocationPermissionRevoked,
        DeliveryTrackingAlertType.NoCommunication,
        DeliveryTrackingAlertType.UnexpectedStay,
    ];

    public static bool Includes(DeliveryTrackingAlertType alertType) =>
        IncludedAlertTypes.Contains(alertType);

    public static string AlertTypeCode(DeliveryTrackingAlertType alertType) => alertType switch
    {
        DeliveryTrackingAlertType.GpsDisabled => "gps_disabled",
        DeliveryTrackingAlertType.LocationPermissionRevoked => "location_permission_revoked",
        DeliveryTrackingAlertType.NoCommunication => "no_communication",
        DeliveryTrackingAlertType.UnexpectedStay => "unexpected_stay",
        _ => throw new ArgumentOutOfRangeException(nameof(alertType), alertType, null),
    };

    public static string NotificationBody(DeliveryTrackingAlertType alertType) => alertType switch
    {
        DeliveryTrackingAlertType.GpsDisabled =>
            "Android reportó la ubicación desactivada durante al menos un minuto. Debes mantenerla activa durante la jornada. " +
            "Administración tiene el evento registrado para revisión.",
        DeliveryTrackingAlertType.LocationPermissionRevoked =>
            "Android reportó permisos de ubicación insuficientes. Mantén ubicación precisa y permiso todo el tiempo durante la jornada. " +
            "Administración tiene el evento registrado para revisión.",
        DeliveryTrackingAlertType.UnexpectedStay =>
            "Se registró una permanencia que requiere revisión administrativa. Consulta al administrador para aclarar su contexto.",
        DeliveryTrackingAlertType.NoCommunication =>
            "Se registró una interrupción del seguimiento para revisión administrativa. No se considera una falta automáticamente.",
        _ => throw new ArgumentOutOfRangeException(nameof(alertType), alertType, null),
    };
}
