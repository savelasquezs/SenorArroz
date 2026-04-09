using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

/// <summary>Token FCM registrado por el domiciliario para recibir push notifications.</summary>
public class UserDeviceToken : BaseEntity
{
    public int UserId { get; set; }

    /// <summary>Token FCM emitido por Firebase en el dispositivo.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>"android" o "ios"</summary>
    public string Platform { get; set; } = "android";

    /// <summary>Fecha del último registro/refresh del token.</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual User User { get; set; } = null!;
}
