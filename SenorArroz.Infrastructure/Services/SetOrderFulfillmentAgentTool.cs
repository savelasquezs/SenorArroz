using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class SetOrderFulfillmentAgentTool(
    ApplicationDbContext db,
    IWhatsAppSimpleOrderStateService states) : IAgentTool
{
    public string Name => "set_order_fulfillment";
    public string Description => "Actualiza el draft del pedido cuando el cliente elige recoger, confirma una dirección guardada o rechaza la dirección actual mientras informa una nueva. Los IDs se validan contra el cliente y la sucursal del contexto seguro.";
    public string Category => "order";
    public bool ModifiesData => true;
    public string RiskLevel => "medium";
    public JsonElement ParametersSchema => JsonDocument.Parse(
        """{"type":"object","properties":{"orderType":{"type":"string","enum":["onsite","delivery"]},"addressId":{"type":"integer","minimum":1}},"required":["orderType"],"additionalProperties":false}""")
        .RootElement.Clone();

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var orderType = arguments.GetProperty("orderType").GetString();
        var addressId = arguments.TryGetProperty("addressId", out var addressElement)
            && addressElement.TryGetInt32(out var parsedAddressId)
            ? parsedAddressId
            : (int?)null;
        if (orderType is not ("onsite" or "delivery"))
            return new(false, null, "Tipo de pedido inválido.", "invalid_arguments");

        string activity;
        if (orderType == "onsite")
        {
            addressId = null;
            activity = "Configuró el pedido para recoger en el local.";
        }
        else if (addressId.HasValue)
        {
            if (!context.CustomerId.HasValue)
                return new(false, null, "No hay un cliente vinculado para seleccionar la dirección.", "customer_required");
            var address = await db.Addresses.AsNoTracking()
                .Include(x => x.Neighborhood)
                .FirstOrDefaultAsync(x => x.Id == addressId.Value
                    && x.CustomerId == context.CustomerId.Value
                    && x.Customer.BranchId == context.BranchId,
                    cancellationToken);
            if (address is null)
                return new(false, null, "La dirección no pertenece al cliente y la sucursal del contexto.", "address_not_found");
            activity = $"El cliente confirmó la dirección {address.AddressText}, {address.Neighborhood.Name}.";
        }
        else
        {
            activity = "El cliente rechazó la dirección propuesta; el domicilio quedó pendiente de una dirección válida.";
        }

        var state = await states.LoadAsync(context.ConversationId, cancellationToken);
        state.OrderType = orderType == "onsite" ? OrderType.Onsite : OrderType.Delivery;
        state.SelectedAddressId = addressId;
        state.Activities.Add(new() { Type = "fulfillment", Message = activity, Timestamp = DateTime.UtcNow });
        await states.SaveAsync(context.ConversationId, state, cancellationToken);
        return new(true, new
        {
            orderType,
            selectedAddressId = addressId,
            addressPending = orderType == "delivery" && !addressId.HasValue
        }, Code: "order_fulfillment_updated", Message: activity);
    }
}
