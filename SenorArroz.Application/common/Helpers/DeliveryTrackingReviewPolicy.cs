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
        DeliveryTrackingAlertType.UnexpectedStay,
    ];

    public static bool Includes(DeliveryTrackingAlertType alertType) =>
        IncludedAlertTypes.Contains(alertType);

    public static string AlertTypeCode(DeliveryTrackingAlertType alertType) => alertType switch
    {
        DeliveryTrackingAlertType.GpsDisabled => "gps_disabled",
        DeliveryTrackingAlertType.LocationPermissionRevoked => "location_permission_revoked",
        DeliveryTrackingAlertType.UnexpectedStay => "unexpected_stay",
        _ => throw new ArgumentOutOfRangeException(nameof(alertType), alertType, null),
    };

    public static string NotificationBody(DeliveryTrackingAlertType alertType) => alertType switch
    {
        DeliveryTrackingAlertType.UnexpectedStay =>
            "Se detectó que permaneciste en un lugar no autorizado por más tiempo del permitido. " +
            "Estás incurriendo en una posible falta disciplinaria. Si contabas con permiso, omite este mensaje; " +
            "para aclarar la situación, consulta a tu administrador.",
        DeliveryTrackingAlertType.GpsDisabled or DeliveryTrackingAlertType.LocationPermissionRevoked =>
            "Se detectó que apagaste la ubicación o retiraste su permiso durante la jornada. " +
            "Estás incurriendo en una posible falta disciplinaria. Si contabas con permiso, omite este mensaje; " +
            "para aclarar la situación, consulta a tu administrador.",
        _ => throw new ArgumentOutOfRangeException(nameof(alertType), alertType, null),
    };
}
