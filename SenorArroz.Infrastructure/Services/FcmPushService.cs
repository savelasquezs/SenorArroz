using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

/// <summary>
/// Envía push notifications via Firebase Cloud Messaging HTTP v1 API.
/// Usa las mismas credenciales de Google ya configuradas para Firebase Storage.
/// </summary>
public class FcmPushService : IFcmPushService
{
    private readonly HttpClient _http;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FcmPushService> _logger;
    private readonly string _fcmProjectId;

    // Scope requerido por FCM HTTP v1
    private static readonly string[] FcmScopes =
        ["https://www.googleapis.com/auth/firebase.messaging"];

    public FcmPushService(
        HttpClient http,
        IServiceProvider serviceProvider,
        ILogger<FcmPushService> logger,
        IConfiguration configuration)
    {
        _http = http;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _fcmProjectId = configuration["Fcm:ProjectId"] ?? string.Empty;
    }

    public async Task SendToTokensAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        CancellationToken cancellationToken = default)
    {
        if (tokens.Count == 0)
            return;

        if (string.IsNullOrEmpty(_fcmProjectId))
        {
            _logger.LogWarning(
                "FCM: hay {Count} token(s) pero Fcm:ProjectId está vacío; configure el id del proyecto Firebase (misma consola que FCM).",
                tokens.Count);
            return;
        }

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FCM: no se pudo obtener access token");
            return;
        }

        var invalidTokens = new List<string>();

        foreach (var token in tokens)
        {
            try
            {
                var payload = BuildPayload(token, title, body, data);
                var json = JsonSerializer.Serialize(payload);
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://fcm.googleapis.com/v1/projects/{_fcmProjectId}/messages:send");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug(
                        "FCM: envío OK (200) token prefijo {Prefix}",
                        token[..Math.Min(16, token.Length)]);
                    continue;
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                // Solo quitar de BD cuando FCM indica token inexistente/expirado.
                // INVALID_ARGUMENT suele ser payload; borrarlo eliminaba tokens válidos por error.
                if (ShouldRemoveStoredDeviceToken(response.StatusCode, responseBody))
                {
                    invalidTokens.Add(token);
                    _logger.LogInformation(
                        "FCM: token dado de baja en FCM, se elimina de BD (prefijo {Prefix}): {Body}",
                        token[..Math.Min(20, token.Length)],
                        responseBody.Length > 500 ? responseBody[..500] + "…" : responseBody);
                }
                else
                {
                    _logger.LogWarning("FCM: error enviando a token {StatusCode}: {Body}",
                        response.StatusCode, responseBody);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM: excepción enviando push a token");
            }
        }

        // Eliminar tokens inválidos de la BD
        if (invalidTokens.Count > 0)
        {
            await RemoveInvalidTokensAsync(invalidTokens, cancellationToken);
        }
    }

    /// <summary>
    /// True si el error de FCM indica que este registration token ya no debe usarse (sí borrar en BD).
    /// </summary>
    private static bool ShouldRemoveStoredDeviceToken(HttpStatusCode statusCode, string responseBody)
    {
        if (string.IsNullOrEmpty(responseBody))
            return false;

        if (responseBody.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase))
            return true;

        if (statusCode == HttpStatusCode.NotFound &&
            responseBody.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static object BuildPayload(
        string token, string title, string body,
        Dictionary<string, string>? data)
    {
        var message = new Dictionary<string, object>
        {
            ["token"] = token,
            ["notification"] = new { title, body },
            ["android"] = new
            {
                notification = new
                {
                    sound = "default",
                    channel_id = "delivery_orders",
                    priority = "high"
                },
                priority = "high"
            },
            ["apns"] = new
            {
                payload = new
                {
                    aps = new { sound = "default", badge = 1 }
                }
            }
        };

        if (data != null)
            message["data"] = data;

        return new { message };
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var credential = GoogleCredential.GetApplicationDefault()
            .CreateScoped(FcmScopes);
        var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(
            cancellationToken: ct);
        return token;
    }

    private async Task RemoveInvalidTokensAsync(List<string> tokens, CancellationToken ct)
    {
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var toRemove = await db.UserDeviceTokens
                .Where(t => tokens.Contains(t.Token))
                .ToListAsync(ct);
            db.UserDeviceTokens.RemoveRange(toRemove);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FCM: no se pudieron eliminar tokens inválidos");
        }
    }
}
