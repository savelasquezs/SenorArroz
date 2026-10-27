using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Helpers;

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
    IPrintQueueService? printQueue = null,
    IInventoryService? inventory = null,
    IServiceScopeFactory? serviceScopeFactory = null,
    IOptions<RappiOptions>? rappiOptions = null) : IRappiOrderProcessor
{
    private readonly string? configuredEnvironment = rappiOptions?.Value.Environment;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int WebhookMaxAttempts = 5;
    // Not accepted by the public webhook allowlist; only the core cancellation
    // transaction can create this trusted second phase in the existing inbox.
    private const string CancellationEffectsEventType = "LOCAL_RAPPI_CANCEL_EFFECTS";
    // Older workers claim every `received` row and do not understand internal phases.
    private const string CancellationEffectsPending = "cancel_effects_pending";
    private const string CancellationEffectsLeased = "cancel_effects_leased";
    private const string AcceptanceEffectsEventType = "LOCAL_RAPPI_ACCEPT_EFFECTS";
    private const string AcceptanceEffectsPending = "accept_effects_pending";
    private const string AcceptanceEffectsLeased = "accept_effects_leased";
    private const string AwaitingOrderStatus = "awaiting_order";
    // A distinct status makes rollout safe: legacy workers never stamped a lease on
    // `processing`, so even a very old UpdatedAt cannot prove that claim is abandoned.
    private const string LeasedWebhookStatus = "processing_leased";
    // A claim must outlive normal provider retries, but must not survive a crashed worker forever.
    private static readonly TimeSpan WebhookProcessingLease = TimeSpan.FromMinutes(15);

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
            .FirstOrDefaultAsync(x => x.Id == connectionId && x.Provider == "rappi"
                && (configuredEnvironment == null || x.Environment == configuredEnvironment), ct);
        if (connection is null)
            return new(false, Error: "La integración Rappi no existe.");

        var store = connection.Stores.FirstOrDefault(x =>
            x.RappiStoreId == parsed.StoreInternalId
            || (!string.IsNullOrWhiteSpace(x.StoreIntegrationId)
                && x.StoreIntegrationId == parsed.StoreExternalId));
        int externalOrderId;
        await using (var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            await LockExternalOrderAsync(connection.TenantId, connectionId, parsed.OrderId, ct);
            var existing = await db.ExternalDeliveryOrders
                .FirstOrDefaultAsync(x =>
                    x.ConnectionId == connectionId
                    && x.ExternalOrderId == parsed.OrderId, ct);
            if (existing is not null) await ReloadTrackedAsync(existing, ct);
            if (existing?.InternalOrderId is int internalOrderId)
                return new(true, existing.Id, internalOrderId);
            if (TerminalOrderResult(existing) is { } terminalResult)
                return terminalResult;

            var external = existing ?? new ExternalDeliveryOrder
            {
                ConnectionId = connectionId,
                BranchId = connection.BranchId,
                ExternalOrderId = parsed.OrderId,
                CreatedAt = clock.UtcNow
            };
            if (existing is null)
                db.ExternalDeliveryOrders.Add(external);

            // Legacy SyncError rows may predate the durable attempt marker.
            if (existing?.Status == ExternalOrderStatus.SyncError && !external.LastAttemptAt.HasValue)
                external.LastAttemptAt = clock.UtcNow;
            external.RawPayloadJson = rawOrderJson;
            ApplyParsedOrder(external, connection, store, parsed);

            var validationErrors = Validate(connection, store, parsed);
            await AppendInventoryValidationErrorsAsync(validationErrors, connection, parsed, ct);
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
            if (transaction is not null) await transaction.CommitAsync(ct);

            if (validationErrors.Count > 0)
                return new(false, external.Id, Held: true, Error: external.LastError);

            externalOrderId = external.Id;
        }
        return await RevalidateAndAcceptAsync(externalOrderId, connection.TechnicalUserId, ct);
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
            .FirstOrDefaultAsync(x => x.Id == externalOrderId
                && (configuredEnvironment == null || x.Connection.Environment == configuredEnvironment), ct);
        if (external is null)
            return new(false, Error: "La orden Rappi no existe.");
        try
        {
            await using var serialization = await PostgreSqlSessionLock.AcquireAsync(db.Database,
                1380012112, $"{external.TenantId}:{external.ConnectionId}:{external.ExternalOrderId}", ct);
            var alreadyTaken = false;
            await using (var checkpoint = db.Database.IsRelational() && db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(ct) : null)
            {
                await LockExternalOrderAsync(external.TenantId, external.ConnectionId, external.ExternalOrderId, ct);
                await ReloadTrackedAsync(external, ct);
                if (external.InternalOrderId.HasValue)
                    return new(true, external.Id, external.InternalOrderId);
                if (TerminalOrderResult(external) is { } terminalResult)
                    return terminalResult;
                // Preserve the retry guard even if local validation moves a legacy
                // SyncError without a timestamp into BlockedMapping.
                if (external.Status == ExternalOrderStatus.SyncError && !external.LastAttemptAt.HasValue)
                    external.LastAttemptAt = clock.UtcNow;

                var parsed = ParseOrder(external.RawPayloadJson);
                ApplyParsedOrder(external, external.Connection, external.Store, parsed);
                var errors = Validate(external.Connection, external.Store, parsed);
                await AppendInventoryValidationErrorsAsync(errors, external.Connection, parsed, ct);
                external.ValidationErrorsJson = errors.Count == 0
                    ? null
                    : JsonSerializer.Serialize(errors, JsonOptions);
                if (errors.Count > 0)
                {
                    external.Status = ExternalOrderStatus.BlockedMapping;
                    external.LastError = Limit(string.Join(" ", errors), 1000);
                    await db.SaveChangesAsync(ct);
                    if (checkpoint is not null) await checkpoint.CommitAsync(ct);
                    return new(false, external.Id, Held: true, Error: external.LastError);
                }

                // LastAttemptAt survives duplicate NEW_ORDER ingestion and validation
                // changes. Every retry first checks history before reserving stock or
                // repeating the remote PUT, not only after another failed acceptance.
                if (external.LastAttemptAt.HasValue || external.Status == ExternalOrderStatus.SyncError)
                {
                    var evidence = await GetAcceptanceEvidenceAsync(external.ExternalOrderId, ct);
                    alreadyTaken = evidence.Accepted;
                    if (!alreadyTaken && !evidence.MayAttempt)
                    {
                        external.Status = ExternalOrderStatus.SyncError;
                        external.LastAttemptAt = clock.UtcNow;
                        external.LastError = evidence.Error;
                        await db.SaveChangesAsync(ct);
                        if (checkpoint is not null) await checkpoint.CommitAsync(ct);
                        return new(false, external.Id, Held: true, Error: external.LastError);
                    }
                }

                external.Status = ExternalOrderStatus.Processing;
                external.LastAttemptAt = clock.UtcNow;
                external.LastError = null;
                await db.SaveChangesAsync(ct);
                if (checkpoint is not null) await checkpoint.CommitAsync(ct);
            }

            await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(ct) : null;
            await LockExternalOrderAsync(external.TenantId, external.ConnectionId, external.ExternalOrderId, ct);
            await ReloadTrackedAsync(external, ct);
            if (external.InternalOrderId.HasValue)
                return new(true, external.Id, external.InternalOrderId);
            if (TerminalOrderResult(external) is { } completed)
                return completed;

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
                if (transaction is not null) await transaction.CommitAsync(ct);
                return new(true, external.Id, duplicateOrder.Id);
            }

            var technicalUserId = external.Connection.TechnicalUserId;
            if (!technicalUserId.HasValue || !external.Connection.CustomerId.HasValue)
                throw new InvalidOperationException(
                    "Configura el cliente Rappi y el usuario técnico antes de aceptar órdenes.");
            var tenantId = await db.Branches.AsNoTracking()
                .Where(x => x.Id == external.BranchId)
                .Select(x => x.TenantId)
                .SingleAsync(ct);

            var mappings = external.Connection.ProductMappings
                .Where(x => x.IsSelected)
                .ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
            var lines = JsonSerializer.Deserialize<List<ExternalDeliveryOrderLine>>(
                external.LinesJson,
                JsonOptions) ?? [];

            var order = new Order
            {
                TenantId = tenantId,
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
            if (inventory is not null)
                await inventory.SnapshotAndReserveAsync(order, false, $"order:{order.Id}:rappi-taken", ct);
            if (!alreadyTaken)
            {
                var taken = await rappi.AcceptOrderAsync(
                    external.ExternalOrderId,
                    external.CookingTimeMinutes,
                    ct);
                if (!taken.Success)
                {
                    var evidence = await GetAcceptanceEvidenceAsync(external.ExternalOrderId, ct);
                    if (!evidence.Accepted)
                    {
                        if (transaction is not null) await transaction.RollbackAsync(ct);
                        // 400 means invalid state transition, not proof of expiry.
                        return await RecordAcceptanceFailureAsync(externalOrderId,
                            ExternalOrderStatus.SyncError, evidence.Error, true, ct);
                    }
                }
            }
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
            var acceptanceEffects = new IntegrationWebhookEvent
            {
                TenantId = external.TenantId,
                ConnectionId = external.ConnectionId,
                Provider = "rappi",
                EventKey = $"{AcceptanceEffectsEventType}:{external.ConnectionId}:{external.ExternalOrderId}",
                EventType = AcceptanceEffectsEventType,
                PayloadJson = JsonSerializer.Serialize(new { orderId = order.Id }, JsonOptions),
                PayloadHash = string.Empty,
                Status = AcceptanceEffectsLeased,
                AttemptCount = 1,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.IntegrationWebhookEvents.Add(acceptanceEffects);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            if (serialization is not null) await serialization.DisposeAsync();
            try
            {
                await ApplyAcceptanceEffectsAsync(acceptanceEffects, ct);
                await CompleteAcceptanceEffectsAsync(acceptanceEffects, null, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Rappi acceptance effects remain pending for order {OrderId}.", order.Id);
                await CompleteAcceptanceEffectsAsync(acceptanceEffects,
                    "El pedido fue aceptado; los avisos o la impresión pendientes se reintentarán.", ct);
            }
            return new(true, external.Id, order.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // The provider can throw after its last transport retry, including
            // OAuth or the history read. The transaction has disposed/rolled back;
            // preserve an ordinary failure marker, never swallow host shutdown.
            return await RecordAcceptanceFailureAsync(externalOrderId, ExternalOrderStatus.SyncError,
                "La comunicación con Rappi quedó incierta; se consultará el historial antes de reintentar aceptación.", true, ct);
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
            return await RecordAcceptanceFailureAsync(externalOrderId, ExternalOrderStatus.SyncError,
                "No fue posible crear y reservar la orden local antes de aceptarla en Rappi. Se reintentará.", false, ct);
        }
        catch (SenorArroz.Domain.Exceptions.BusinessException ex)
        {
            return await RecordAcceptanceFailureAsync(externalOrderId, ExternalOrderStatus.BlockedMapping,
                ex.Message, true, ct);
        }
        catch (InvalidOperationException ex)
        {
            return await RecordAcceptanceFailureAsync(externalOrderId, ExternalOrderStatus.SyncError,
                ex.Message, false, ct);
        }
    }

    private async Task<RappiOrderProcessingResult> RecordAcceptanceFailureAsync(
        int externalOrderId, ExternalOrderStatus status, string? error, bool held, CancellationToken ct)
    {
        ClearTracking();
        var failed = await db.ExternalDeliveryOrders.SingleAsync(x => x.Id == externalOrderId, ct);
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockExternalOrderAsync(failed.TenantId, failed.ConnectionId, failed.ExternalOrderId, ct);
        await ReloadTrackedAsync(failed, ct);
        if (failed.InternalOrderId.HasValue) return new(true, failed.Id, failed.InternalOrderId);
        if (TerminalOrderResult(failed) is { } terminal) return terminal;
        var needsRetryGuard = status == ExternalOrderStatus.SyncError || failed.Status == ExternalOrderStatus.SyncError;
        failed.Status = status;
        if (needsRetryGuard)
            failed.LastAttemptAt = clock.UtcNow;
        failed.LastError = Limit(error, 1000);
        if (status == ExternalOrderStatus.BlockedMapping)
            failed.ValidationErrorsJson = JsonSerializer.Serialize(new[] { error }, JsonOptions);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return new(false, failed.Id, Held: held, Error: failed.LastError);
    }

    private async Task ApplyAcceptanceEffectsAsync(IntegrationWebhookEvent webhookEvent, CancellationToken ct)
    {
        using var payload = JsonDocument.Parse(webhookEvent.PayloadJson);
        var orderId = payload.RootElement.GetProperty("orderId").GetInt32();
        var external = await db.ExternalDeliveryOrders.AsNoTracking().FirstOrDefaultAsync(x =>
            x.ConnectionId == webhookEvent.ConnectionId && x.TenantId == webhookEvent.TenantId
            && x.InternalOrderId == orderId, ct)
            ?? throw new InvalidOperationException("El pedido de los avisos Rappi no coincide con su integración.");
        var fullOrder = await orders.GetByIdWithFullDetailsAsync(orderId, ct)
            ?? throw new InvalidOperationException("No se encontró el pedido para los avisos Rappi.");
        if (fullOrder.TenantId != external.TenantId || fullOrder.BranchId != external.BranchId)
            throw new InvalidOperationException("El pedido de los avisos Rappi pertenece a otra sucursal.");
        if (fullOrder.Status is OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.OnTheWay)
            return;
        await notifications.NotifyNewOrderToKitchen(mapper.Map<OrderDto>(fullOrder));
        await TryEnqueueKitchenPrintWhenOrderCreatedAsync(fullOrder, ct);
    }

    private async Task CompleteAcceptanceEffectsAsync(
        IntegrationWebhookEvent webhookEvent, string? error, CancellationToken ct)
    {
        var status = error is null ? "processed" : AcceptanceEffectsPending;
        DateTime? processedAt = error is null ? clock.UtcNow : null;
        if (db.Database.IsRelational())
        {
            await OwnedWebhookClaim(db.IntegrationWebhookEvents, webhookEvent.Id, webhookEvent.AttemptCount)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, status)
                    .SetProperty(x => x.LastError, error).SetProperty(x => x.ProcessedAt, processedAt)
                    .SetProperty(x => x.UpdatedAt, clock.UtcNow), ct);
        }
        else
        {
            webhookEvent.Status = status;
            webhookEvent.LastError = error;
            webhookEvent.ProcessedAt = processedAt;
            webhookEvent.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task TryEnqueueKitchenPrintWhenOrderCreatedAsync(Order order, CancellationToken ct)
    {
        if (printQueue is null)
            return;

        var shouldPrint = await db.BranchPrintSettings.AsNoTracking().AnyAsync(
            s => s.BranchId == order.BranchId
                && s.EnableKitchenJobs
                && s.KitchenAutoPrintTrigger == BranchPrintSettings.KitchenAutoPrintWhenOrderCreated,
            ct);
        if (!shouldPrint)
            return;

        try
        {
            await printQueue.EnqueueAsync(order.BranchId, PrintJobKind.Kitchen, [order.Id], ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "No se encoló comanda de cocina al aceptar pedido Rappi {OrderId} (sucursal {BranchId}).",
                order.Id,
                order.BranchId);
        }
    }

    public async Task<RappiOperationResult> RejectAsync(
        int externalOrderId,
        string reason,
        CancellationToken ct)
    {
        var external = await db.ExternalDeliveryOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == externalOrderId, ct);
        if (external is null)
            return new(false, Error: "La orden Rappi no existe.");
        if (external.InternalOrderId.HasValue)
            return new(false, Error: "No se puede rechazar una orden que ya fue aceptada.");
        if (TerminalOrderResult(external) is { } terminal)
            return new(false, Error: terminal.Error);

        var result = await rappi.RejectOrderAsync(external.ExternalOrderId, reason, ct);
        // The remote request stays outside the lock. Its delayed outcome must not
        // overwrite a cancellation/acceptance committed while it was in flight.
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockExternalOrderAsync(external.TenantId, external.ConnectionId, external.ExternalOrderId, ct);
        external = await db.ExternalDeliveryOrders.SingleAsync(x => x.Id == externalOrderId, ct);
        await ReloadTrackedAsync(external, ct);
        if (external.InternalOrderId.HasValue)
            return new(false, Error: "La orden fue aceptada mientras se procesaba el rechazo.");
        if (TerminalOrderResult(external) is { } completed)
            return new(false, Error: completed.Error);
        external.LastAttemptAt = clock.UtcNow;
        external.Status = result.Success
            ? ExternalOrderStatus.Rejected
            : result.StatusCode == 400
                ? ExternalOrderStatus.Expired
                : ExternalOrderStatus.SyncError;
        external.LastError = result.Success ? null : Limit(result.Error, 1000);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return result;
    }

    public async Task ProcessPendingWebhookEventsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var events = db.IntegrationWebhookEvents.Where(x =>
        configuredEnvironment == null || db.DeliveryAppConnections.Any(c =>
            c.Id == x.ConnectionId && c.Environment == configuredEnvironment));
        await ExhaustedWebhookEvents(events, now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "failed")
                .SetProperty(x => x.LastError,
                    "Se agotaron los intentos del webhook Rappi; requiere revisión y reintento manual.")
                .SetProperty(x => x.UpdatedAt, now), ct);
        await UncertainWebhookEvents(events, now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "failed")
                .SetProperty(x => x.LastError,
                    "Webhook interrumpido con resultado incierto; revisar menú o conectividad antes de reintentar.")
                .SetProperty(x => x.UpdatedAt, now), ct);

        var pending = await ClaimableWebhookEvents(events, now)
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.AttemptCount })
            .Take(50)
            .ToListAsync(ct);

        foreach (var candidate in pending)
        {
            var id = candidate.Id;
            var attemptCount = candidate.AttemptCount + 1;
            var claimedAt = clock.UtcNow;
            // Recheck the lease in the UPDATE: another instance may have claimed this row
            // since the candidate list was read. Tenant filters remain on both queries.
            var claimed = await ClaimableWebhookEvents(events, claimedAt)
                .Where(x => x.Id == id && x.AttemptCount == candidate.AttemptCount)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, x => x.EventType == CancellationEffectsEventType
                            ? CancellationEffectsLeased : x.EventType == AcceptanceEffectsEventType
                                ? AcceptanceEffectsLeased : LeasedWebhookStatus)
                        .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                        .SetProperty(x => x.UpdatedAt, claimedAt),
                    ct);
            if (claimed == 0)
                continue;

            var webhookEvent = await OwnedWebhookClaim(db.IntegrationWebhookEvents, id, attemptCount)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct);
            if (webhookEvent is null)
                continue;
            string status;
            string? error;
            DateTime? processedAt;
            using var processingTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var heartbeatStop = new CancellationTokenSource();
            processingTimeout.CancelAfter(TimeSpan.FromMinutes(5));
            // Production DI always supplies the scope factory. Direct unit-test constructors
            // may omit it, but do not exercise long-running work or lease renewal.
            var heartbeat = serviceScopeFactory is null
                ? Task.CompletedTask
                : KeepWebhookLeaseAliveAsync(id, attemptCount, webhookEvent.TenantId,
                    processingTimeout, heartbeatStop.Token);
            if (serviceScopeFactory is null)
                logger.LogWarning("Rappi webhook {EventId} has no scope factory; lease heartbeat is unavailable.", id);
            try
            {
                // Normal work is bounded well below the recovery lease. A long batch may
                // safely replay its already committed/idempotent items on the next attempt.
                var applied = await ProcessWebhookEventAsync(webhookEvent, processingTimeout.Token);
                status = applied ? "processed" : AwaitingOrderStatus;
                processedAt = applied ? clock.UtcNow : null;
                error = applied ? null : "Esperando la recepción y aceptación del pedido Rappi para aplicar su estado.";
                // Keep pending business changes (notably menu/connectivity) atomic with
                // completion. Individual order handlers finish their own transactions first.
                await using var transaction = await db.Database.BeginTransactionAsync(processingTimeout.Token);
                await db.SaveChangesAsync(processingTimeout.Token);
                var completed = await OwnedWebhookClaim(db.IntegrationWebhookEvents, id, attemptCount)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, status)
                        .SetProperty(x => x.ProcessedAt, processedAt)
                        .SetProperty(x => x.LastError, error)
                        .SetProperty(x => x.UpdatedAt, clock.UtcNow), processingTimeout.Token);
                if (completed == 1)
                    await transaction.CommitAsync(processingTimeout.Token);
                else
                {
                    await transaction.RollbackAsync(ct);
                    ClearTracking();
                }
                continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Leave the claim durable. A future worker can recover it after its lease.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing Rappi webhook event {EventId}.", id);
                // Do not accidentally persist partially mutated business entities while
                // recording a retry. Already committed operations retain their idempotency.
                ClearTracking();
                error = Limit(ex.Message, 1000);
                status = attemptCount < WebhookMaxAttempts ? "received" : "failed";
                processedAt = null;
            }
            finally
            {
                // Do not link this CTS to timeout/shutdown: a callback which ignores its
                // cancellation token is still alive and must retain its lease until it exits.
                heartbeatStop.Cancel();
                await heartbeat;
            }

            var completedAt = clock.UtcNow;
            // AttemptCount is the fencing token: a late completion cannot overwrite a
            // newer claim after lease recovery. The event is deliberately not tracked.
            await OwnedWebhookClaim(db.IntegrationWebhookEvents, id, attemptCount)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, x => status == "received" && x.EventType == CancellationEffectsEventType
                        ? CancellationEffectsPending : status == "received" && x.EventType == AcceptanceEffectsEventType
                            ? AcceptanceEffectsPending : status)
                    .SetProperty(x => x.ProcessedAt, processedAt)
                    .SetProperty(x => x.LastError, error)
                    .SetProperty(x => x.UpdatedAt, completedAt), ct);
        }
    }

    private async Task KeepWebhookLeaseAliveAsync(
        int eventId,
        int attemptCount,
        int tenantId,
        CancellationTokenSource processing,
        CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    if (await RenewWebhookLeaseAsync(eventId, attemptCount, tenantId, stop))
                        continue;
                    processing.Cancel();
                    logger.LogWarning("Rappi webhook {EventId} lost claim {AttemptCount}.", eventId, attemptCount);
                    return;
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Ask cooperative work to stop; while it remains alive keep trying to
                    // renew. A prolonged DB partition still relies on fenced/idempotent writes.
                    processing.Cancel();
                    logger.LogError(ex, "Could not renew Rappi webhook {EventId} claim.", eventId);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> RenewWebhookLeaseAsync(
        int eventId, int attemptCount, int tenantId, CancellationToken ct)
    {
        if (serviceScopeFactory is null)
            throw new InvalidOperationException("Rappi lease renewal requires an independent dependency scope.");
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITenantExecutionContext>();
        using var tenantScope = execution.BeginTenantScope(tenantId);
        var scopedDb = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var renewedAt = clock.UtcNow;
        return await OwnedWebhookClaim(scopedDb.IntegrationWebhookEvents, eventId, attemptCount)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt, renewedAt), ct) == 1;
    }

    private static IQueryable<IntegrationWebhookEvent> ClaimableWebhookEvents(
        IQueryable<IntegrationWebhookEvent> events,
        DateTime now)
    {
        var expiredBefore = now - WebhookProcessingLease;
        return events.Where(x => x.Provider == "rappi"
            && ((x.Status == "manual_retry_received"
                    && (x.EventType == "ORDER_EVENT_CANCEL" || x.EventType == "ORDER_OTHER_EVENT"
                        || x.EventType == CancellationEffectsEventType || x.EventType == AcceptanceEffectsEventType))
                || (x.Status == AwaitingOrderStatus && x.EventType == "ORDER_OTHER_EVENT"
                    && x.UpdatedAt <= now.AddSeconds(-30) && x.CreatedAt > expiredBefore)
                || x.AttemptCount < WebhookMaxAttempts
            && ((x.Status == "received" && x.EventType != CancellationEffectsEventType
                    && x.EventType != AcceptanceEffectsEventType)
                || (x.Status == LeasedWebhookStatus && x.UpdatedAt <= expiredBefore
                    && (x.EventType == "NEW_ORDER" || x.EventType == "ORDER_EVENT_CANCEL"
                        || x.EventType == "ORDER_OTHER_EVENT"))
                || (x.EventType == CancellationEffectsEventType
                    && (x.Status == CancellationEffectsPending
                        || x.Status == CancellationEffectsLeased && x.UpdatedAt <= expiredBefore))
                || (x.EventType == AcceptanceEffectsEventType
                    && (x.Status == AcceptanceEffectsPending
                        || x.Status == AcceptanceEffectsLeased && x.UpdatedAt <= expiredBefore)))));
    }

    private static IQueryable<IntegrationWebhookEvent> UncertainWebhookEvents(
        IQueryable<IntegrationWebhookEvent> events,
        DateTime now)
    {
        var expiredBefore = now - WebhookProcessingLease;
        return events.Where(x => x.Provider == "rappi" && x.Status == LeasedWebhookStatus
            && x.UpdatedAt <= expiredBefore
            && x.EventType != "NEW_ORDER" && x.EventType != "ORDER_EVENT_CANCEL"
            && x.EventType != "ORDER_OTHER_EVENT");
    }

    private static IQueryable<IntegrationWebhookEvent> ExhaustedWebhookEvents(
        IQueryable<IntegrationWebhookEvent> events,
        DateTime now)
    {
        var expiredBefore = now - WebhookProcessingLease;
        return events.Where(x => x.Provider == "rappi"
            && ((x.Status == AwaitingOrderStatus && x.CreatedAt <= expiredBefore)
                || x.AttemptCount >= WebhookMaxAttempts
            && (x.Status == "received"
                || (x.Status == LeasedWebhookStatus && x.UpdatedAt <= expiredBefore)
                || x.EventType == CancellationEffectsEventType
                    && (x.Status == CancellationEffectsPending
                        || x.Status == CancellationEffectsLeased && x.UpdatedAt <= expiredBefore)
                || x.EventType == AcceptanceEffectsEventType
                    && (x.Status == AcceptanceEffectsPending
                        || x.Status == AcceptanceEffectsLeased && x.UpdatedAt <= expiredBefore))));
    }

    private static IQueryable<IntegrationWebhookEvent> OwnedWebhookClaim(
        IQueryable<IntegrationWebhookEvent> events,
        int id,
        int attemptCount) =>
        events.Where(x => x.Id == id && x.Provider == "rappi"
            && x.AttemptCount == attemptCount
            && (x.Status == LeasedWebhookStatus && x.EventType != CancellationEffectsEventType
                    && x.EventType != AcceptanceEffectsEventType
                || x.Status == CancellationEffectsLeased && x.EventType == CancellationEffectsEventType
                || x.Status == AcceptanceEffectsLeased && x.EventType == AcceptanceEffectsEventType));

    private static RappiOrderProcessingResult? TerminalOrderResult(ExternalDeliveryOrder? external) =>
        external is
        {
            Status: ExternalOrderStatus.Cancelled or ExternalOrderStatus.Rejected
            or ExternalOrderStatus.Expired or ExternalOrderStatus.ReconciliationRequired
        }
            ? new(false, external.Id, Held: true,
                Error: "La orden Rappi está cerrada o requiere conciliación; no puede aceptarse de nuevo.")
            : null;

    private async Task<bool> ProcessWebhookEventAsync(IntegrationWebhookEvent webhookEvent, CancellationToken ct)
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
                await ApplyCancellationCoreAsync(connection.Id, webhookEvent.PayloadJson, webhookEvent, ct);
                break;
            case CancellationEffectsEventType:
                await ResumeCancellationEffectsAsync(webhookEvent, ct);
                break;
            case AcceptanceEffectsEventType:
                await ApplyAcceptanceEffectsAsync(webhookEvent, ct);
                break;
            case "ORDER_OTHER_EVENT":
                return await ApplyOrderEventAsync(connection.Id, webhookEvent.PayloadJson, ct);
            case "MENU_APPROVED":
            case "MENU_REJECTED":
                await ApplyMenuStatusAsync(connection.Id, webhookEvent.EventType, webhookEvent.PayloadJson, ct, webhookEvent.EventKey);
                break;
            case "STORE_CONNECTIVITY":
                ApplyConnectivity(connection, webhookEvent.PayloadJson);
                break;
        }
        return true;
    }

    // Retained as a narrow private test seam. Real webhook processing always supplies
    // its leased inbox row, so the post-commit phase is durable.
    private Task ApplyCancellationAsync(int connectionId, string payload, CancellationToken ct) =>
        ApplyCancellationCoreAsync(connectionId, payload, null, ct);

    private async Task ApplyCancellationCoreAsync(
        int connectionId, string payload, IntegrationWebhookEvent? webhookEvent, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payload);
        var orderId = FindString(document.RootElement, "order_id")
            ?? throw new InvalidOperationException("El webhook de cancelación no contiene order_id.");
        var reason = BuildCancellationReason(document.RootElement);
        var connection = await db.DeliveryAppConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == connectionId && x.Provider == "rappi", ct)
            ?? throw new InvalidOperationException("La conexión del webhook no existe.");
        CancellationEffects? effects = null;
        string? checkpointEnvelope = null;
        await using (var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            await LockExternalOrderAsync(connection.TenantId, connectionId, orderId, ct);
            var external = await db.ExternalDeliveryOrders.FirstOrDefaultAsync(x =>
                x.ConnectionId == connectionId && x.ExternalOrderId == orderId, ct);
            if (external is null)
            {
                // A cancellation may arrive before NEW_ORDER. Reuse the existing terminal
                // entity as a veto; no order, financial payment or inventory is created.
                db.ExternalDeliveryOrders.Add(new ExternalDeliveryOrder
                {
                    TenantId = connection.TenantId,
                    ConnectionId = connectionId,
                    BranchId = connection.BranchId,
                    ExternalOrderId = orderId,
                    Status = ExternalOrderStatus.Cancelled,
                    RawPayloadJson = payload,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                });
            }
            else
            {
                await ReloadTrackedAsync(external, ct);
                if (external.InternalOrderId is not int internalOrderId)
                {
                    external.Status = ExternalOrderStatus.Cancelled;
                    external.LastError = null;
                }
                else
                {
                    // Match OrderMutationBehavior's parent lock. Re-read *after* waiting,
                    // including already-tracked payments, before checking settlement.
                    await LockOrderAsync(connection.TenantId, internalOrderId, ct);
                    var order = await db.ReloadOrderForManagementAsync(internalOrderId, ct)
                        ?? throw new InvalidOperationException("El pedido interno Rappi no existe.");
                    var payments = await db.AppPayments.Where(x => x.OrderId == internalOrderId).ToListAsync(ct);
                    foreach (var payment in payments) await ReloadTrackedAsync(payment, ct);
                    if (order.Status == OrderStatus.Cancelled)
                    {
                        external.Status = ExternalOrderStatus.Cancelled;
                        external.LastError = null;
                    }
                    else if (order.Status == OrderStatus.Delivered || payments.Any(x => x.IsSetted))
                    {
                        external.Status = ExternalOrderStatus.ReconciliationRequired;
                        external.LastError = "Rappi canceló una orden entregada o liquidada; requiere conciliación manual.";
                    }
                    else
                    {
                        effects = new(connection.TenantId, connectionId, external.Id, internalOrderId,
                            order.BranchId, order.Status, order.DeliveryRouteId,
                            KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(order, clock.UtcNow), reason);
                        foreach (var payment in payments.Where(x => !x.IsReversed))
                        {
                            payment.IsReversed = true;
                            payment.ReversedAt = clock.UtcNow;
                            payment.ReversalReason = reason;
                        }
                        if (inventory is not null && order.Status == OrderStatus.Taken)
                            await inventory.ReleaseOrderAsync(order, $"order:{order.Id}:rappi-cancel", ct);
                        order.Status = OrderStatus.Cancelled;
                        order.CancelledReason = reason;
                        order.AddStatusTime(OrderStatus.Cancelled, clock.UtcNow);
                        external.Status = ExternalOrderStatus.Cancelled;
                        external.LastError = null;
                    }
                }
            }
            await db.SaveChangesAsync(ct);
            if (effects is not null && webhookEvent is not null)
            {
                var envelope = JsonSerializer.Serialize(new CancellationEffectsEnvelope(payload, effects), JsonOptions);
                var checkpointed = await OwnedWebhookClaim(db.IntegrationWebhookEvents,
                        webhookEvent.Id, webhookEvent.AttemptCount)
                    .Where(x => x.TenantId == connection.TenantId && x.ConnectionId == connectionId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.EventType, CancellationEffectsEventType)
                        .SetProperty(x => x.Status, CancellationEffectsLeased)
                        .SetProperty(x => x.PayloadJson, envelope)
                        .SetProperty(x => x.UpdatedAt, clock.UtcNow), ct);
                if (checkpointed != 1)
                    throw new InvalidOperationException("Se perdió el reclamo del webhook antes de confirmar la cancelación.");
                // EventKey/PayloadHash remain those of the original provider delivery.
                // Only this server-side transition admits the trusted effects envelope.
                checkpointEnvelope = envelope;
            }
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        if (checkpointEnvelope is not null && webhookEvent is not null)
        {
            webhookEvent.EventType = CancellationEffectsEventType;
            webhookEvent.PayloadJson = checkpointEnvelope;
            webhookEvent.Status = CancellationEffectsLeased;
        }
        if (effects is not null)
            await ApplyCancellationEffectsAsync(effects, ct);
    }

    private async Task ResumeCancellationEffectsAsync(IntegrationWebhookEvent webhookEvent, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<CancellationEffectsEnvelope>(webhookEvent.PayloadJson, JsonOptions)
            ?? throw new InvalidOperationException("Falta el contexto de efectos de la cancelación.");
        if (envelope.Snapshot.TenantId != webhookEvent.TenantId
            || envelope.Snapshot.ConnectionId != webhookEvent.ConnectionId)
            throw new InvalidOperationException("El contexto de cancelación no pertenece al webhook.");
        await ApplyCancellationEffectsAsync(envelope.Snapshot, ct);
    }

    private async Task ApplyCancellationEffectsAsync(CancellationEffects effects, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Los efectos de cancelación requieren una transacción ya confirmada.");
        var validOrder = await db.ExternalDeliveryOrders.AsNoTracking().AnyAsync(x =>
            x.Id == effects.ExternalOrderId && x.TenantId == effects.TenantId
            && x.ConnectionId == effects.ConnectionId && x.InternalOrderId == effects.OrderId
            && x.InternalOrder != null && x.InternalOrder.BranchId == effects.BranchId
            && x.InternalOrder.Status == OrderStatus.Cancelled, ct);
        if (!validOrder)
            throw new InvalidOperationException("El pedido de los efectos de cancelación ya no coincide con su contexto.");

        // Routes are idempotent and run first. Finalization may call Google: neither
        // it nor realtime notifications belongs inside the core database transaction.
        await deliveryRouteWorkflow.OnOrderCancelledWhileRouteOpenAsync(effects.OrderId, ct);
        await deliveryRouteWorkflow.TryFinalizeRouteWhenAllTerminalAsync(effects.OrderId, effects.RouteId, ct);
        if (effects.NotifyKitchen)
            await notifications.NotifyOrderCancelledToKitchen(effects.BranchId, effects.OrderId, effects.Reason);
        if (effects.PreviousStatus == OrderStatus.OnTheWay)
        {
            var fullOrder = await orders.GetByIdWithFullDetailsAsync(effects.OrderId, ct);
            if (fullOrder is not null)
                await notifications.NotifyOrderModifiedToDelivery(mapper.Map<OrderDto>(fullOrder), "status");
        }
    }

    private sealed record CancellationEffectsEnvelope(string OriginalPayload, CancellationEffects Snapshot);
    private sealed record CancellationEffects(int TenantId, int ConnectionId, int ExternalOrderId,
        int OrderId, int BranchId, OrderStatus PreviousStatus, int? RouteId, bool NotifyKitchen, string Reason);

    private async Task<bool> ApplyOrderEventAsync(int connectionId, string payload, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payload);
        var orderId = FindString(document.RootElement, "order_id");
        if (string.IsNullOrWhiteSpace(orderId))
            throw new InvalidOperationException("El evento Rappi no contiene order_id.");
        var eventName = FindEventName(document.RootElement);
        var connection = await db.DeliveryAppConnections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == connectionId, ct);
        if (connection is null) return false;
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockExternalOrderAsync(connection.TenantId, connectionId, orderId, ct);
        var external = await db.ExternalDeliveryOrders.FirstOrDefaultAsync(x =>
            x.ConnectionId == connectionId && x.ExternalOrderId == orderId, ct);
        if (external is null) return false;
        await ReloadTrackedAsync(external, ct);
        if (external.InternalOrderId is not int internalOrderId)
            return TerminalOrderResult(external) is not null;
        await LockOrderAsync(connection.TenantId, internalOrderId, ct);
        var order = await db.ReloadOrderForManagementAsync(internalOrderId, ct);
        if (order is null) return false;
        if (eventName.Equals("hand_to_domiciliary", StringComparison.OrdinalIgnoreCase)
            && order.Status is not (OrderStatus.OnTheWay or OrderStatus.Delivered or OrderStatus.Cancelled))
        {
            if (inventory is not null)
                await inventory.ConsumeOrderAsync(order, $"order:{order.Id}:rappi-consume", ct);
            order.Status = OrderStatus.OnTheWay;
            order.AddStatusTime(OrderStatus.OnTheWay, clock.UtcNow);
        }
        else if (eventName.Equals("close_order", StringComparison.OrdinalIgnoreCase)
                 && order.Status is not (OrderStatus.Delivered or OrderStatus.Cancelled))
        {
            if (inventory is not null)
                await inventory.ConsumeOrderAsync(order, $"order:{order.Id}:rappi-consume", ct);
            order.Status = OrderStatus.Delivered;
            order.AddStatusTime(OrderStatus.Delivered, clock.UtcNow);
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        if (eventName.Equals("hand_to_domiciliary", StringComparison.OrdinalIgnoreCase)
            || eventName.Equals("close_order", StringComparison.OrdinalIgnoreCase))
        {
            var fullOrder = await orders.GetByIdWithFullDetailsAsync(internalOrderId, ct);
            if (fullOrder is not null)
            {
                var dto = mapper.Map<OrderDto>(fullOrder);
                await notifications.NotifyOrderModifiedToKitchen(dto, "status");
                await notifications.NotifyOrderModifiedToDelivery(dto, "status");
                await notifications.NotifyOrderManagementChanged(fullOrder.Id, fullOrder.BranchId,
                    fullOrder.OriginBranchId, fullOrder.ManagementVersion);
            }
        }
        return true;
    }

    private async Task LockExternalOrderAsync(int tenantId, int connectionId, string orderId, CancellationToken ct)
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            var key = $"{tenantId}:{connectionId}:{orderId}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(1380012112, hashtext({key}))", ct);
        }
    }

    private async Task LockOrderAsync(int tenantId, int orderId, CancellationToken ct)
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM \"order\" WHERE id = {orderId} AND tenant_id = {tenantId} FOR UPDATE", ct);
    }

    private Task ReloadTrackedAsync(object entity, CancellationToken ct) =>
        db is DbContext context ? context.Entry(entity).ReloadAsync(ct) : Task.CompletedTask;

    private async Task ApplyMenuStatusAsync(
        int connectionId,
        string eventType,
        string payload,
        CancellationToken ct,
        string? eventKey = null)
    {
        const string pollPrefix = "MENU_APPROVED_POLL:";
        const string approvalPendingError = "Rappi notificó aprobación de menú sin identificador de publicación; pendiente de confirmación mediante consulta a Rappi.";
        // Only locally generated poll keys carry a trusted publication identity.
        // Provider callbacks have no documented correlator; a delayed callback must
        // not approve/reject the next menu merely because it is currently pending.
        if (eventType != "MENU_APPROVED" || eventKey is null
            || !eventKey.StartsWith(pollPrefix, StringComparison.Ordinal)
            || !int.TryParse(eventKey[pollPrefix.Length..], out var publicationId))
        {
            if (eventType == "MENU_APPROVED"
                && !await db.RappiMenuPublications.AnyAsync(x => x.ConnectionId == connectionId
                    && (x.Status == "submitting" || x.Status == "submitted" || x.Status == "approval_review_required"), ct)
                && await db.RappiMenuPublications.AnyAsync(x => x.ConnectionId == connectionId && x.Status == "approved", ct))
            {
                var confirmedConnection = await db.DeliveryAppConnections.FirstOrDefaultAsync(x => x.Id == connectionId, ct);
                if (confirmedConnection?.LastError == approvalPendingError)
                    confirmedConnection.LastError = null;
                return;
            }
            if (eventType == "MENU_REJECTED")
            {
                // No publication correlator is documented. Hold all potentially affected
                // pending submissions without falsely labelling any one as rejected.
                // A delayed rejection can cause a conservative hold; manual review is required.
                var pending = await db.RappiMenuPublications.Where(x => x.ConnectionId == connectionId
                    && (x.Status == "submitting" || x.Status == "submitted")).ToListAsync(ct);
                foreach (var item in pending)
                {
                    item.Status = "approval_review_required";
                    item.Error = "Rappi notificó rechazo sin identificador de publicación; aprobación automática suspendida hasta revisión manual.";
                    item.UpdatedAt = clock.UtcNow;
                }
            }
            var connection = await db.DeliveryAppConnections.FirstOrDefaultAsync(x => x.Id == connectionId, ct);
            if (connection is not null)
                connection.LastError = eventType == "MENU_REJECTED"
                    ? "Rappi notificó rechazo de menú sin identificador de publicación; revisa el motivo y el menú pendiente en Rappi antes de volver a publicar."
                    : approvalPendingError;
            return;
        }
        var publication = await db.RappiMenuPublications
            .FirstOrDefaultAsync(x => x.Id == publicationId
                && x.ConnectionId == connectionId && x.Status == "submitted", ct);
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

    private async Task<AcceptanceEvidence> GetAcceptanceEvidenceAsync(string orderId, CancellationToken ct)
    {
        var result = await rappi.GetOrderEventsAsync(orderId, ct);
        if (!result.Success || result.Events is null)
            return new(false, false, "No se pudo confirmar el estado Rappi; se consultará de nuevo antes de reintentar. "
                + Limit(result.Error, 500));
        if (result.Events.Count == 0)
            return new(false, true, "Rappi no devolvió eventos que confirmen aceptación; no se presume expiración.");

        // Do not assume array order or ignore an unknown/newer event. A repeated
        // taken can also mean courier assignment, so it cannot undo any advanced
        // or terminal event. This is acceptance recovery, not state reconciliation.
        if (result.Events.Any(x => x.EventTime.Offset != TimeSpan.Zero || x.EventTime > clock.UtcNow))
            return new(false, false, "El historial Rappi contiene fechas ambiguas o futuras; requiere revisión antes de aceptar.");
        if (result.Events.Any(x => x.Event is not ("taken_visible_order" or "replace_storekeeper")))
            return new(false, false, "El historial Rappi contiene cancelación, avance o eventos no reconocidos; requiere revisión y no se reabre como Taken.");
        if (result.Events.GroupBy(x => x.EventTime).Any(group => group.Select(x => x.Event).Distinct().Count() > 1))
            return new(false, false, "El historial Rappi contiene eventos distintos con la misma fecha; requiere revisión.");
        var takenEvents = result.Events.Where(x => x.Event == "taken_visible_order").ToList();
        if (takenEvents.Count == 0
            || result.Events.Any(x => x.Event == "replace_storekeeper" && x.EventTime < takenEvents.Min(t => t.EventTime)))
            return new(false, false, "El historial Rappi no confirma una aceptación coherente; requiere revisión.");
        return new(true, false, null);
    }

    private sealed record AcceptanceEvidence(bool Accepted, bool MayAttempt, string? Error);

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
            if (!mapping.Product.Active)
                errors.Add($"{line.Name}: producto inactivo o agotado.");
        }
        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task AppendInventoryValidationErrorsAsync(List<string> errors, DeliveryAppConnection connection, ParsedRappiOrder order, CancellationToken ct)
    {
        if (inventory is null || errors.Count > 0) return;
        var mappings = connection.ProductMappings.Where(x => x.IsSelected)
            .ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
        var requested = order.Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.Sku) && mappings.ContainsKey(x.Sku))
            .GroupBy(x => mappings[x.Sku].ProductId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
        var availability = await inventory.GetAvailabilityAsync(requested.Keys.ToArray(), connection.BranchId, ct);
        var byProduct = availability.ToDictionary(x => x.ProductId);
        foreach (var item in requested)
        {
            if (!byProduct.TryGetValue(item.Key, out var available)
                || !available.Available
                || available.MaximumQuantity.HasValue && available.MaximumQuantity.Value < item.Value)
            {
                var name = mappings.Values.First(x => x.ProductId == item.Key).Product.Name;
                errors.Add($"{name}: inventario insuficiente.");
            }
        }
    }

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

    private void ClearTracking()
    {
        if (db is DbContext context)
            context.ChangeTracker.Clear();
    }

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
