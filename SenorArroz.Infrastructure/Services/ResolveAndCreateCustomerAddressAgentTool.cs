using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class ResolveAndCreateCustomerAddressAgentTool(
    ApplicationDbContext db,
    RegisteredNeighborhoodResolver neighborhoods,
    GoogleAddressGeocoder geocoder,
    IWhatsAppSimpleOrderStateService states,
    RequestHumanAssistanceAgentTool humanAssistance,
    IClock clock) : IAgentTool
{
    public string Name => "resolve_and_create_customer_address";
    public string Description => "Valida con Google una dirección nueva del cliente identificado, resuelve un barrio activo de la sucursal, reutiliza o crea la dirección y la selecciona para el pedido. Nunca recibe IDs, coordenadas ni tarifas.";
    public string Category => "customer_address";
    public bool ModifiesData => true;
    public string RiskLevel => "high";
    public JsonElement ParametersSchema => JsonDocument.Parse(
        """{"type":"object","properties":{"address":{"type":"string","minLength":3,"maxLength":200},"neighborhood":{"type":"string","maxLength":150},"additionalInformation":{"type":"string","maxLength":150},"customerDoesNotKnowNeighborhood":{"type":"boolean"}},"required":["address","neighborhood","additionalInformation","customerDoesNotKnowNeighborhood"],"additionalProperties":false}""")
        .RootElement.Clone();

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryReadArguments(arguments, out var address, out var neighborhood, out var additionalInformation, out var doesNotKnowNeighborhood))
                return await Transfer(context, "Los datos de la nueva dirección son inconsistentes.", cancellationToken);

            var customer = context.CustomerId.HasValue
                ? await db.Customers.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.Id == context.CustomerId.Value
                    && x.BranchId == context.BranchId
                    && x.Active,
                    cancellationToken)
                : null;
            if (customer is null)
                return await Transfer(context, "No se pudo validar un cliente activo de la sucursal para crear la dirección.", cancellationToken);

            var geocoded = await ResolveExactAddress(address, context, cancellationToken);
            if (geocoded.Result is null)
                return geocoded.TransferResult!;

            var registeredNeighborhood = await ResolveNeighborhood(
                neighborhood,
                doesNotKnowNeighborhood,
                geocoded.Result,
                context,
                cancellationToken);
            if (registeredNeighborhood.Match is null)
                return registeredNeighborhood.TransferResult!;

            var normalized = NormalizeAddress(address);
            var existingRows = await db.Addresses
                .Where(x => x.CustomerId == customer.Id)
                .ToListAsync(cancellationToken);
            var selected = existingRows.FirstOrDefault(x =>
                NormalizeAddress(x.AddressText) == normalized
                || (!string.IsNullOrWhiteSpace(x.OriginalAddressText)
                    && NormalizeAddress(x.OriginalAddressText) == normalized));
            if (selected is not null && selected.NeighborhoodId != registeredNeighborhood.Match.Id)
                return await Transfer(context, "La dirección existente tiene un barrio diferente al validado para esta solicitud.", cancellationToken);

            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            var created = false;

            if (selected is null)
            {
                selected = new Address
                {
                    CustomerId = customer.Id,
                    NeighborhoodId = registeredNeighborhood.Match.Id,
                    AddressText = address,
                    OriginalAddressText = address,
                    NormalizedAddressText = normalized,
                    AdditionalInfo = additionalInformation,
                    DeliveryFee = registeredNeighborhood.Match.DeliveryFee,
                    Latitude = geocoded.Result.Latitude,
                    Longitude = geocoded.Result.Longitude,
                    IsPrimary = existingRows.Count == 0,
                    ValidationSource = "google_geocoding",
                    ValidatedAt = clock.UtcNow
                };
                db.Addresses.Add(selected);
                await db.SaveChangesAsync(cancellationToken);
                created = true;
            }

            var state = await states.LoadAsync(context.ConversationId, cancellationToken);
            state.SelectedAddressId = selected.Id;
            await states.SaveAsync(context.ConversationId, state, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return new(
                true,
                new
                {
                    addressId = selected.Id,
                    selected = true,
                    created,
                    reused = !created,
                    address = selected.AddressText,
                    neighborhood = registeredNeighborhood.Match.Name,
                    isPrimary = selected.IsPrimary
                },
                Code: created ? "customer_address_created_and_selected" : "customer_address_reused_and_selected",
                Message: created
                    ? "La dirección fue validada, guardada y seleccionada para el pedido."
                    : "La dirección ya existía y fue seleccionada para el pedido.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await Transfer(context, "Ocurrió una inconsistencia al validar o guardar la dirección.", cancellationToken);
        }
    }

    private async Task<(GeocodedAddress? Result, AgentToolExecutionResult? TransferResult)> ResolveExactAddress(
        string originalAddress,
        AgentToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var original = await geocoder.Resolve(originalAddress, null, null, cancellationToken);
        if (original.Result is { RequiresConfirmation: false })
            return (original.Result, null);
        if (original.Error?.Contains("no está configurado", StringComparison.OrdinalIgnoreCase) == true)
            return (null, await Transfer(context, original.Error, cancellationToken));

        var alternatives = BuildLastDigitAlternatives(originalAddress);
        if (alternatives.Count == 0)
            return (null, await Transfer(context, "Google no validó la dirección exacta y no fue posible construir un intento alternativo seguro.", cancellationToken));

        var exactAlternatives = new List<GeocodedAddress>();
        foreach (var alternative in alternatives)
        {
            var attempt = await geocoder.Resolve(alternative, null, null, cancellationToken);
            if (attempt.Result is { RequiresConfirmation: false })
                exactAlternatives.Add(attempt.Result);
        }

        if (exactAlternatives.Count == 0)
            return (null, await Transfer(context, "Google no pudo validar la dirección exacta, incluso con el intento alternativo.", cancellationToken));
        if (exactAlternatives.Count > 1
            && exactAlternatives.Skip(1).Any(x => !EquivalentGoogleResult(exactAlternatives[0], x)))
            return (null, await Transfer(context, "Los intentos alternativos de Google devolvieron direcciones diferentes.", cancellationToken));

        return (exactAlternatives[0], null);
    }

    private async Task<(NeighborhoodMatch? Match, AgentToolExecutionResult? TransferResult)> ResolveNeighborhood(
        string suppliedNeighborhood,
        bool doesNotKnowNeighborhood,
        GeocodedAddress geocoded,
        AgentToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        var source = doesNotKnowNeighborhood ? geocoded.Neighborhood : suppliedNeighborhood;
        if (string.IsNullOrWhiteSpace(source))
            return (null, await Transfer(context, "No fue posible determinar el barrio con seguridad.", cancellationToken));

        var resolution = await neighborhoods.Resolve(source, context.BranchId, cancellationToken);
        if (!resolution.Matched || resolution.RequiresConfirmation || resolution.Match is null)
            return (null, await Transfer(context, resolution.RequiresConfirmation
                ? "El barrio coincide con varias opciones registradas de la sucursal."
                : "El barrio no está registrado y activo en la sucursal.", cancellationToken));

        if (!doesNotKnowNeighborhood && !string.IsNullOrWhiteSpace(geocoded.Neighborhood))
        {
            var googleResolution = await neighborhoods.Resolve(geocoded.Neighborhood, context.BranchId, cancellationToken);
            if (!googleResolution.Matched
                || googleResolution.RequiresConfirmation
                || googleResolution.Match is null
                || googleResolution.Match.Id != resolution.Match.Id)
                return (null, await Transfer(context, "El barrio informado no coincide de forma segura con el barrio obtenido por Google.", cancellationToken));
        }

        return (resolution.Match, null);
    }

    private async Task<AgentToolExecutionResult> Transfer(
        AgentToolExecutionContext context,
        string reason,
        CancellationToken cancellationToken)
    {
        var arguments = JsonSerializer.SerializeToElement(new { reason });
        return await humanAssistance.ExecuteAsync(context, arguments, cancellationToken);
    }

    private static bool TryReadArguments(
        JsonElement arguments,
        out string address,
        out string neighborhood,
        out string? additionalInformation,
        out bool doesNotKnowNeighborhood)
    {
        address = string.Empty;
        neighborhood = string.Empty;
        additionalInformation = null;
        doesNotKnowNeighborhood = false;
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("address", out var addressElement)
            || addressElement.ValueKind != JsonValueKind.String
            || !arguments.TryGetProperty("neighborhood", out var neighborhoodElement)
            || neighborhoodElement.ValueKind != JsonValueKind.String
            || !arguments.TryGetProperty("additionalInformation", out var additionalElement)
            || additionalElement.ValueKind != JsonValueKind.String
            || !arguments.TryGetProperty("customerDoesNotKnowNeighborhood", out var unknownElement)
            || unknownElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;

        address = addressElement.GetString()?.Trim() ?? string.Empty;
        neighborhood = neighborhoodElement.GetString()?.Trim() ?? string.Empty;
        additionalInformation = NullIfEmpty(additionalElement.GetString());
        doesNotKnowNeighborhood = unknownElement.GetBoolean();
        return address.Length is >= 3 and <= 200
            && neighborhood.Length <= 150
            && (additionalInformation?.Length ?? 0) <= 150
            && (doesNotKnowNeighborhood || neighborhood.Length > 0);
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

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
