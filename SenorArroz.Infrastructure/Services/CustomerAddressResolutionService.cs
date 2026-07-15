using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed record CustomerAddressInput(
    string Address,
    string Neighborhood,
    string? AdditionalInformation,
    bool CustomerDoesNotKnowNeighborhood);

public sealed record ResolvedCustomerAddress(
    CustomerAddressInput Input,
    GeocodedAddress Geocoded,
    NeighborhoodMatch Neighborhood,
    string NormalizedAddress);

public sealed record CustomerAddressResolutionResult(ResolvedCustomerAddress? Address, string? Error)
{
    public bool Success => Address is not null;
}

public sealed record CustomerAddressPersistenceResult(Address? Address, bool Created, string? Error)
{
    public bool Success => Address is not null;
}

public sealed class CustomerAddressResolutionService(
    ApplicationDbContext db,
    RegisteredNeighborhoodResolver neighborhoods,
    GoogleAddressGeocoder geocoder,
    IClock clock)
{
    public async Task<CustomerAddressResolutionResult> ResolveAsync(
        CustomerAddressInput input,
        int branchId,
        CancellationToken cancellationToken)
    {
        var geocoded = await ResolveExactAddress(input.Address, cancellationToken);
        if (geocoded.Result is null)
            return new(null, geocoded.Error);

        var registeredNeighborhood = await ResolveNeighborhood(
            input.Neighborhood,
            input.CustomerDoesNotKnowNeighborhood,
            geocoded.Result,
            branchId,
            cancellationToken);
        if (registeredNeighborhood.Match is null)
            return new(null, registeredNeighborhood.Error);

        return new(new(
            input,
            geocoded.Result,
            registeredNeighborhood.Match,
            NormalizeAddress(input.Address)), null);
    }

    public async Task<CustomerAddressPersistenceResult> CreateOrReuseAsync(
        int customerId,
        ResolvedCustomerAddress resolved,
        CancellationToken cancellationToken)
    {
        var rows = await db.Addresses
            .Where(x => x.CustomerId == customerId)
            .ToListAsync(cancellationToken);
        var existing = rows.FirstOrDefault(x =>
            NormalizeAddress(x.AddressText) == resolved.NormalizedAddress
            || (!string.IsNullOrWhiteSpace(x.OriginalAddressText)
                && NormalizeAddress(x.OriginalAddressText) == resolved.NormalizedAddress));

        if (existing is not null)
        {
            return existing.NeighborhoodId == resolved.Neighborhood.Id
                ? new(existing, false, null)
                : new(null, false, "La dirección existente tiene un barrio diferente al validado para esta solicitud.");
        }

        var created = new Address
        {
            CustomerId = customerId,
            NeighborhoodId = resolved.Neighborhood.Id,
            AddressText = resolved.Input.Address,
            OriginalAddressText = resolved.Input.Address,
            NormalizedAddressText = resolved.NormalizedAddress,
            AdditionalInfo = resolved.Input.AdditionalInformation,
            DeliveryFee = resolved.Neighborhood.DeliveryFee,
            Latitude = resolved.Geocoded.Latitude,
            Longitude = resolved.Geocoded.Longitude,
            IsPrimary = rows.Count == 0,
            ValidationSource = "google_geocoding",
            ValidatedAt = clock.UtcNow
        };
        db.Addresses.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return new(created, true, null);
    }

    private async Task<(GeocodedAddress? Result, string? Error)> ResolveExactAddress(
        string originalAddress,
        CancellationToken cancellationToken)
    {
        var original = await geocoder.Resolve(originalAddress, null, null, cancellationToken);
        if (original.Result is { RequiresConfirmation: false })
            return (original.Result, null);
        if (original.Error?.Contains("no está configurado", StringComparison.OrdinalIgnoreCase) == true)
            return (null, original.Error);

        var alternatives = BuildLastDigitAlternatives(originalAddress);
        if (alternatives.Count == 0)
            return (null, "Google no validó la dirección exacta y no fue posible construir un intento alternativo seguro.");

        var exactAlternatives = new List<GeocodedAddress>();
        foreach (var alternative in alternatives)
        {
            var attempt = await geocoder.Resolve(alternative, null, null, cancellationToken);
            if (attempt.Result is { RequiresConfirmation: false })
                exactAlternatives.Add(attempt.Result);
        }

        if (exactAlternatives.Count == 0)
            return (null, "Google no pudo validar la dirección exacta, incluso con el intento alternativo.");
        if (exactAlternatives.Count > 1
            && exactAlternatives.Skip(1).Any(x => !EquivalentGoogleResult(exactAlternatives[0], x)))
            return (null, "Los intentos alternativos de Google devolvieron direcciones diferentes.");

        return (exactAlternatives[0], null);
    }

    private async Task<(NeighborhoodMatch? Match, string? Error)> ResolveNeighborhood(
        string suppliedNeighborhood,
        bool doesNotKnowNeighborhood,
        GeocodedAddress geocoded,
        int branchId,
        CancellationToken cancellationToken)
    {
        var source = doesNotKnowNeighborhood ? geocoded.Neighborhood : suppliedNeighborhood;
        if (string.IsNullOrWhiteSpace(source))
            return (null, "No fue posible determinar el barrio con seguridad.");

        var resolution = await neighborhoods.Resolve(source, branchId, cancellationToken);
        if (!resolution.Matched || resolution.RequiresConfirmation || resolution.Match is null)
            return (null, resolution.RequiresConfirmation
                ? "El barrio coincide con varias opciones registradas de la sucursal."
                : "El barrio no está registrado y activo en la sucursal.");

        if (!doesNotKnowNeighborhood && !string.IsNullOrWhiteSpace(geocoded.Neighborhood))
        {
            var googleResolution = await neighborhoods.Resolve(geocoded.Neighborhood, branchId, cancellationToken);
            if (!googleResolution.Matched
                || googleResolution.RequiresConfirmation
                || googleResolution.Match is null
                || googleResolution.Match.Id != resolution.Match.Id)
                return (null,
                    "El barrio informado no coincide de forma segura con el barrio obtenido por Google. "
                    + $"Comparación: cliente=\"{DiagnosticValue(suppliedNeighborhood)}\" => {DescribeResolution(resolution)}; "
                    + $"Google=\"{DiagnosticValue(geocoded.Neighborhood)}\" => {DescribeResolution(googleResolution)}.");
        }

        return (resolution.Match, null);
    }

    private static string DescribeResolution(NeighborhoodResolution resolution)
    {
        if (resolution.Match is not null)
            return $"registrado=\"{DiagnosticValue(resolution.Match.Name)}\" (id {resolution.Match.Id})";
        if (resolution.RequiresConfirmation && resolution.Options.Count > 0)
            return $"ambiguo entre [{string.Join(", ", resolution.Options.Select(x => $"\"{DiagnosticValue(x.Name)}\" (id {x.Id})"))}]";
        return "sin coincidencia activa en la sucursal";
    }

    private static string DiagnosticValue(string? value)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized[..Math.Min(150, normalized.Length)];
    }

    internal static IReadOnlyList<string> BuildLastDigitAlternatives(string address)
    {
        var match = Regex.Match(address, @"\d(?!.*\d)");
        if (!match.Success) return [];
        var digit = address[match.Index] - '0';
        var alternatives = new List<string>(2);
        if (digit > 0)
            alternatives.Add(address[..match.Index] + (digit - 1).ToString(CultureInfo.InvariantCulture) + address[(match.Index + 1)..]);
        if (digit < 9)
            alternatives.Add(address[..match.Index] + (digit + 1).ToString(CultureInfo.InvariantCulture) + address[(match.Index + 1)..]);
        return alternatives;
    }

    internal static string NormalizeAddress(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        var tokens = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x switch
            {
                "carrera" or "cra" or "cr" => "kr",
                "calle" or "cl" => "cl",
                "avenida" or "av" => "av",
                "numero" or "nro" or "no" => string.Empty,
                _ => x
            })
            .Where(x => x.Length > 0);
        return string.Join(' ', tokens);
    }

    private static bool EquivalentGoogleResult(GeocodedAddress left, GeocodedAddress right) =>
        NormalizeAddress(left.FormattedAddress) == NormalizeAddress(right.FormattedAddress)
        && RegisteredNeighborhoodResolver.Normalize(left.Neighborhood ?? string.Empty)
            == RegisteredNeighborhoodResolver.Normalize(right.Neighborhood ?? string.Empty)
        && Math.Abs(left.Latitude - right.Latitude) <= 0.000001m
        && Math.Abs(left.Longitude - right.Longitude) <= 0.000001m;
}
