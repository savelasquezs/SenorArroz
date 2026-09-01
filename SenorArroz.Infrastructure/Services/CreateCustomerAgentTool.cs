using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class CreateCustomerAgentTool(
    ApplicationDbContext db,
    CustomerAddressResolutionService addressResolution,
    IWhatsAppSimpleOrderStateService states,
    RequestHumanAssistanceAgentTool humanAssistance) : IAgentTool
{
    public string Name => "create_customer";
    public string Description => "Crea o vincula de forma segura al cliente de la conversación usando su nombre. Sin dirección configura el pedido para recoger; con dirección completa crea o reutiliza la dirección y configura domicilio en una sola transacción.";
    public string Category => "customer";
    public bool ModifiesData => true;
    public string RiskLevel => "high";
    public JsonElement ParametersSchema => JsonDocument.Parse(
        """{"type":"object","properties":{"name":{"type":"string","minLength":2,"maxLength":150},"address":{"type":"string","minLength":3,"maxLength":200},"neighborhood":{"type":"string","maxLength":150},"additionalInformation":{"type":"string","maxLength":150},"customerDoesNotKnowNeighborhood":{"type":"boolean"}},"required":["name"],"additionalProperties":false}""")
        .RootElement.Clone();

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolExecutionContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        if (!TryReadArguments(arguments, out var request, out var missingAddressData))
        {
            if (missingAddressData)
            {
                return new(
                    false,
                    null,
                    "Faltan datos para crear la dirección.",
                    "customer_address_data_required",
                    RequiresUserInput: true,
                    SuggestedQuestion: "Regálame la dirección completa y el barrio, por favor. Si no conoces el barrio, dímelo.");
            }

            return await Transfer(context, "El nombre o los datos proporcionados para crear el cliente no son válidos.", cancellationToken);
        }

        var conversation = await db.WhatsAppConversations.FirstOrDefaultAsync(x =>
            x.Id == context.ConversationId && x.BranchId == context.BranchId,
            cancellationToken);
        if (conversation is null
            || (!string.IsNullOrWhiteSpace(context.PhoneNumber)
                && !string.Equals(conversation.PhoneNumber, context.PhoneNumber, StringComparison.Ordinal)))
        {
            return await Transfer(context, "La conversación no coincide con la identidad y la sucursal del contexto seguro.", cancellationToken);
        }

        var phone = NormalizePhone(conversation.PhoneNumber);
        var userId = WhatsAppIdentityNormalizer.NormalizeUserId(conversation.WhatsAppUserId);
        if (phone is null && userId is null)
            return await Transfer(context, "La conversación no tiene una identidad de WhatsApp válida.", cancellationToken);

        ResolvedCustomerAddress? resolvedAddress = null;
        if (request.Address is not null)
        {
            var resolved = await addressResolution.ResolveAsync(request.Address, context.BranchId, cancellationToken);
            if (!resolved.Success)
                return await Transfer(context, resolved.Error!, cancellationToken);
            resolvedAddress = resolved.Address;
        }

        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var customerByUserId = userId is null
                ? null
                : await db.Customers
                    .Where(x => x.BranchId == context.BranchId && x.WhatsAppUserId == userId)
                    .OrderByDescending(x => x.Active)
                    .ThenBy(x => x.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            var phoneMatches = phone is null
                ? new List<Customer>()
                : await db.Customers
                    .Where(x => x.BranchId == context.BranchId && (x.Phone1 == phone || x.Phone2 == phone))
                    .OrderByDescending(x => x.Active)
                    .ThenBy(x => x.Id)
                    .ToListAsync(cancellationToken);
            if (phoneMatches.Count > 1)
                throw new InvalidOperationException("Existe un conflicto entre clientes con el mismo teléfono en la sucursal.");

            var customerByPhone = phoneMatches.SingleOrDefault();
            if (customerByUserId is not null && customerByPhone is not null && customerByUserId.Id != customerByPhone.Id)
                throw new InvalidOperationException("El BSUID y el teléfono pertenecen a clientes diferentes.");

            var customer = customerByUserId ?? customerByPhone;
            var created = customer is null;
            var reactivated = customer is { Active: false };
            if (customer is null)
            {
                customer = new Customer
                {
                    BranchId = context.BranchId,
                    Name = request.Name,
                    Phone1 = phone,
                    WhatsAppUserId = userId,
                    WhatsAppUsername = conversation.WhatsAppUsername,
                    Active = true
                };
                db.Customers.Add(customer);
            }
            else if (!customer.Active)
            {
                customer.Active = true;
                customer.Name = request.Name;
            }

            if (userId is not null
                && (string.IsNullOrWhiteSpace(customer.WhatsAppUserId)
                    || string.Equals(customer.WhatsAppUserId, userId, StringComparison.Ordinal)))
            {
                customer.WhatsAppUserId = userId;
                customer.WhatsAppUsername = conversation.WhatsAppUsername ?? customer.WhatsAppUsername;
            }
            if (string.IsNullOrWhiteSpace(customer.Phone1) && phone is not null)
                customer.Phone1 = phone;

            await db.SaveChangesAsync(cancellationToken);
            conversation.CustomerId = customer.Id;
            conversation.ContactName = customer.Name;

            Address? selectedAddress = null;
            var addressCreated = false;
            var state = await states.LoadAsync(context.ConversationId, cancellationToken);
            if (resolvedAddress is null)
            {
                state.OrderType = OrderType.Onsite;
                state.SelectedAddressId = null;
                state.Activities.Add(new()
                {
                    Type = "customer",
                    Message = $"Vinculó a {customer.Name} y configuró el pedido para recoger en el local.",
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                var persisted = await addressResolution.CreateOrReuseAsync(
                    customer.Id,
                    resolvedAddress,
                    cancellationToken);
                if (!persisted.Success)
                    throw new InvalidOperationException(persisted.Error);
                selectedAddress = persisted.Address;
                addressCreated = persisted.Created;
                state.OrderType = OrderType.Delivery;
                state.SelectedAddressId = selectedAddress!.Id;
                state.Activities.Add(new()
                {
                    Type = "customer_address",
                    Message = $"Vinculó a {customer.Name} y seleccionó {selectedAddress.AddressText} para domicilio.",
                    Timestamp = DateTime.UtcNow
                });
            }

            await states.SaveAsync(context.ConversationId, state, cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return new(
                true,
                new
                {
                    customerId = customer.Id,
                    name = customer.Name,
                    created,
                    reused = !created && !reactivated,
                    reactivated,
                    orderType = state.OrderType == OrderType.Delivery ? "delivery" : "onsite",
                    addressId = selectedAddress?.Id,
                    addressCreated,
                    addressReused = selectedAddress is not null && !addressCreated,
                    address = selectedAddress?.AddressText,
                    neighborhood = resolvedAddress?.Neighborhood?.Name
                },
                Code: resolvedAddress is null
                    ? "customer_ready_for_pickup"
                    : "customer_and_address_ready_for_delivery",
                Message: resolvedAddress is null
                    ? "El cliente quedó vinculado y el pedido configurado para recoger en el local."
                    : "El cliente y la dirección quedaron vinculados y el pedido configurado para domicilio.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            db.ChangeTracker.Clear();
            return await Transfer(context, "Ocurrió una inconsistencia al crear o vincular el cliente.", cancellationToken);
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

    private static bool TryReadArguments(
        JsonElement arguments,
        out CreateCustomerRequest request,
        out bool missingAddressData)
    {
        request = new(string.Empty, null);
        missingAddressData = false;
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
            return false;

        var name = Regex.Replace(nameElement.GetString()?.Trim() ?? string.Empty, @"\s+", " ");
        if (name.Length is < 2 or > 150
            || !name.Any(char.IsLetter)
            || name.Any(x => !(char.IsLetter(x) || char.IsWhiteSpace(x) || x is '-' or '\'')))
            return false;

        var hasAddress = arguments.TryGetProperty("address", out var addressElement);
        var hasNeighborhood = arguments.TryGetProperty("neighborhood", out var neighborhoodElement);
        var hasAdditional = arguments.TryGetProperty("additionalInformation", out var additionalElement);
        var hasUnknown = arguments.TryGetProperty("customerDoesNotKnowNeighborhood", out var unknownElement);
        var hasAnyAddressData = hasAddress || hasNeighborhood || hasAdditional || hasUnknown;
        if (!hasAnyAddressData)
        {
            request = new(name, null);
            return true;
        }

        if ((hasAddress && addressElement.ValueKind != JsonValueKind.String)
            || (hasNeighborhood && neighborhoodElement.ValueKind != JsonValueKind.String)
            || (hasAdditional && additionalElement.ValueKind != JsonValueKind.String)
            || (hasUnknown && unknownElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
            return false;

        var address = hasAddress ? addressElement.GetString()?.Trim() ?? string.Empty : string.Empty;
        var neighborhood = hasNeighborhood ? neighborhoodElement.GetString()?.Trim() ?? string.Empty : string.Empty;
        var additionalInformation = hasAdditional && !string.IsNullOrWhiteSpace(additionalElement.GetString())
            ? additionalElement.GetString()!.Trim()
            : null;
        var doesNotKnowNeighborhood = hasUnknown && unknownElement.GetBoolean();
        if (address.Length is < 3 or > 200
            || neighborhood.Length > 150
            || (additionalInformation?.Length ?? 0) > 150
            || (!doesNotKnowNeighborhood && neighborhood.Length == 0))
        {
            missingAddressData = address.Length < 3 || (!doesNotKnowNeighborhood && neighborhood.Length == 0);
            return false;
        }

        request = new(name, new(address, neighborhood, additionalInformation, doesNotKnowNeighborhood));
        return true;
    }

    private static string? NormalizePhone(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 10) return null;
        var phone = digits[^10..];
        return phone.Length == 10 ? phone : null;
    }

    private sealed record CreateCustomerRequest(string Name, CustomerAddressInput? Address);
}
