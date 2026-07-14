using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class ResolveAndCreateCustomerAddressAgentTool(
    ApplicationDbContext db,
    CustomerAddressResolutionService addressResolution,
    IWhatsAppSimpleOrderStateService states,
    RequestHumanAssistanceAgentTool humanAssistance) : IAgentTool
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
            if (!TryReadArguments(arguments, out var input))
                return await Transfer(context, "Los datos de la nueva dirección son inconsistentes.", cancellationToken);

            var customerExists = context.CustomerId.HasValue
                && await db.Customers.AsNoTracking().AnyAsync(x =>
                    x.Id == context.CustomerId.Value
                    && x.BranchId == context.BranchId
                    && x.Active,
                    cancellationToken);
            if (!customerExists)
                return await Transfer(context, "No se pudo validar un cliente activo de la sucursal para crear la dirección.", cancellationToken);

            var resolved = await addressResolution.ResolveAsync(input, context.BranchId, cancellationToken);
            if (!resolved.Success)
                return await Transfer(context, resolved.Error!, cancellationToken);

            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            var persisted = await addressResolution.CreateOrReuseAsync(
                context.CustomerId!.Value,
                resolved.Address!,
                cancellationToken);
            if (!persisted.Success)
                throw new InvalidOperationException(persisted.Error);

            var state = await states.LoadAsync(context.ConversationId, cancellationToken);
            state.OrderType = OrderType.Delivery;
            state.SelectedAddressId = persisted.Address!.Id;
            state.Activities.Add(new()
            {
                Type = "address",
                Message = $"Validó y seleccionó la dirección {persisted.Address.AddressText}, {resolved.Address!.Neighborhood.Name}.",
                Timestamp = DateTime.UtcNow
            });
            await states.SaveAsync(context.ConversationId, state, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return new(
                true,
                new
                {
                    addressId = persisted.Address.Id,
                    selected = true,
                    created = persisted.Created,
                    reused = !persisted.Created,
                    address = persisted.Address.AddressText,
                    neighborhood = resolved.Address!.Neighborhood.Name,
                    isPrimary = persisted.Address.IsPrimary,
                    orderType = "delivery"
                },
                Code: persisted.Created ? "customer_address_created_and_selected" : "customer_address_reused_and_selected",
                Message: persisted.Created
                    ? "La dirección fue validada, guardada y seleccionada para el pedido."
                    : "La dirección ya existía y fue seleccionada para el pedido.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            db.ChangeTracker.Clear();
            return await Transfer(context, "Ocurrió una inconsistencia al validar o guardar la dirección.", cancellationToken);
        }
    }

    private async Task<AgentToolExecutionResult> Transfer(
        AgentToolExecutionContext context,
        string reason,
        CancellationToken cancellationToken) =>
        await humanAssistance.ExecuteAsync(
            context,
            JsonSerializer.SerializeToElement(new { reason }),
            cancellationToken);

    private static bool TryReadArguments(JsonElement arguments, out CustomerAddressInput input)
    {
        input = new(string.Empty, string.Empty, null, false);
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

        var address = addressElement.GetString()?.Trim() ?? string.Empty;
        var neighborhood = neighborhoodElement.GetString()?.Trim() ?? string.Empty;
        var additionalInformation = string.IsNullOrWhiteSpace(additionalElement.GetString())
            ? null
            : additionalElement.GetString()!.Trim();
        var doesNotKnowNeighborhood = unknownElement.GetBoolean();
        if (address.Length is < 3 or > 200
            || neighborhood.Length > 150
            || (additionalInformation?.Length ?? 0) > 150
            || (!doesNotKnowNeighborhood && neighborhood.Length == 0))
            return false;

        input = new(address, neighborhood, additionalInformation, doesNotKnowNeighborhood);
        return true;
    }
}
