using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public record NeighborhoodMatch(int Id, string Name, string BranchName, int DeliveryFee, bool RequiresBranchReassignment);
public record NeighborhoodResolution(bool Matched, bool RequiresConfirmation, NeighborhoodMatch? Match, IReadOnlyList<NeighborhoodMatch> Options, string? SuggestedQuestion);

public class RegisteredNeighborhoodResolver(ApplicationDbContext db)
{
    public async Task<NeighborhoodResolution> Resolve(string query, int conversationBranchId, CancellationToken ct)
    {
        var sought = Normalize(query);
        if (sought.Length < 2)
            return new(false, false, null, [], null);

        var rows = await db.Neighborhoods
            .AsNoTracking()
            .Where(x => x.Active && x.BranchId == conversationBranchId)
            .Include(x => x.Branch)
            .Select(x => new { x.Id, x.Name, BranchName = x.Branch.Name, x.DeliveryFee })
            .ToListAsync(ct);
        var ranked = rows
            .Select(x => new { Row = x, Score = Score(sought, Normalize(x.Name)) })
            .Where(x => x.Score >= .68)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Row.Name)
            .Take(5)
            .ToList();
        if (ranked.Count == 0)
            return new(false, false, null, [], null);

        var options = ranked
            .Select(x => new NeighborhoodMatch(x.Row.Id, x.Row.Name, x.Row.BranchName, x.Row.DeliveryFee, false))
            .ToList();
        var safe = ranked[0].Score >= .82
            && (ranked.Count == 1 || ranked[0].Score - ranked[1].Score >= .12);
        return safe
            ? new(true, false, options[0], [], null)
            : new(false, true, null, options.Take(3).ToList(), $"¿Te encuentras en {string.Join(" o ", options.Take(3).Select(x => x.Name))}?");
    }

    internal static string Normalize(string value)
    {
        var s = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        foreach (var c in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                b.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return string.Join(' ', b.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x is not ("barrio" or "sector" or "por" or "en" or "vivo" or "estoy" or "para" or "es")));
    }

    private static double Score(string a, string b)
    {
        if (a == b) return 1;
        if (b.Contains(a) || a.Contains(b)) return .91;
        return 1d - (double)Levenshtein(a, b) / Math.Max(a.Length, b.Length);
    }

    private static int Levenshtein(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var previous = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var old = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = old;
            }
        }
        return row[b.Length];
    }
}

public record GeocodedAddress(
    string FormattedAddress,
    decimal Latitude,
    decimal Longitude,
    string? Neighborhood,
    string Quality,
    bool RequiresConfirmation);

public class GoogleAddressGeocoder(HttpClient http, IOptions<GoogleMapsRouteOptions> options)
{
    public async Task<(GeocodedAddress? Result, string? Error)> Resolve(
        string? address,
        decimal? latitude,
        decimal? longitude,
        CancellationToken ct)
    {
        var key = options.Value.GeocodingApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return (null, "Google Maps Geocoding no está configurado.");

        var target = latitude.HasValue && longitude.HasValue
            ? $"latlng={latitude.Value.ToString(CultureInfo.InvariantCulture)},{longitude.Value.ToString(CultureInfo.InvariantCulture)}"
            : $"address={Uri.EscapeDataString(address ?? string.Empty)}";
        using var response = await http.GetAsync(
            $"https://maps.googleapis.com/maps/api/geocode/json?{target}&key={Uri.EscapeDataString(key)}&language=es&region=co",
            ct);
        if (!response.IsSuccessStatusCode)
            return (null, "No fue posible consultar Google Maps.");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var status = doc.RootElement.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;
        if (string.Equals(status, "REQUEST_DENIED", StringComparison.OrdinalIgnoreCase))
            return (null, "Google Maps rechazó la credencial de Geocoding. Verifica que la API esté habilitada, tenga facturación y restricciones compatibles con el servidor.");
        if (string.Equals(status, "OVER_DAILY_LIMIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "OVER_QUERY_LIMIT", StringComparison.OrdinalIgnoreCase))
            return (null, "Google Maps Geocoding alcanzó su cuota o tiene un problema de facturación.");
        if (string.Equals(status, "UNKNOWN_ERROR", StringComparison.OrdinalIgnoreCase))
            return (null, "Google Maps Geocoding no estuvo disponible temporalmente.");
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return (null, "Google Maps no encontró la dirección.");

        var first = results[0];
        if (!first.TryGetProperty("geometry", out var geometry)
            || !geometry.TryGetProperty("location", out var location)
            || !location.TryGetProperty("lat", out var lat)
            || !location.TryGetProperty("lng", out var lng))
            return (null, "Google Maps devolvió una respuesta incompleta.");

        var resultTypes = first.TryGetProperty("types", out var typesElement)
            ? typesElement.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var componentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? neighborhood = null;
        if (first.TryGetProperty("address_components", out var components))
        {
            foreach (var component in components.EnumerateArray())
            {
                if (!component.TryGetProperty("types", out var componentTypesElement)) continue;
                var currentTypes = componentTypesElement.EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => x is not null)
                    .Cast<string>()
                    .ToList();
                componentTypes.UnionWith(currentTypes);
                if (neighborhood is null
                    && currentTypes.Any(x => x is "neighborhood" or "sublocality" or "sublocality_level_1")
                    && component.TryGetProperty("long_name", out var longName))
                    neighborhood = longName.GetString();
            }
        }

        var formattedAddress = first.TryGetProperty("formatted_address", out var formatted)
            ? formatted.GetString()?.Trim() ?? string.Empty
            : string.Empty;
        var partial = first.TryGetProperty("partial_match", out var partialElement) && partialElement.GetBoolean();
        var locationType = geometry.TryGetProperty("location_type", out var locationTypeElement)
            ? locationTypeElement.GetString()
            : null;
        var exact = !partial
            && !resultTypes.Contains("route")
            && string.Equals(locationType, "ROOFTOP", StringComparison.OrdinalIgnoreCase)
            && componentTypes.Contains("route")
            && componentTypes.Contains("street_number")
            && !string.IsNullOrWhiteSpace(formattedAddress);
        var quality = exact ? "exact" : partial
            ? "partial_match"
            : resultTypes.Contains("route")
                ? "route"
                : locationType ?? "incomplete";

        return (new(
            formattedAddress,
            lat.GetDecimal(),
            lng.GetDecimal(),
            neighborhood,
            quality,
            !exact), null);
    }
}
