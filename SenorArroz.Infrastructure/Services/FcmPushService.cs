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
        if (tokens.Count == 0 || string.IsNullOrEmpty(_fcmProjectId))
            return;

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
                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    // 404 o UNREGISTERED → token inválido → eliminar
                    if ((int)response.StatusCode == 404 ||
                        responseBody.Contains("UNREGISTERED") ||
                        responseBody.Contains("INVALID_ARGUMENT"))
                    {
                        invalidTokens.Add(token);
                        _logger.LogDebug("FCM: token inválido removido: {Token}", token[..Math.Min(20, token.Length)]);
                    }
                    else
                    {
                        _logger.LogWarning("FCM: error enviando a token {StatusCode}: {Body}",
                            response.StatusCode, responseBody);
                    }
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
