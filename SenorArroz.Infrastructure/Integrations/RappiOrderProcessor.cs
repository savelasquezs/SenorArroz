using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class RappiOrderProcessor(
    IApplicationDbContext db,
    IRappiDeliveryProvider rappi,
    IClock clock,
    IOrderRepository orders,
    IMapper mapper,
    IOrderNotificationService notifications,
    IDeliveryRouteWorkflowService deliveryRouteWorkflow,
    ILogger<RappiOrderProcessor> logger,
    IKitchenAutoPrintService? kitchenAutoPrint = null) : IRappiOrderProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RappiOrderProcessingResult> IngestNewOrderAsync(
        int connectionId,
        string rawOrderJson,
        CancellationToken ct)
    {
        ParsedRappiOrder parsed;
        try
        {
            parsed = ParseOrder(rawOrderJson);
        }
        catch (InvalidOperationException ex)
        {
            return new(false, Held: true, Error: ex.Message);
        }

        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .Include(x => x.ProductMappings)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == connectionId && x.Provider == "rappi", ct);
        if (connection is null)
            return new(false, Error: "La integración Rappi no existe.");

        var store = connection.Stores.FirstOrDefault(x =>
            x.RappiStoreId == parsed.StoreInternalId
            || (!string.IsNullOrWhiteSpace(x.StoreIntegrationId)
                && x.StoreIntegrationId == parsed.StoreExternalId));
        var existing = await db.ExternalDeliveryOrders
            .FirstOrDefaultAsync(x =>
                x.ConnectionId == connectionId
                && x.ExternalOrderId == parsed.OrderId, ct);
        if (existing?.InternalOrderId is int internalOrderId)
            return new(true, existing.Id, internalOrderId);

        var external = existing ?? new ExternalDeliveryOrder
        {
            ConnectionId = connectionId,
            BranchId = connection.BranchId,
            ExternalOrderId = parsed.OrderId,
            CreatedAt = clock.UtcNow
        };
        if (existing is null)
            db.ExternalDeliveryOrders.Add(external);

        external.RawPayloadJson = rawOrderJson;
        ApplyParsedOrder(external, connection, store, parsed);

        var validationErrors = Validate(connection, store, parsed);
        external.ValidationErrorsJson = validationErrors.Count == 0
            ? null
            : JsonSerializer.Serialize(validationErrors, JsonOptions);
        external.Status = validationErrors.Count == 0
            ? ExternalOrderStatus.PendingAcceptance
            : ExternalOrderStatus.BlockedMapping;
        external.LastError = validationErrors.Count == 0
            ? null
            : Limit(string.Join(" ", validationErrors), 1000);
        await db.SaveChangesAsync(ct);

        if (validationErrors.Count > 0)
            return new(false, external.Id, Held: true, Error: external.LastError);

        return await RevalidateAndAcceptAsync(external.Id, connection.TechnicalUserId, ct);
    }

    public async Task<RappiOrderProcessingResult> RevalidateAndAcceptAsync(
        int externalOrderId,
        int? actorUserId,
        CancellationToken ct)
    {
        var external = await db.ExternalDeliveryOrders
            .Include(x => x.Store)
            .Include(x => x.Connection)
                .ThenInclude(x => x.ProductMappings)
                    .ThenInclude(x => x.Product)
                        .ThenInclude(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == externalOrderId, ct);
        if (external is null)
            return new(false, Error: "La orden Rappi no existe.");
        if (external.InternalOrderId.HasValue)
            return new(true, external.Id, external.InternalOrderId);

        var parsed = ParseOrder(external.RawPayloadJson);
        ApplyParsedOrder(external, external.Connection, external.Store, parsed);
        var errors = Validate(external.Connection, external.Store, parsed);
        external.ValidationErrorsJson = errors.Count == 0
            ? null
            : JsonSerializer.Serialize(errors, JsonOptions);
        if (errors.Count > 0)
        {
            external.Status = ExternalOrderStatus.BlockedMapping;
            external.LastError = Limit(string.Join(" ", errors), 1000);
            await db.SaveChangesAsync(ct);
            return new(false, external.Id, Held: true, Error: external.LastError);
        }

        external.Status = ExternalOrderStatus.Processing;
        external.LastAttemptAt = clock.UtcNow;
        external.LastError = null;
        await db.SaveChangesAsync(ct);

        var taken = await rappi.AcceptOrderAsync(
            external.ExternalOrderId,
            external.CookingTimeMinutes,
            ct);
        if (!taken.Success)
        {
            var alreadyTaken = await IsAlreadyTakenAsync(external.ExternalOrderId, ct);
            if (!alreadyTaken)
            {
                external.Status = taken.StatusCode == 400
                    ? ExternalOrderStatus.Expired
                    : ExternalOrderStatus.SyncError;
                external.LastError = Limit(taken.Error, 1000);
                await db.SaveChangesAsync(ct);
                return new(false, external.Id, Held: taken.StatusCode == 400, Error: external.LastError);
            }
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var duplicateOrder = await db.Orders
                .FirstOrDefaultAsync(x =>
                    x.DeliveryAppConnectionId == external.ConnectionId
                    && x.ExternalOrderId == external.ExternalOrderId, ct);
            if (duplicateOrder is not null)
            {
                external.InternalOrderId = duplicateOrder.Id;
                external.Status = ExternalOrderStatus.Accepted;
                external.AcceptedAt ??= clock.UtcNow;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new(true, external.Id, duplicateOrder.Id);
            }

            var technicalUserId = external.Connection.TechnicalUserId;
            if (!technicalUserId.HasValue || !external.Connection.CustomerId.HasValue)
                throw new InvalidOperationException(
                    "Configura el cliente Rappi y el usuario técnico antes de aceptar órdenes.");

            var mappings = external.Connection.ProductMappings
                .Where(x => x.IsSelected)
                .ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
            var lines = JsonSerializer.Deserialize<List<ExternalDeliveryOrderLine>>(
                external.LinesJson,
                JsonOptions) ?? [];

            var order = new Order
            {
                BranchId = external.BranchId,
                TakenById = technicalUserId.Value,
                CustomerId = external.Connection.CustomerId,
                GuestName = Limit(external.CustomerName, 100),
                Type = OrderType.Delivery,
                Status = OrderStatus.Taken,
                Subtotal = external.TotalProducts,
                DiscountTotal = external.TotalDiscountByPartner,
                Total = external.Total,
                Notes = Limit($"Rappi #{external.ExternalOrderId}", 200),
                DeliveryAppConnectionId = external.ConnectionId,
                ExternalOrderId = external.ExternalOrderId,
                OrderSource = "rappi",
                ExternalFulfillmentProvider = "rappi",
                ExternalStoreName = external.Store?.Name ?? external.ExternalStoreId,
                ExternalCustomerPhone = Limit(external.CustomerPhone, 50),
                ExternalDeliveryAddress = Limit(external.DeliveryAddress, 600),
                ExternalTotalDiscounts = external.TotalDiscounts,
                ExternalDiscountByRappi = external.TotalDiscountByRappi,
                ExternalDiscountByPartner = external.TotalDiscountByPartner,
                ExternalCharges = external.TotalCharges,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            order.AddStatusTime(OrderStatus.Taken, clock.UtcNow);
            foreach (var line in lines)
            {
                var mapping = mappings[line.Sku];
                order.OrderDetails.Add(new OrderDetail
                {
                    ProductId = mapping.ProductId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Discount = 0,
                    Notes = Limit(line.Notes, 500)
                });
            }

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            var commission = decimal.Round(
                external.Total * external.Connection.EstimatedCommissionRate,
                2,
                MidpointRounding.AwayFromZero);
            db.AppPayments.Add(new AppPayment
            {
                OrderId = order.Id,
                AppId = external.Connection.FinancialAppId,
                Amount = external.Total,
                EstimatedCommissionRate = external.Connection.EstimatedCommissionRate,
                EstimatedCommissionAmount = commission,
                ExpectedNetAmount = external.Total - commission,
                IsSetted = false
            });
            external.InternalOrderId = order.Id;
            external.AcceptedByUserId = actorUserId ?? technicalUserId;
            external.AcceptedAt = clock.UtcNow;
            external.Status = ExternalOrderStatus.Accepted;
            external.ValidationErrorsJson = null;
            external.LastError = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var fullOrder = await orders.GetByIdWithFullDetailsAsync(order.Id, ct);
            if (fullOrder is not null)
            {
                if (kitchenAutoPrint is not null)
                    await kitchenAutoPrint.TryEnqueueAsync(
                        fullOrder,
                        KitchenAutoPrintTrigger.WhenOrderCreated,
                        ct);
                await notifications.NotifyNewOrderToKitchen(mapper.Map<OrderDto>(fullOrder));
            }
            return new(true, external.Id, order.Id);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Rappi order {ExternalOrderId} raced during local creation.", external.ExternalOrderId);
            var duplicate = await db.Orders.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.DeliveryAppConnectionId == external.ConnectionId
                    && x.ExternalOrderId == external.ExternalOrderId, ct);
            if (duplicate is not null)
                return new(true, external.Id, duplicate.Id);
            external.Status = ExternalOrderStatus.SyncError;
            external.LastError = "Rappi aceptó la orden, pero no fue posible crearla localmente. Se reintentará.";
            await db.SaveChangesAsync(ct);
            return new(false, external.Id, Error: external.LastError);
        }
        catch (InvalidOperationException ex)
        {
            external.Status = ExternalOrderStatus.SyncError;
            external.LastError = Limit(ex.Message, 1000);
            await db.SaveChangesAsync(ct);
            return new(false, external.Id, Error: external.LastError);
        }
    }

    public async Task<RappiOperationResult> RejectAsync(
        int externalOrderId,
        string reason,
        CancellationToken ct)
    {
        var external = await db.ExternalDeliveryOrders.FirstOrDefaultAsync(x => x.Id == externalOrderId, ct);
        if (external is null)
            return new(false, Error: "La orden Rappi no existe.");
        if (external.InternalOrderId.HasValue)
            return new(false, Error: "No se puede rechazar una orden que ya fue aceptada.");

        external.LastAttemptAt = clock.UtcNow;
        var result = await rappi.RejectOrderAsync(external.ExternalOrderId, reason, ct);
        external.Status = result.Success
            ? ExternalOrderStatus.Rejected
            : result.StatusCode == 400
                ? ExternalOrderStatus.Expired
                : ExternalOrderStatus.SyncError;
        external.LastError = result.Success ? null : Limit(result.Error, 1000);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task ProcessPendingWebhookEventsAsync(CancellationToken ct)
    {
        var pendingIds = await db.IntegrationWebhookEvents
            .AsNoTracking()
            .Where(x => x.Provider == "rappi" && x.Status == "received")
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .Take(50)
            .ToListAsync(ct);

        foreach (var id in pendingIds)
        {
            var claimed = await db.IntegrationWebhookEvents
                .Where(x => x.Id == id && x.Status == "received")
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, "processing")
                        .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1),
                    ct);
            if (claimed == 0)
                continue;

            var webhookEvent = await db.IntegrationWebhookEvents
                .FirstAsync(x => x.Id == id, ct);
            try
            {
                await ProcessWebhookEventAsync(webhookEvent, ct);
                webhookEvent.Status = "processed";
                webhookEvent.ProcessedAt = clock.UtcNow;
                webhookEvent.LastError = null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing Rappi webhook event {EventId}.", id);
                webhookEvent.LastError = Limit(ex.Message, 1000);
                webhookEvent.Status = webhookEvent.AttemptCount < 5 ? "received" : "failed";
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task ProcessWebhookEventAsync(IntegrationWebhookEvent webhookEvent, CancellationToken ct)
    {
        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .FirstOrDefaultAsync(x => x.Id == webhookEvent.ConnectionId, ct)
            ?? throw new InvalidOperationException("La conexión del webhook no existe.");
        connection.LastWebhookAt = clock.UtcNow;

        switch (webhookEvent.EventType.ToUpperInvariant())
        {
            case "NEW_ORDER":
                foreach (var rawOrder in ExtractOrderPayloads(webhookEvent.PayloadJson))
                    await IngestNewOrderAsync(connection.Id, rawOrder, ct);
                break;
            case "ORDER_EVENT_CANCEL":
                await ApplyCancellationAsync(connection.Id, webhookEvent.PayloadJson, ct);
                break;
            case "ORDER_OTHER_EVENT":
                await ApplyOrderEventAsync(connection.Id, webhookEvent.PayloadJson, ct);
                break;
            case "MENU_APPROVED":
            case "MENU_REJECTED":
                await ApplyMenuStatusAsync(connection.Id, webhookEvent.EventType, webhookEvent.PayloadJson, ct);
                break;
            case "STORE_CONNECTIVITY":
                ApplyConnectivity(connection, webhookEvent.PayloadJson);
                break;
        }
    }

    private async Task ApplyCancellationAsync(int connectionId, string payload, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payload);
        var orderId = FindString(document.RootElement, "order_id")
            ?? throw new InvalidOperationException("El webhook de cancelación no contiene order_id.");
        var cancellationReason = BuildCancellationReason(document.RootElement);
        var external = await db.ExternalDeliveryOrders
            .Include(x => x.InternalOrder)
            .FirstOrDefaultAsync(x =>
                x.ConnectionId == connectionId
                && x.ExternalOrderId == orderId, ct);
        if (external is null)
            return;

        if (external.InternalOrder is null)
        {
            external.Status = ExternalOrderStatus.Cancelled;
            external.LastError = null;
            return;
        }

        if (external.InternalOrder.Status == OrderStatus.Cancelled)
        {
            external.Status = ExternalOrderStatus.Cancelled;
            external.LastError = null;
            return;
        }

        var appPayments = await db.AppPayments
            .Where(x => x.OrderId == external.InternalOrder.Id)
            .ToListAsync(ct);
        if (external.InternalOrder.Status == OrderStatus.Delivered || appPayments.Any(x => x.IsSetted))
        {
            external.Status = ExternalOrderStatus.ReconciliationRequired;
            external.LastError = "Rappi canceló una orden entregada o liquidada; requiere conciliación manual.";
            return;
        }

        var previousStatus = external.InternalOrder.Status;
        var routeIdSnapshot = external.InternalOrder.DeliveryRouteId;
        var notifyKitchen = KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(
            external.InternalOrder,
            clock.UtcNow);

        foreach (var appPayment in appPayments)
        {
            if (appPayment.IsReversed)
                continue;

            appPayment.IsReversed = true;
            appPayment.ReversedAt = clock.UtcNow;
            appPayment.ReversalReason = cancellationReason;
        }
        external.InternalOrder.Status = OrderStatus.Cancelled;
        external.InternalOrder.CancelledReason = cancellationReason;
        external.InternalOrder.AddStatusTime(OrderStatus.Cancelled, clock.UtcNow);
        external.Status = ExternalOrderStatus.Cancelled;
        external.LastError = null;
        await db.SaveChangesAsync(ct);

        if (notifyKitchen)
        {
            await notifications.NotifyOrderCancelledToKitchen(
                external.InternalOrder.BranchId,
                external.InternalOrder.Id,
                cancellationReason);
        }

        await deliveryRouteWorkflow.OnOrderCancelledWhileRouteOpenAsync(
            external.InternalOrder.Id,
            ct);
        await deliveryRouteWorkflow.TryFinalizeRouteWhenAllTerminalAsync(
            external.InternalOrder.Id,
            routeIdSnapshot,
            ct);

        if (previousStatus == OrderStatus.OnTheWay)
        {
            var fullOrder = await orders.GetByIdWithFullDetailsAsync(external.InternalOrder.Id, ct);
            if (fullOrder is not null)
            {
                await notifications.NotifyOrderModifiedToDelivery(
                    mapper.Map<OrderDto>(fullOrder),
                    "status");
            }
        }
    }

    private async Task ApplyOrderEventAsync(int connectionId, string payload, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payload);
        var orderId = FindString(document.RootElement, "order_id");
        if (string.IsNullOrWhiteSpace(orderId))
            return;
        var eventName = FindEventName(document.RootElement);
        var external = await db.ExternalDeliveryOrders
            .Include(x => x.InternalOrder)
            .FirstOrDefaultAsync(x =>
                x.ConnectionId == connectionId
                && x.ExternalOrderId == orderId, ct);
        if (external?.InternalOrder is null)
            return;

        if (eventName.Equals("hand_to_domiciliary", StringComparison.OrdinalIgnoreCase)
            && external.InternalOrder.Status is not (OrderStatus.Delivered or OrderStatus.Cancelled))
        {
            external.InternalOrder.Status = OrderStatus.OnTheWay;
            external.InternalOrder.AddStatusTime(OrderStatus.OnTheWay, clock.UtcNow);
        }
        else if (eventName.Equals("close_order", StringComparison.OrdinalIgnoreCase)
                 && external.InternalOrder.Status != OrderStatus.Cancelled)
        {
            external.InternalOrder.Status = OrderStatus.Delivered;
            external.InternalOrder.AddStatusTime(OrderStatus.Delivered, clock.UtcNow);
        }
    }

    private async Task ApplyMenuStatusAsync(
        int connectionId,
        string eventType,
        string payload,
        CancellationToken ct)
    {
        var publication = await db.RappiMenuPublications
            .Where(x => x.ConnectionId == connectionId && x.Status == "submitted")
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (publication is null)
            return;
        publication.Status = eventType == "MENU_APPROVED" ? "approved" : "rejected";
        publication.CompletedAt = clock.UtcNow;
        publication.Error = eventType == "MENU_REJECTED"
            ? Limit(payload, 1000)
            : null;
        if (eventType == "MENU_APPROVED")
        {
            using var document = JsonDocument.Parse(publication.PayloadJson);
            if (document.RootElement.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                var approved = items.EnumerateArray()
                    .Select(x => new
                    {
                        Sku = GetString(x, "sku"),
                        Name = GetString(x, "name"),
                        Description = GetString(x, "description"),
                        ImageUrl = GetString(x, "imageUrl"),
                        Price = GetInt(x, "price")
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Sku) && x.Price.HasValue)
                    .ToDictionary(x => x.Sku!, StringComparer.OrdinalIgnoreCase);
                var mappings = await db.DeliveryAppProductMappings
                    .Where(x => x.ConnectionId == connectionId && x.IsSelected)
                    .ToListAsync(ct);
                foreach (var mapping in mappings)
                {
                    if (!approved.TryGetValue(mapping.Sku, out var item))
                        continue;
                    mapping.PublishedName = item.Name;
                    mapping.PublishedDescription = item.Description;
                    mapping.PublishedImageUrl = item.ImageUrl;
                    mapping.PublishedPrice = item.Price;
                    mapping.PublishedAt = clock.UtcNow;
                }
                var connection = await db.DeliveryAppConnections
                    .FirstAsync(x => x.Id == connectionId, ct);
                connection.LastMenuPublishedAt = clock.UtcNow;
                connection.LastError = null;
            }
        }
    }

    private void ApplyConnectivity(DeliveryAppConnection connection, string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var storeId = FindString(document.RootElement, "external_store_id")
            ?? FindString(document.RootElement, "store_id");
        var store = connection.Stores.FirstOrDefault(x =>
            x.StoreIntegrationId == storeId || x.RappiStoreId == storeId);
        if (store is null)
            return;
        store.ConnectivityEnabled = FindBoolean(document.RootElement, "enabled")
            ?? FindBoolean(document.RootElement, "online");
        store.LastConnectivityAt = clock.UtcNow;
        store.LastError = FindString(document.RootElement, "message");
    }

    private async Task<bool> IsAlreadyTakenAsync(string orderId, CancellationToken ct)
    {
        var events = await rappi.GetOrderEventsAsync(orderId, ct);
        if (!events.Success || events.Events is null)
            return false;
        return events.Events.Any(x =>
            x.Contains("taken_visible_order", StringComparison.OrdinalIgnoreCase)
            || x.Contains("ready_for_pick", StringComparison.OrdinalIgnoreCase)
            || x.Contains("hand_to_domiciliary", StringComparison.OrdinalIgnoreCase)
            || x.Contains("close_order", StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Validate(
        DeliveryAppConnection connection,
        DeliveryAppStore? store,
        ParsedRappiOrder order)
    {
        var errors = new List<string>();
        if (!connection.IsActive)
            errors.Add("La integración está desactivada.");
        if (store is null)
            errors.Add("La tienda recibida no está configurada.");
        if (!string.Equals(order.DeliveryMethod, "delivery", StringComparison.OrdinalIgnoreCase))
            errors.Add($"La modalidad {order.DeliveryMethod} no está soportada en este sprint.");
        if (!connection.CustomerId.HasValue)
            errors.Add("Falta seleccionar el cliente interno Rappi.");
        if (!connection.TechnicalUserId.HasValue)
            errors.Add("Falta configurar el usuario técnico Rappi.");
        if (order.Lines.Count == 0)
            errors.Add("La orden no contiene productos.");
        if (!order.HasRequiredTotals
            || order.Total <= 0
            || order.TotalProducts < 0
            || order.TotalDiscounts < 0
            || order.TotalDiscountByPartner < 0
            || order.TotalCharges < 0)
            errors.Add("Los totales de la orden son inválidos.");
        if (order.Lines.All(x => x.Subitems is not { Count: > 0 })
            && order.Lines.Sum(x => (long)x.UnitPrice * x.Quantity) != order.TotalProducts)
            errors.Add("El total de productos no coincide con las líneas recibidas.");

        var mappings = connection.ProductMappings
            .Where(x => x.IsSelected)
            .ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
        foreach (var line in order.Lines)
        {
            if (!string.Equals(line.ItemType, "PRODUCT", StringComparison.OrdinalIgnoreCase))
                errors.Add($"{line.Name}: tipo de ítem no soportado.");
            if (line.Quantity <= 0 || line.UnitPrice < 0)
                errors.Add($"{line.Name}: cantidad o precio inválido.");
            if (line.Subitems is { Count: > 0 })
                errors.Add($"{line.Name}: contiene modificadores no soportados.");
            if (string.IsNullOrWhiteSpace(line.Sku) || !mappings.TryGetValue(line.Sku, out var mapping))
            {
                errors.Add($"{line.Name}: SKU sin asociación.");
                continue;
            }
            if (!mapping.PublishedPrice.HasValue)
                errors.Add($"{line.Name}: no pertenece al último menú publicado.");
            else if (mapping.PublishedPrice.Value != line.UnitPrice)
                errors.Add($"{line.Name}: precio distinto al último menú publicado.");
            if (!IsProductAvailable(mapping.Product))
                errors.Add($"{line.Name}: producto inactivo o agotado.");
        }
        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsProductAvailable(Product product) =>
        product.Active
        && (product.Category.Name.Contains("arroz", StringComparison.OrdinalIgnoreCase)
            || !product.Stock.HasValue
            || product.Stock.Value > 0);

    private void ApplyParsedOrder(
        ExternalDeliveryOrder external,
        DeliveryAppConnection connection,
        DeliveryAppStore? store,
        ParsedRappiOrder parsed)
    {
        external.StoreId = store?.Id;
        external.ExternalStoreId = parsed.StoreInternalId ?? parsed.StoreExternalId ?? string.Empty;
        external.CustomerName = Limit(parsed.CustomerName, 200);
        external.CustomerPhone = Limit(parsed.CustomerPhone, 50);
        external.DeliveryAddress = Limit(parsed.DeliveryAddress, 600);
        external.DeliveryMethod = Limit(parsed.DeliveryMethod, 40);
        external.PaymentMethod = Limit(parsed.PaymentMethod, 60);
        external.Total = parsed.Total;
        external.TotalProducts = parsed.TotalProducts;
        external.TotalDiscounts = parsed.TotalDiscounts;
        external.TotalDiscountByPartner = parsed.TotalDiscountByPartner;
        external.TotalDiscountByRappi = parsed.Discounts.Sum(x => x.AmountByRappi);
        external.TotalCharges = parsed.TotalCharges;
        external.CookingTimeMinutes = parsed.CookingTimeMinutes > 0
            ? Math.Clamp(parsed.CookingTimeMinutes, 5, 180)
            : connection.DefaultCookingTimeMinutes;
        external.LinesJson = JsonSerializer.Serialize(parsed.Lines, JsonOptions);
        external.DiscountsJson = JsonSerializer.Serialize(parsed.Discounts, JsonOptions);
        external.UpdatedAt = clock.UtcNow;
    }

    private static ParsedRappiOrder ParseOrder(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            root = root.EnumerateArray().FirstOrDefault();
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("El payload de la orden Rappi es inválido.");

        var detail = root.TryGetProperty("order_detail", out var orderDetail)
            ? orderDetail
            : root;
        var orderId = GetString(detail, "order_id")
            ?? GetString(root, "order_id")
            ?? throw new InvalidOperationException("El payload Rappi no contiene order_id.");
        var customer = root.TryGetProperty("customer", out var customerNode)
            ? customerNode
            : detail.TryGetProperty("customer", out customerNode)
                ? customerNode
                : default;
        var store = root.TryGetProperty("store", out var storeNode)
            ? storeNode
            : detail.TryGetProperty("store", out storeNode)
                ? storeNode
                : default;
        var delivery = detail.TryGetProperty("delivery_information", out var deliveryNode)
            ? deliveryNode
            : default;
        var totals = detail.TryGetProperty("totals", out var totalsNode)
            ? totalsNode
            : default;
        var discounts = ParseDiscounts(detail);
        var totalCharges = totals.ValueKind == JsonValueKind.Object
            && totals.TryGetProperty("charges", out var charges)
            ? SumNumericProperties(charges)
            : 0;

        return new ParsedRappiOrder(
            orderId,
            GetString(store, "internal_id"),
            GetString(store, "external_id"),
            $"{GetString(customer, "first_name")} {GetString(customer, "last_name")}".Trim(),
            GetString(customer, "phone_number"),
            FindString(delivery, "complete_address")
                ?? FindString(delivery, "address"),
            GetString(detail, "delivery_method") ?? string.Empty,
            GetString(detail, "payment_method") ?? "unknown",
            GetInt(totals, "total_order") ?? 0,
            GetInt(totals, "total_products") ?? 0,
            GetInt(totals, "total_discounts") ?? 0,
            GetInt(totals, "total_discount_by_partner") ?? discounts.Sum(x => x.AmountByPartner),
            totalCharges,
            GetInt(detail, "cooking_time")
                ?? GetInt(detail, "coooking_time")
                ?? 0,
            ParseLines(detail),
            discounts,
            HasInt(totals, "total_order")
            && HasInt(totals, "total_products")
            && HasInt(totals, "total_discounts")
            && HasInt(totals, "total_discount_by_partner"));
    }

    private static List<ExternalDeliveryOrderLine> ParseLines(JsonElement detail)
    {
        if (!detail.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];
        return items.EnumerateArray().Select(ParseLine).ToList();
    }

    private static ExternalDeliveryOrderLine ParseLine(JsonElement item)
    {
        var subitems = item.TryGetProperty("subitems", out var children)
            && children.ValueKind == JsonValueKind.Array
            ? children.EnumerateArray().Select(ParseLine).ToList()
            : [];
        return new(
            GetString(item, "id") ?? GetString(item, "product_id") ?? string.Empty,
            GetString(item, "sku") ?? string.Empty,
            GetString(item, "name") ?? "Producto Rappi",
            GetString(item, "type") ?? "PRODUCT",
            GetInt(item, "quantity") ?? 0,
            GetInt(item, "price") ?? GetInt(item, "unit_price_with_discount") ?? 0,
            GetString(item, "comments"),
            subitems);
    }

    private static List<ExternalDeliveryDiscount> ParseDiscounts(JsonElement detail)
    {
        if (!detail.TryGetProperty("discounts", out var discounts)
            || discounts.ValueKind != JsonValueKind.Array)
            return [];
        return discounts.EnumerateArray().Select(x => new ExternalDeliveryDiscount(
            GetString(x, "title"),
            GetString(x, "description"),
            GetString(x, "type"),
            GetString(x, "sku"),
            GetInt(x, "value") ?? 0,
            GetInt(x, "amount_by_rappi") ?? 0,
            GetInt(x, "amount_by_partner") ?? 0)).ToList();
    }

    private static IReadOnlyList<string> ExtractOrderPayloads(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray().Select(x => x.GetRawText()).ToList();
        foreach (var propertyName in new[] { "orders", "data" })
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(propertyName, out var array)
                && array.ValueKind == JsonValueKind.Array)
                return array.EnumerateArray().Select(x => x.GetRawText()).ToList();
        }
        return [root.GetRawText()];
    }

    private static string FindEventName(JsonElement root)
    {
        foreach (var name in new[] { "order_event", "event_type", "event", "name" })
        {
            var value = FindString(root, name);
            if (!string.IsNullOrWhiteSpace(value)
                && !value.Equals("ORDER_OTHER_EVENT", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("ORDER_EVENT_CANCEL", StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return string.Empty;
    }

    private static string BuildCancellationReason(JsonElement root)
    {
        var eventName = FindEventName(root);
        var detail = FindString(root, "cancel_reason")
            ?? FindString(root, "reason")
            ?? FindString(root, "description");

        var reason = string.IsNullOrWhiteSpace(eventName)
            ? "Cancelado desde Rappi"
            : $"Cancelado desde Rappi ({eventName.Trim()})";

        if (!string.IsNullOrWhiteSpace(detail)
            && !detail.Equals(eventName, StringComparison.OrdinalIgnoreCase))
        {
            reason += $": {detail.Trim()}";
        }

        return Limit(reason, 200);
    }

    private static int SumNumericProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return 0;
        long total = 0;
        foreach (var property in element.EnumerateObject())
            if (TryGetInt(property.Value, out var value))
                total += value;
        if (total is > int.MaxValue or < int.MinValue)
            throw new InvalidOperationException("Los cargos de la orden Rappi exceden el rango permitido.");
        return (int)total;
    }

    private static string? FindString(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty(name, out var value))
                return value.ToString();
            foreach (var property in root.EnumerateObject())
            {
                var found = FindString(property.Value, name);
                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = FindString(item, name);
                if (!string.IsNullOrWhiteSpace(found))
                    return found;
            }
        }
        return null;
    }

    private static bool? FindBoolean(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
            ? value.ToString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && TryGetInt(value, out var parsed)
            ? parsed
            : null;

    private static bool HasInt(JsonElement element, string name) =>
        GetInt(element, name).HasValue;

    private static bool TryGetInt(JsonElement value, out int parsed)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out parsed))
                return true;
            if (value.TryGetDecimal(out var number)
                && decimal.Truncate(number) == number
                && number is >= int.MinValue and <= int.MaxValue)
            {
                parsed = decimal.ToInt32(number);
                return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.String
                 && decimal.TryParse(
                     value.GetString(),
                     NumberStyles.Number,
                     CultureInfo.InvariantCulture,
                     out var number)
                 && decimal.Truncate(number) == number
                 && number is >= int.MinValue and <= int.MaxValue)
        {
            parsed = decimal.ToInt32(number);
            return true;
        }

        parsed = 0;
        return false;
    }

    private static string Limit(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Length <= maxLength
                ? value.Trim()
                : value.Trim()[..maxLength];

    private record ParsedRappiOrder(
        string OrderId,
        string? StoreInternalId,
        string? StoreExternalId,
        string CustomerName,
        string? CustomerPhone,
        string? DeliveryAddress,
        string DeliveryMethod,
        string PaymentMethod,
        int Total,
        int TotalProducts,
        int TotalDiscounts,
        int TotalDiscountByPartner,
        int TotalCharges,
        int CookingTimeMinutes,
        IReadOnlyList<ExternalDeliveryOrderLine> Lines,
        IReadOnlyList<ExternalDeliveryDiscount> Discounts,
        bool HasRequiredTotals);
}
