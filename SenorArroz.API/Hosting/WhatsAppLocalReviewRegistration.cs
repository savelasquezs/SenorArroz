using System.Net;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http;
using Npgsql;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using static SenorArroz.Application.Common.Services.WhatsAppWebhookIdentityReader;

namespace SenorArroz.API.Hosting;

public static class WhatsAppLocalReviewRegistration
{
    public static bool ConfigureWhatsAppLocalReview(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("WhatsAppLocalReview:Enabled")) return false;
        var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        var localHost = connection.Host == "coexistence-db"
            || connection.Host == "localhost"
            || IPAddress.TryParse(connection.Host, out var address) && IPAddress.IsLoopback(address);
        if (!environment.IsDevelopment() || !localHost || connection.Database != "senor_arroz_coexistence_local")
            throw new InvalidOperationException("La revisión local exige Development y la copia senor_arroz_coexistence_local.");
        foreach (var service in services.Where(service => service.ServiceType == typeof(IHostedService)).ToArray())
            services.Remove(service);
        services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder =>
        {
            builder.PrimaryHandler.Dispose();
            builder.PrimaryHandler = new LocalReviewTransportHandler();
        }));
        return true;
    }

    private sealed class LocalReviewTransportHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri is { Scheme: "https", Host: "graph.facebook.com" } uri
                && uri.AbsolutePath.EndsWith("/local-coexistence-review/messages", StringComparison.Ordinal)
                && string.IsNullOrEmpty(uri.Query)
                && request.Headers.Authorization is { Scheme: "Bearer", Parameter: "local-disabled" }
                && request.Content is not null)
            {
                try
                {
                    using var payload = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                    var body = payload.RootElement;
                    if (body.ValueKind == JsonValueKind.Object
                        && body.TryGetProperty("messaging_product", out var product) && product.ValueKind == JsonValueKind.String && product.ValueEquals("whatsapp")
                        && body.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.ValueEquals("text")
                        && body.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(to.GetString())
                        && body.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.Object
                        && text.TryGetProperty("body", out var message) && message.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(message.GetString()))
                    {
                        var response = new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            RequestMessage = request,
                            Content = JsonContent.Create(new { messaging_product = "whatsapp",
                                messages = new[] { new { id = $"wamid.local.simulation.sent.{Guid.NewGuid():N}" } } })
                        };
                        response.Headers.Add("X-WhatsApp-Local-Simulation", "true");
                        return response;
                    }
                }
                catch (JsonException) { }
            }
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                Content = JsonContent.Create(new { error = new {
                    message = "Los envíos externos están deshabilitados en esta revisión local.",
                    type = "local_review_transport_disabled" } })
            };
        }
    }
}

public sealed class WhatsAppCoexistencePilot
{
    private const string RecoveryCredentialPrefix = "pilot-recovery.";
    private readonly string _appSecret;
    private readonly string _verifyToken;
    private readonly HashSet<string> _recipients;

