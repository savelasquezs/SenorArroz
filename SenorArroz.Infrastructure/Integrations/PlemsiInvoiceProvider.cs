using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class PlemsiInvoiceProvider(HttpClient http, IIntegrationSecretProtector secrets, IConfiguration? configuration = null) : IElectronicInvoiceProvider
{
    public string Code => "plemsi";

    private async Task<JsonElement> CallAsync(ElectronicInvoiceIntegration integration, HttpMethod method, string path, string? payload, CancellationToken ct)
    {
        if (integration.Environment == "production" && method != HttpMethod.Get && configuration?.GetValue<bool>("ElectronicInvoicing:ProductionEnabled") != true)
            throw new BusinessException("La emisión y los cambios en Plemsi producción están desactivados en este servidor.");
        var host = integration.Environment == "production" ? "https://api.plemsi.com" : "https://pruebas.plemsi.com";
        using var request = new HttpRequestMessage(method, host + "/api/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secrets.Unprotect(integration.EncryptedToken));
        if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.NotFound && method == HttpMethod.Get) throw new KeyNotFoundException();
        if (!response.IsSuccessStatusCode)
            throw new PlemsiRequestException(response.StatusCode, body);
        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            throw new PlemsiRequestException(HttpStatusCode.BadRequest, body);
        return json.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await http.SendAsync(request, ct); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new PlemsiRequestException(HttpStatusCode.GatewayTimeout, "{\"error\":\"Plemsi tardó en responder. Consulta el mismo documento antes de reintentar.\"}"); }
        catch (HttpRequestException)
        { throw new PlemsiRequestException(HttpStatusCode.ServiceUnavailable, "{\"error\":\"Plemsi está temporalmente indisponible. Intenta nuevamente más tarde.\"}"); }
    }

    public Task<JsonElement> CompanyAsync(ElectronicInvoiceIntegration i, CancellationToken ct) => CallAsync(i, HttpMethod.Get, "common/company", null, ct);
    public Task<JsonElement> ResolutionsAsync(ElectronicInvoiceIntegration i, CancellationToken ct) => CallAsync(i, HttpMethod.Get, "billing/resolution?perPage=100&page=1", null, ct);
    public Task<JsonElement> RemainingAsync(ElectronicInvoiceIntegration i, string resolution, CancellationToken ct) =>
        CallAsync(i, HttpMethod.Get, "billing/resolution/remaining-numbers/" + Uri.EscapeDataString(resolution), null, ct);
    public Task<JsonElement> LookupRecipientAsync(ElectronicInvoiceIntegration i, int type, string identification, CancellationToken ct) =>
        CallAsync(i, HttpMethod.Get, $"billing/customer/dian/basic-info/{type}/{Uri.EscapeDataString(identification)}", null, ct);

    public async Task SyncEmailAsync(ElectronicInvoiceIntegration i, CancellationToken ct)
    {
        var company = await CompanyAsync(i, ct);
        var data = company.TryGetProperty("data", out var nested) ? nested : company;
        if (!data.TryGetProperty("email", out var email) || email.ValueKind != JsonValueKind.Object)
            throw new BusinessException("No se pudo leer la configuración actual de correos de Plemsi. No se ha modificado.");
        var emails = JsonNode.Parse(email.GetRawText())!.AsObject();
        emails["billing"] = i.BillingEmail;
        await CallAsync(i, HttpMethod.Put, "common/company/update", new JsonObject { ["email"] = emails }.ToJsonString(), ct);
    }

    public Task<JsonElement> SubmitAsync(ElectronicInvoiceIntegration i, string kind, string payload, CancellationToken ct) =>
        CallAsync(i, HttpMethod.Post, $"billing/{(kind == "credit" ? "credit" : "invoice")}", payload, ct);

    public async Task<JsonElement?> FindAsync(ElectronicInvoiceIntegration i, string kind, string number, CancellationToken ct)
    {
        try { return await CallAsync(i, HttpMethod.Get, $"billing/{(kind == "credit" ? "credit" : "invoice")}/one?by=number&value={Uri.EscapeDataString(number)}", null, ct); }
        catch (KeyNotFoundException) { return null; }
    }

    public async Task SendEmailAsync(ElectronicInvoiceIntegration i, string reference, string email, CancellationToken ct) =>
        await CallAsync(i, HttpMethod.Post, "billing/general/utility/send-email", JsonSerializer.Serialize(new { cude = reference, targetEmail = email }), ct);

    public async Task<byte[]> DownloadAsync(ElectronicInvoiceIntegration i, string kind, string reference, string format, CancellationToken ct)
    {
        var result = await CallAsync(i, HttpMethod.Get, $"billing/{(kind == "credit" ? "credit" : "invoice")}/{format}/{Uri.EscapeDataString(reference)}", null, ct);
        var data = result.GetProperty("data");
        var value = data.ValueKind == JsonValueKind.String ? data.GetString() : FindString(data, "base64", "b64data", "content", "xml", "pdf");
        if (string.IsNullOrWhiteSpace(value)) throw new BusinessException("Plemsi no devolvió el archivo solicitado.");
        return Convert.FromBase64String(value);
    }

    public static string? FindString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
            foreach (var p in element.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString();
        foreach (var p in element.EnumerateObject())
        {
            var found = FindString(p.Value, names);
            if (found is not null) return found;
        }
        return null;
    }

    public static string? CompanyTaxId(JsonElement company)
    {
        var data = company.TryGetProperty("data", out var nested) ? nested : company;
        if (data.ValueKind != JsonValueKind.Object) return null;
        if (data.TryGetProperty("dni", out var dni) && dni.ValueKind == JsonValueKind.Object
            && dni.TryGetProperty("number", out var number) && number.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            return number.ToString();
        return FindString(data, "identification_number", "nit", "tax_id");
    }

    public static bool? DianValidation(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in element.EnumerateObject())
        {
            if (p.Name.Equals("IsValid", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("is_valid", StringComparison.OrdinalIgnoreCase))
            {
                if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) return p.Value.GetBoolean();
                if (p.Value.ValueKind == JsonValueKind.String && bool.TryParse(p.Value.GetString(), out var value)) return value;
            }
            var nested = DianValidation(p.Value);
            if (nested.HasValue) return nested;
        }
        return null;
    }

    public static string? DataValidationError(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;
        try
        {
            using var json = JsonDocument.Parse(response);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.String
                || !info.GetString()!.EndsWith(": Invalid data structure", StringComparison.Ordinal)
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() == 0 || DianValidation(root).HasValue
                || !string.IsNullOrWhiteSpace(FindString(root, "cufe", "cude", "uuid"))) return null;
            var errors = new List<string>();
            foreach (var error in data.EnumerateArray())
            {
                if (error.ValueKind != JsonValueKind.Object
                    || !error.TryGetProperty("field", out var field) || field.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(field.GetString())
                    || !error.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(reason.GetString())) return null;
                errors.Add($"{field.GetString()}: {reason.GetString()}");
            }
            return "Plemsi rechazó la estructura de los datos antes de emitir: " + string.Join("; ", errors.Distinct());
        }
        catch (JsonException) { return null; }
    }
}

public sealed class PlemsiRequestException(HttpStatusCode status, string response) : HttpRequestException(
    status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || response.Contains("Api Auth Validation Error", StringComparison.OrdinalIgnoreCase)
        ? "Plemsi rechazó la credencial. Verifica con soporte el token de esta empresa y su ambiente."
        : status == HttpStatusCode.TooManyRequests || (int)status >= 500
            ? "Plemsi está temporalmente indisponible. Intenta nuevamente en unos minutos."
            : $"Plemsi rechazó la consulta (HTTP {(int)status}). Verifica los datos y la numeración configurada.", null, status)
{
    public string ProviderResponse { get; } = response;
}
