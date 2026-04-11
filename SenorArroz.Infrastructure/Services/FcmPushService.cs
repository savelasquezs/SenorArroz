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
        CancellationToken cancellationToken = default,
        string? correlationId = null)
    {
        var tag = FormatCorrelation(correlationId);

        if (tokens.Count == 0)
        {
            _logger.LogInformation("{Tag}STEP skip empty_token_list", tag);
            return;
        }

        if (string.IsNullOrEmpty(_fcmProjectId))
        {
            _logger.LogWarning(
                "{Tag}STEP fail config Fcm:ProjectId vacío; hay {Count} token(s).",
                tag, tokens.Count);
            return;
        }

        _logger.LogInformation(
            "{Tag}STEP oauth_start projectId_set={HasProject} token_targets={Count}",
            tag, true, tokens.Count);

        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync(cancellationToken);
            _logger.LogInformation("{Tag}STEP oauth_ok", tag);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Tag}STEP fail oauth_exception", tag);
            return;
        }

        var invalidTokens = new List<string>();
        var successCount = 0;
        var errorCount = 0;

        var i = 0;
        foreach (var token in tokens)
        {
            i++;
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
                    successCount++;
                    _logger.LogInformation(
                        "{Tag}STEP http_ok idx={Idx}/{Total} prefix={Prefix}",
                        tag, i, tokens.Count, token[..Math.Min(16, token.Length)]);
                    continue;
                }

                errorCount++;
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (ShouldRemoveStoredDeviceToken(response.StatusCode, responseBody))
                {
                    invalidTokens.Add(token);
                    _logger.LogInformation(
                        "{Tag}STEP http_token_dead idx={Idx} prefix={Prefix} status={Status} body={Body}",
                        tag, i, token[..Math.Min(20, token.Length)], response.StatusCode,
                        Truncate(responseBody, 400));
                }
                else
                {
                    _logger.LogWarning(
                        "{Tag}STEP http_error idx={Idx} prefix={Prefix} status={Status} body={Body}",
                        tag, i, token[..Math.Min(16, token.Length)], response.StatusCode,
                        Truncate(responseBody, 400));
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogWarning(ex,
                    "{Tag}STEP http_exception idx={Idx}/{Total}",
                    tag, i, tokens.Count);
            }
        }

        if (invalidTokens.Count > 0)
        {
            await RemoveInvalidTokensAsync(invalidTokens, cancellationToken, tag);
        }

        _logger.LogInformation(
            "{Tag}STEP summary ok={Ok} err={Err} removed_from_db={Removed}",
            tag, successCount, errorCount, invalidTokens.Count);
    }

    private static string FormatCorrelation(string? correlationId) =>
        string.IsNullOrWhiteSpace(correlationId)
            ? "FCM "
            : $"FCM[{correlationId}] ";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

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

    private async Task RemoveInvalidTokensAsync(List<string> tokens, CancellationToken ct, string logTag)
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
            _logger.LogInformation("{Tag}STEP db_removed_invalid count={Count}", logTag, toRemove.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Tag}STEP fail db_remove_invalid", logTag);
        }
    }
}