    private WhatsAppCoexistencePilot(IConfiguration configuration)
    {
        var settings = configuration.GetSection("WhatsAppCoexistence:Pilot");
        TenantId = settings.GetValue<int>("TenantId");
        BranchId = settings.GetValue<int>("BranchId");
        var phoneId = settings["PhoneNumberId"];
        PhoneNumberId = string.IsNullOrWhiteSpace(phoneId) ? null : phoneId;
        var recoveryWaba = settings["RecoveryWabaId"];
        var recoveryToken = settings["RecoveryAccessToken"];
        TemporaryRecoveryToken = settings.GetValue<bool>("TemporaryRecoveryToken");
        if (TemporaryRecoveryToken || !string.IsNullOrWhiteSpace(recoveryWaba) || !string.IsNullOrWhiteSpace(recoveryToken))
        {
            if (PhoneNumberId is null || string.IsNullOrWhiteSpace(recoveryWaba)
                || !System.Text.RegularExpressions.Regex.IsMatch(recoveryWaba, "^[0-9]{1,64}$")
                || string.IsNullOrWhiteSpace(recoveryToken))
                throw new InvalidOperationException("Configura el número, la cuenta y la credencial exclusiva para recuperar Santander.");
            RecoveryWabaId = recoveryWaba;
            RecoveryAccessToken = recoveryToken;
        }
        var callback = settings["CallbackUrl"];
        if (!string.IsNullOrWhiteSpace(callback))
        {
            if (!Uri.TryCreate(callback, UriKind.Absolute, out var url) || url.Scheme != "https"
                || url.IsLoopback || !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query)
                || !string.IsNullOrEmpty(url.Fragment) || url.AbsolutePath != "/api/whatsapp/webhook" || callback.Length > 200)
                throw new InvalidOperationException("Configura una URL HTTPS exclusiva para el webhook del piloto.");
            CallbackUrl = url.AbsoluteUri;
        }
        _recipients = (settings["AllowedRecipients"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(x => NormalizePhone(x) ?? "").ToHashSet(StringComparer.Ordinal);
        _appSecret = configuration["WhatsAppCoexistence:AppSecret"] ?? "";
        _verifyToken = configuration["WhatsAppCoexistence:WebhookVerifyToken"] ?? "";
        if (TenantId <= 0 || BranchId <= 0 || _recipients.Count != 2 || _recipients.Contains("573012274655")
            || _recipients.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x, "^573[0-9]{9}$"))
            || !WhatsAppWebhookSignature.IsValidAppSecret(_appSecret) || _verifyToken.Length < 32
            || PhoneNumberId is not null && !System.Text.RegularExpressions.Regex.IsMatch(PhoneNumberId, "^[0-9]{1,64}$"))
            throw new InvalidOperationException("Configura el piloto de Santander, dos destinatarios válidos y los secretos de Meta.");
    }

    public int TenantId { get; }
    public int BranchId { get; }
    public string? PhoneNumberId { get; }
    public string? CallbackUrl { get; }
    internal string? RecoveryWabaId { get; }
    internal string? RecoveryAccessToken { get; }
    internal bool TemporaryRecoveryToken { get; }

    internal static bool IsRecoveryCredential(string? credential) =>
        credential?.StartsWith(RecoveryCredentialPrefix, StringComparison.Ordinal) == true;

    internal static string ProtectRecoveryCredential(string token, long? expiresAt, IIntegrationSecretProtector protector)
    {
        var expiry = (expiresAt ?? 0).ToString(CultureInfo.InvariantCulture);
        return $"{RecoveryCredentialPrefix}{expiry}.{protector.Protect($"{expiry}.{token}")}";
    }

    internal string? UnprotectRecoveryCredential(string credential, IIntegrationSecretProtector protector, long now)
    {
        if (!IsRecoveryCredential(credential) || RecoveryAccessToken is null) return null;
        var start = RecoveryCredentialPrefix.Length;
        var separator = credential.IndexOf('.', start);
        if (separator <= start || !long.TryParse(credential.AsSpan(start, separator - start),
                NumberStyles.None, CultureInfo.InvariantCulture, out var expiry)
            || (TemporaryRecoveryToken ? expiry <= now : expiry != 0)) return null;
        try
        {
            var payload = protector.Unprotect(credential[(separator + 1)..]);
            var embeddedExpiry = credential[start..(separator + 1)];
            return payload.StartsWith(embeddedExpiry, StringComparison.Ordinal)
                && payload[embeddedExpiry.Length..] == RecoveryAccessToken ? RecoveryAccessToken : null;
        }
        catch (Exception error) when (error is CryptographicException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }
    public static bool IsLine(string? value) => NormalizePhone(value) == "573012274655";
    public bool AllowsRecipient(string? value) => NormalizePhone(value) is { } phone && _recipients.Contains(phone);

    public async Task<WhatsAppWebhookEnvelope[]> FilterWebhookAsync(IApplicationDbContext db,
        IReadOnlyList<WhatsAppWebhookEnvelope> envelopes, CancellationToken ct)
    {
        var onLine = envelopes.Where(x => x.PhoneNumberId == PhoneNumberId
            && (x.Field is "messages" or "smb_message_echoes")).ToArray();
        var echoes = onLine.Where(x => x.Field == "smb_message_echoes" && x.UserId is not null).ToArray();
        if (echoes.Length == 0) return onLine.Where(x => AllowsRecipient(x.PhoneNumber)).ToArray();

        var users = echoes.Select(x => x.UserId).Distinct().ToArray();
        var phones = echoes.Where(x => AllowsRecipient(x.PhoneNumber)).Select(x => x.PhoneNumber).Distinct().ToArray();
        var known = await db.WhatsAppConversations.AsNoTracking().Where(x => x.TenantId == TenantId
            && x.BranchId == BranchId && x.ChannelSettingId == null
            && (users.Contains(x.WhatsAppUserId) || phones.Contains(x.PhoneNumber)))
            .Select(x => new { x.Id, x.PhoneNumber, x.WhatsAppUserId }).ToListAsync(ct);

        return onLine.Where(x =>
        {
            if (x.Field != "smb_message_echoes" || x.UserId is null) return AllowsRecipient(x.PhoneNumber);
            var byUser = known.Find(contact => contact.WhatsAppUserId == x.UserId);
            if (x.PhoneNumber is null) return AllowsRecipient(byUser?.PhoneNumber);
            if (!AllowsRecipient(x.PhoneNumber)
                || (!string.IsNullOrEmpty(byUser?.PhoneNumber) && byUser.PhoneNumber != x.PhoneNumber)) return false;
            var byPhone = known.Find(contact => contact.PhoneNumber == x.PhoneNumber);
            return byPhone is null || (byUser is not null ? byPhone.Id == byUser.Id
                : string.IsNullOrEmpty(byPhone.WhatsAppUserId) || byPhone.WhatsAppUserId == x.UserId);
        }).ToArray();
    }

    public bool VerifySignature(string? signature, string payload) => WhatsAppWebhookSignature.IsValid(signature, payload, _appSecret);
    public bool VerifyChallenge(string token) => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.UTF8.GetBytes(token), System.Text.Encoding.UTF8.GetBytes(_verifyToken));

    public static void Configure(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("WhatsAppCoexistence:Pilot:Enabled")) return;
        var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        var localDatabase = connection.Host == "localhost" || connection.Host == "coexistence-pilot-db"
            || IPAddress.TryParse(connection.Host, out var address) && IPAddress.IsLoopback(address);
        if (!environment.IsDevelopment() || !localDatabase || connection.Database != "senor_arroz_coexistence_pilot"
            || configuration.GetValue<bool>("WhatsAppLocalReview:Enabled")
            || !string.Equals(configuration["WhatsAppProcessing:Mode"], "External", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El piloto de Santander requiere Development, PostgreSQL aislado y modo External.");

        var pilot = new WhatsAppCoexistencePilot(configuration);
        services.AddSingleton(pilot);
        foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType != null
            && x.ImplementationType != typeof(WhatsAppWebhookWorker)
            && x.ImplementationType != typeof(WhatsAppRealtimeRelay)
            && x.ImplementationType != typeof(WhatsAppProcessingMaintenance)
            && x.ImplementationType != typeof(WhatsAppCommerceOutboxWorker)).ToArray())
            services.Remove(descriptor);

        var businessUrl = new Uri(configuration["WhatsAppProcessing:BusinessApiUrl"] ?? "http://127.0.0.1:5089");
        if (!businessUrl.IsLoopback)
            throw new InvalidOperationException("La API comercial del piloto debe estar en loopback.");
        services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder =>
        {
            builder.PrimaryHandler.Dispose();
            builder.PrimaryHandler = new PilotTransportHandler(pilot, builder.Name, businessUrl,
                builder.Services.GetRequiredService<IIntegrationSecretProtector>())
            {
                InnerHandler = new HttpClientHandler { AllowAutoRedirect = false }
            };
        }));
    }

    private sealed class PilotTransportHandler(WhatsAppCoexistencePilot pilot, string? clientName, Uri businessUrl,
        IIntegrationSecretProtector protector) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri;
            if (uri is { IsAbsoluteUri: true } && uri.Scheme == businessUrl.Scheme && uri.Host == businessUrl.Host
                && uri.Port == businessUrl.Port && uri.AbsolutePath.StartsWith("/internal/whatsapp/", StringComparison.Ordinal))
                return await base.SendAsync(request, ct);

            if (uri is { Scheme: "https", Host: "graph.facebook.com", IsDefaultPort: true })
            {
                var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var bearer = request.Headers.Authorization?.Parameter;
                var recoveryCredential = IsRecoveryCredential(bearer);
                var rawRecoveryToken = bearer is not null && bearer == pilot.RecoveryAccessToken;
                if (clientName == nameof(WhatsAppCoexistenceClient) && (request.Method == HttpMethod.Get
                    || request.Method == HttpMethod.Post && (uri.AbsolutePath.EndsWith("/subscribed_apps", StringComparison.Ordinal)
                        || parts.Length == 2 && parts[1] == pilot.PhoneNumberId)))
                {
                    if ((!recoveryCredential && !rawRecoveryToken || AllowsRecoveryMetaOperation(parts, request.Method))
                        && (!recoveryCredential || TrySetRecoveryAuthorization(request, bearer!)))
                        return await base.SendAsync(request, ct);
                }
                if (clientName == nameof(IWhatsAppCloudClient) && !rawRecoveryToken && request.Method == HttpMethod.Post
                    && parts.Length == 3 && parts[1] == pilot.PhoneNumberId && parts[2] == "messages"
                    && string.IsNullOrEmpty(uri.Query) && request.Content is not null)
                {
                    using var payload = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                    if (payload.RootElement.TryGetProperty("to", out var recipient) && recipient.ValueKind == JsonValueKind.String
                        && pilot.AllowsRecipient(recipient.GetString()) && payload.RootElement.TryGetProperty("type", out var type)
                        && type.ValueKind == JsonValueKind.String
                        && (type.ValueEquals("text") || type.ValueEquals("interactive") && IsCommercialChoicePrompt(payload.RootElement))
                        && (!recoveryCredential || TrySetRecoveryAuthorization(request, bearer!)))
                        return await base.SendAsync(request, ct);
                }
            }
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                RequestMessage = request,
                Content = JsonContent.Create(new { error = new { message = "Transporte fuera del alcance del piloto de Santander." } })
            };
        }

        private static bool IsCommercialChoicePrompt(JsonElement payload)
        {
            if (!payload.TryGetProperty("interactive", out var interactive) || interactive.ValueKind != JsonValueKind.Object
                || !interactive.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !interactive.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.Object)
                return false;
            if (type.ValueEquals("button"))
                return action.TryGetProperty("buttons", out var buttons) && buttons.ValueKind == JsonValueKind.Array
                    && buttons.GetArrayLength() is >= 1 and <= 3 && buttons.EnumerateArray().All(button =>
                        button.ValueKind == JsonValueKind.Object && button.TryGetProperty("reply", out var reply) && IsCommercialChoiceRow(reply));
            return type.ValueEquals("list") && action.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array
                && sections.GetArrayLength() == 1 && sections[0].ValueKind == JsonValueKind.Object
                && sections[0].TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array
                && rows.GetArrayLength() is >= 1 and <= 10 && rows.EnumerateArray().All(IsCommercialChoiceRow);
        }

        private static bool IsCommercialChoiceRow(JsonElement row) =>
            row.ValueKind == JsonValueKind.Object && row.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            && WhatsAppCommercialDraft.ChoicePrefix(id.GetString()) is not null;

        private bool AllowsRecoveryMetaOperation(string[] parts, HttpMethod method) =>
            parts is [_, var account, "subscribed_apps"] && account == pilot.RecoveryWabaId
            || parts is [_, var waba, "phone_numbers"] && waba == pilot.RecoveryWabaId && method == HttpMethod.Get
            || parts is [_, var phone] && phone == pilot.PhoneNumberId;

        private bool TrySetRecoveryAuthorization(HttpRequestMessage request, string credential)
        {
            var token = pilot.UnprotectRecoveryCredential(credential, protector, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (token is null) return false;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return true;
        }
    }
}
