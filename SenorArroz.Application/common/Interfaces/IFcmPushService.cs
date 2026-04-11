namespace SenorArroz.Application.Common.Interfaces;

/// <summary>Servicio para enviar push notifications via Firebase Cloud Messaging (HTTP v1 API).</summary>
public interface IFcmPushService
{
    /// <summary>
    /// Envía una notificación a una lista de tokens FCM.
    /// Los tokens inválidos/expirados se eliminan automáticamente.
    /// </summary>
    Task SendToTokensAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default,
        string? correlationId = null);
}
