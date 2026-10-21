using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class RappiIntegrationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RappiOptions> options,
    IBackgroundWorkSignal<RappiWork> workSignal,
    ILogger<RappiIntegrationWorker> logger) : BackgroundService
{
    private readonly RappiOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRecovery = DateTimeOffset.MinValue;
        var nextCapabilitySync = DateTimeOffset.MinValue;
        var nextCleanup = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var syncCapabilities = now >= nextCapabilitySync;
                var runRecovery = now >= nextRecovery;
                var runCleanup = now >= nextCleanup;
                await TenantWorkerRunner.RunForEachActiveTenantAsync(
                    scopeFactory,
                    async (services, _) =>
                    {
                        var processor = services.GetRequiredService<IRappiOrderProcessor>();
                        var db = services.GetRequiredService<ApplicationDbContext>();
                        var rappi = services.GetRequiredService<IRappiDeliveryProvider>();
                        var clock = services.GetRequiredService<IClock>();
                        var inventory = services.GetRequiredService<IInventoryService>();

                        await processor.ProcessPendingWebhookEventsAsync(stoppingToken);
                        await ProcessReadyOutboxAsync(db, rappi, clock, stoppingToken);
                        await ReconcileAvailabilityStateAsync(db, inventory, clock, stoppingToken);
                        await ProcessAvailabilityAsync(db, rappi, clock, stoppingToken);
                        if (syncCapabilities)
                            await SyncCapabilitiesAsync(db, rappi, stoppingToken);
                        if (runRecovery)
                        {
                            await RecoverSentOrdersAsync(db, rappi, processor, stoppingToken);
                            await RecoverAcceptedOrdersAsync(db, processor, stoppingToken);
                            await RecoverMenuApprovalsAsync(db, rappi, clock, stoppingToken);
                        }
                        if (runCleanup)
                            await PurgePiiAsync(db, clock, stoppingToken);
                    },
                    stoppingToken);
                if (syncCapabilities) nextCapabilitySync = now.AddMinutes(15);
                if (runRecovery) nextRecovery = now.AddSeconds(Math.Max(15, options.RecoveryIntervalSeconds));
                if (runCleanup) nextCleanup = now.AddHours(Math.Max(1, options.PiiCleanupIntervalHours));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Rappi background cycle failed.");
            }

            var waitNow = DateTimeOffset.UtcNow;
            var nextScheduled = new[] { nextRecovery, nextCapabilitySync, nextCleanup }.Min();
            var delay = nextScheduled <= waitNow ? TimeSpan.FromSeconds(5) : nextScheduled - waitNow;
            await workSignal.WaitAsync(delay, stoppingToken);
        }
    }

    private static async Task ProcessReadyOutboxAsync(
        ApplicationDbContext db,
        IRappiDeliveryProvider rappi,
        IClock clock,
        CancellationToken ct)
    {
        var candidates = await db.ExternalDeliveryOrders
            .AsNoTracking()
            .Where(x =>
                x.Connection.Provider == "rappi"
                && x.Connection.IsActive
                && x.Connection.IsVerified
                && x.Status == ExternalOrderStatus.Accepted
                && x.Store != null
                && x.Store.ManualReadyForPickupEnabled
                && x.InternalOrder != null
                && x.InternalOrder.Status == OrderStatus.Ready)
            .OrderBy(x => x.CreatedAt)
            .Take(100)
            .Select(x => new { x.ConnectionId, x.ExternalOrderId, ExternalDeliveryOrderId = x.Id })
            .ToListAsync(ct);
        if (candidates.Count > 0)
        {
            var candidateKeys = candidates
                .Select(x => $"READY_FOR_PICKUP:{x.ConnectionId}:{x.ExternalOrderId}")
                .ToList();
            var existingKeys = await db.IntegrationWebhookEvents
                .AsNoTracking()
                .Where(x => candidateKeys.Contains(x.EventKey))
                .Select(x => x.EventKey)
                .ToHashSetAsync(ct);
            foreach (var candidate in candidates)
            {
                var eventKey = $"READY_FOR_PICKUP:{candidate.ConnectionId}:{candidate.ExternalOrderId}";
                if (existingKeys.Contains(eventKey))
                    continue;
                db.IntegrationWebhookEvents.Add(new IntegrationWebhookEvent
                {
                    ConnectionId = candidate.ConnectionId,
                    Provider = "rappi",
                    EventKey = eventKey,
                    EventType = "READY_FOR_PICKUP_OUTBOX",
                    PayloadHash = string.Empty,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        externalOrderId = candidate.ExternalOrderId,
                        candidate.ExternalDeliveryOrderId
                    }),
                    Status = "outbox_pending",
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                });
            }
            await db.SaveChangesAsync(ct);
        }

        // A lease is substantially longer than the complete operation timeout below,
        // including OAuth, retries and response-body reads (ResponseHeadersRead).
        // Recover only abandoned claims; current sends remain exclusive.
        var expiredBefore = clock.UtcNow - RappiReadyOutboxPolicy.ProcessingLease;
        var abandoned = await db.IntegrationWebhookEvents.AsNoTracking()
            .Where(x => x.Provider == "rappi"
                && x.EventType == RappiReadyOutboxPolicy.EventType
                && x.Status == RappiReadyOutboxPolicy.Processing
                && x.UpdatedAt < expiredBefore)
            .OrderBy(x => x.UpdatedAt).Take(50).ToListAsync(ct);
        foreach (var row in abandoned)
            await TransitionAsync(row,
                row.AttemptCount >= RappiReadyOutboxPolicy.MaxAttempts
                    ? RappiReadyOutboxPolicy.Failed : RappiReadyOutboxPolicy.Pending,
                row.AttemptCount, "El envío READY_FOR_PICKUP fue interrumpido; se recuperó su intento pendiente.", null);

        var rows = await db.IntegrationWebhookEvents.AsNoTracking()
            .Where(x => x.Provider == "rappi"
                && x.EventType == RappiReadyOutboxPolicy.EventType
                && x.Status == RappiReadyOutboxPolicy.Pending)
            .OrderBy(x => x.CreatedAt).Take(20).ToListAsync(ct);
        foreach (var row in rows)
        {
            if (row.AttemptCount >= RappiReadyOutboxPolicy.MaxAttempts)
            {
                await TransitionAsync(row, RappiReadyOutboxPolicy.Failed, row.AttemptCount,
                    "READY_FOR_PICKUP agotó sus 10 intentos. Requiere reintento manual.", null);
                continue;
            }
            var claimed = false;
            try
            {
                using var document = JsonDocument.Parse(row.PayloadJson);
                var externalOrderId = document.RootElement.GetProperty("externalOrderId").GetString();
                if (string.IsNullOrWhiteSpace(externalOrderId))
                    throw new InvalidOperationException("Orden externa ausente en outbox.");
                var external = await db.ExternalDeliveryOrders.AsNoTracking()
                    .Include(x => x.Connection).Include(x => x.Store).Include(x => x.InternalOrder)
                    .FirstOrDefaultAsync(x => x.ConnectionId == row.ConnectionId
                        && x.ExternalOrderId == externalOrderId, ct);
                if (external is null
                    || row.EventKey != RappiReadyOutboxPolicy.EventKey(row.ConnectionId, externalOrderId))
                {
                    await TransitionAsync(row, RappiReadyOutboxPolicy.Failed, row.AttemptCount,
                        "El envío READY_FOR_PICKUP no corresponde a una orden de esta integración.", null);
                    continue;
                }
                if (!RappiReadyOutboxPolicy.CanSend(external))
                {
                    await TransitionAsync(row,
                        external.InternalOrder?.Status is OrderStatus.Cancelled or OrderStatus.Delivered
                            ? RappiReadyOutboxPolicy.Skipped : RappiReadyOutboxPolicy.Failed,
                        row.AttemptCount,
                        "READY_FOR_PICKUP no enviado: la orden debe estar lista y la integración y capacidad habilitadas.", null);
                    continue;
                }

                var claimedAt = clock.UtcNow;
                var nextAttempt = row.AttemptCount + 1;
                var affected = await ExpectedRow(row)
                    .Where(x => db.ExternalDeliveryOrders.Any(order => order.Id == external.Id
                        && order.ConnectionId == x.ConnectionId
                        && order.Connection.Provider == "rappi"
                        && order.Connection.IsActive && order.Connection.IsVerified
                        && order.Status == ExternalOrderStatus.Accepted
                        && order.Store != null && order.Store.ManualReadyForPickupEnabled
                        && order.InternalOrder != null && order.InternalOrder.Status == OrderStatus.Ready))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, RappiReadyOutboxPolicy.Processing)
                        .SetProperty(x => x.AttemptCount, nextAttempt)
                        .SetProperty(x => x.LastError, (string?)null)
                        .SetProperty(x => x.UpdatedAt, claimedAt), ct);
                if (affected == 0)
                    continue;
                claimed = true;
                row.Status = RappiReadyOutboxPolicy.Processing;
                row.AttemptCount = nextAttempt;
                row.UpdatedAt = claimedAt;
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptTimeout.CancelAfter(RappiReadyOutboxPolicy.AttemptTimeout);
                var result = await rappi.ReadyForPickupAsync(externalOrderId, attemptTimeout.Token);
                await TransitionAsync(row,
                    result.Success ? "processed"
                        : nextAttempt >= RappiReadyOutboxPolicy.MaxAttempts
                            ? RappiReadyOutboxPolicy.Failed : RappiReadyOutboxPolicy.Pending,
                    nextAttempt,
                    result.Success ? null : RappiReadyOutboxPolicy.FailureMessage(result.StatusCode),
                    result.Success ? clock.UtcNow : null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Do not erase the durable claim on shutdown; its lease makes it recoverable.
                throw;
            }
            catch (Exception)
            {
                await TransitionAsync(row,
                    !claimed || row.AttemptCount >= RappiReadyOutboxPolicy.MaxAttempts
                        ? RappiReadyOutboxPolicy.Failed : RappiReadyOutboxPolicy.Pending,
                    row.AttemptCount, RappiReadyOutboxPolicy.FailureMessage(null), null);
            }
        }

        IQueryable<IntegrationWebhookEvent> ExpectedRow(IntegrationWebhookEvent snapshot) =>
            db.IntegrationWebhookEvents.Where(x => x.Id == snapshot.Id
                && x.ConnectionId == snapshot.ConnectionId
                && x.Provider == "rappi" && x.EventType == RappiReadyOutboxPolicy.EventType
                && x.Status == snapshot.Status && x.AttemptCount == snapshot.AttemptCount
                && x.UpdatedAt == snapshot.UpdatedAt);

        Task<int> TransitionAsync(IntegrationWebhookEvent snapshot, string status, int attempts,
            string? error, DateTime? processedAt) =>
            ExpectedRow(snapshot).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.AttemptCount, attempts)
                .SetProperty(x => x.LastError, error)
                .SetProperty(x => x.ProcessedAt, processedAt)
                .SetProperty(x => x.UpdatedAt, clock.UtcNow), ct);
    }

    private async Task SyncCapabilitiesAsync(
        ApplicationDbContext db,
        IRappiDeliveryProvider rappi,
        CancellationToken ct)
    {
        var connections = await db.DeliveryAppConnections
            .AsNoTracking()
            .Include(x => x.Stores)
            .Where(x => x.Provider == "rappi" && x.IsActive && x.IsVerified)
            .ToListAsync(ct);
        if (connections.Count == 0)
            return;

        var storeResult = await rappi.TestConnectionAsync(ct);
        if (!storeResult.Success || storeResult.Stores is null)
        {
            logger.LogWarning("Rappi store capability sync failed: {Error}", storeResult.Error);
            return;
        }

        var remoteStoreIds = storeResult.Stores.Select(x => x.StoreId).ToHashSet();
        foreach (var storeId in connections
                     .SelectMany(x => x.Stores)
                     .Select(x => x.RappiStoreId)
                     .Where(remoteStoreIds.Contains)
                     .Distinct())
        {
            var status = await rappi.SetStoreIntegratedAsync(storeId, true, ct);
            if (!status.Success)
                logger.LogWarning("Rappi store {StoreId} integration sync failed: {Error}", storeId, status.Error);
        }

        foreach (var storeId in connections
                     .SelectMany(x => x.Stores)
                     .Where(x => x.IsParent)
                     .Select(x => x.RappiStoreId)
                     .Distinct())
        {
            var menuStatus = await rappi.GetMenuApprovalAsync(storeId, ct);
            if (!menuStatus.Success)
                logger.LogWarning("Rappi menu status sync failed for {StoreId}: {Error}", storeId, menuStatus.Error);
        }
    }

    private static async Task ReconcileAvailabilityStateAsync(
        ApplicationDbContext db,
        IInventoryService inventory,
        IClock clock,
        CancellationToken ct)
    {
        var connections = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .Include(x => x.ProductMappings)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.Category)
            .Where(x => x.Provider == "rappi" && x.IsActive)
            .ToListAsync(ct);
        if (connections.Count == 0)
            return;

        var connectionIds = connections.Select(x => x.Id).ToList();
        var existing = await db.RappiAvailabilityStates
            .Where(x => connectionIds.Contains(x.ConnectionId))
            .ToDictionaryAsync(x => (x.StoreId, x.ProductMappingId), ct);
        var changed = false;
        foreach (var connection in connections)
        {
            var selectedMappings = connection.ProductMappings.Where(x => x.IsSelected).ToArray();
            var availability = await inventory.GetAvailabilityAsync(
                selectedMappings.Select(x => x.ProductId).Distinct().ToArray(),
                connection.BranchId,
                ct);
            var availabilityByProduct = availability.ToDictionary(x => x.ProductId);
            foreach (var store in connection.Stores.Where(x => !string.IsNullOrWhiteSpace(x.StoreIntegrationId)))
            foreach (var mapping in selectedMappings)
            {
                var desired = availabilityByProduct.TryGetValue(mapping.ProductId, out var productAvailability)
                    && productAvailability.Available;
                if (!existing.TryGetValue((store.Id, mapping.Id), out var state))
                {
                    state = new RappiAvailabilityState
                    {
                        ConnectionId = connection.Id,
                        StoreId = store.Id,
                        ProductMappingId = mapping.Id,
                        DesiredAvailable = desired,
                        Status = "pending",
                        CreatedAt = clock.UtcNow,
                        UpdatedAt = clock.UtcNow
                    };
                    db.RappiAvailabilityStates.Add(state);
                    existing[(store.Id, mapping.Id)] = state;
                    changed = true;
                }
                else if (state.DesiredAvailable != desired)
                {
                    state.DesiredAvailable = desired;
                    state.Status = "pending";
                    state.NextAttemptAt = null;
                    state.UpdatedAt = clock.UtcNow;
                    changed = true;
                }
            }
        }
        if (changed)
            await db.SaveChangesAsync(ct);
    }

    private static async Task ProcessAvailabilityAsync(
        ApplicationDbContext db,
        IRappiDeliveryProvider rappi,
        IClock clock,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var states = await db.RappiAvailabilityStates
            .Include(x => x.Store)
            .Include(x => x.ProductMapping)
            .Where(x =>
                x.Status == "pending"
                && (!x.NextAttemptAt.HasValue || x.NextAttemptAt <= now)
                && x.Connection.Provider == "rappi"
                && x.Connection.IsActive && x.Connection.IsVerified
                && x.Connection.TenantId == x.TenantId
                && x.Store.TenantId == x.TenantId
                && x.ProductMapping.TenantId == x.TenantId
                && x.ProductMapping.Product.TenantId == x.TenantId
                && x.ProductMapping.Product.Category.TenantId == x.TenantId
                && x.ProductMapping.Product.Category.BranchId == x.Connection.BranchId
                && x.Connection.Branch.TenantId == x.TenantId
                && x.Connection.Branch.IsActive
                && x.Store.ConnectionId == x.ConnectionId
                && x.ProductMapping.ConnectionId == x.ConnectionId
                && x.ProductMapping.IsSelected
                && x.Store.StoreIntegrationId != null
                && x.Store.StoreIntegrationId.Trim() != "")
            .OrderBy(x => x.Id)
            .Take(500)
            .ToListAsync(ct);
        if (states.Count == 0)
            return;

        var requests = states
            .GroupBy(x => x.StoreId)
            .Select(group => new RappiAvailabilityRequest(
                group.First().Store.StoreIntegrationId!,
                group.Where(x => x.DesiredAvailable)
                    .Select(x => x.ProductMapping.Sku)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                group.Where(x => !x.DesiredAvailable)
                    .Select(x => x.ProductMapping.Sku)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();
        var result = await rappi.SetAvailabilityAsync(requests, ct);
        foreach (var state in states)
        {
            state.AttemptCount++;
            state.LastError = result.Error;
            if (result.Success)
            {
                state.LastSyncedAvailable = state.DesiredAvailable;
                state.Status = "synced";
                state.NextAttemptAt = null;
            }
            else
            {
                state.NextAttemptAt = now.AddSeconds(Math.Min(900, Math.Pow(2, Math.Min(8, state.AttemptCount))));
            }
        }
        if (result.Success)
        {
            var connectionIds = states.Select(x => x.ConnectionId).Distinct().ToList();
            await db.DeliveryAppConnections
                .Where(x => connectionIds.Contains(x.Id))
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(x => x.LastAvailabilitySyncAt, now), ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task RecoverSentOrdersAsync(
        ApplicationDbContext db,
        IRappiDeliveryProvider rappi,
        IRappiOrderProcessor processor,
        CancellationToken ct)
    {
        var connections = await db.DeliveryAppConnections
            .AsNoTracking()
            .Include(x => x.Stores)
            .Where(x => x.Provider == "rappi" && x.IsActive && x.IsVerified)
            .ToListAsync(ct);
        if (connections.Count == 0)
            return;

        var result = await rappi.GetSentOrdersAsync(ct);
        if (!result.Success || result.RawOrders is null)
            return;
        foreach (var raw in result.RawOrders)
        {
            var storeIdentifier = FindStoreIdentifier(raw);
            var connection = connections.FirstOrDefault(x => x.Stores.Any(store =>
                store.RappiStoreId == storeIdentifier
                || store.StoreIntegrationId == storeIdentifier));
            if (connection is not null)
                await processor.IngestNewOrderAsync(connection.Id, raw, ct);
        }
    }

    private static async Task RecoverAcceptedOrdersAsync(
        ApplicationDbContext db,
        IRappiOrderProcessor processor,
        CancellationToken ct)
    {
        var pendingIds = await db.ExternalDeliveryOrders
            .AsNoTracking()
            .Where(x =>
                !x.InternalOrderId.HasValue
                && (x.Status == ExternalOrderStatus.Processing
                    || x.Status == ExternalOrderStatus.SyncError))
            .OrderBy(x => x.LastAttemptAt)
            .Select(x => x.Id)
            .Take(20)
            .ToListAsync(ct);
        foreach (var id in pendingIds)
            await processor.RevalidateAndAcceptAsync(id, null, ct);
    }

    private static async Task RecoverMenuApprovalsAsync(
        ApplicationDbContext db,
        IRappiDeliveryProvider rappi,
        IClock clock,
        CancellationToken ct)
    {
        var publications = await db.RappiMenuPublications
            .Where(x => x.Status == "submitted")
            .OrderBy(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(ct);
        foreach (var publication in publications)
        {
            // The provider approval endpoint identifies only a store, not a publication.
            // Historical parallel submissions cannot be safely disambiguated by polling.
            var pending = await db.RappiMenuPublications.CountAsync(x =>
                x.ConnectionId == publication.ConnectionId && x.StoreId == publication.StoreId
                && (x.Status == "submitting" || x.Status == "submitted" || x.Status == "approval_review_required"), ct);
            if (pending != 1)
            {
                publication.Error = "Hay varios menús pendientes para esta tienda; requiere revisión manual con Rappi.";
                await db.SaveChangesAsync(ct);
                continue;
            }
            var result = await rappi.GetMenuApprovalAsync(publication.StoreId, ct);
            if (!result.Success)
                continue;
            var eventKey = $"MENU_APPROVED_POLL:{publication.Id}";
            if (await db.IntegrationWebhookEvents.AnyAsync(x =>
                    x.ConnectionId == publication.ConnectionId
                    && x.EventKey == eventKey, ct))
                continue;
            db.IntegrationWebhookEvents.Add(new IntegrationWebhookEvent
            {
                ConnectionId = publication.ConnectionId,
                Provider = "rappi",
                EventKey = eventKey,
                EventType = "MENU_APPROVED",
                PayloadHash = string.Empty,
                PayloadJson = "{}",
                Status = "received",
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task PurgePiiAsync(
        ApplicationDbContext db,
        IClock clock,
        CancellationToken ct)
    {
        var connections = await db.DeliveryAppConnections
            .AsNoTracking()
            .Select(x => new { x.Id, x.PiiRetentionDays })
            .ToListAsync(ct);
        foreach (var connection in connections)
        {
            var cutoff = clock.UtcNow.AddDays(-Math.Max(1, connection.PiiRetentionDays));
            var orders = await db.ExternalDeliveryOrders
                .Where(x =>
                    x.ConnectionId == connection.Id
                    && !x.PiiPurgedAt.HasValue
                    && x.CreatedAt < cutoff)
                .Take(500)
                .ToListAsync(ct);
            foreach (var order in orders)
            {
                order.CustomerName = "Cliente Rappi";
                order.CustomerPhone = null;
                order.DeliveryAddress = null;
                order.RawPayloadJson = "{}";
                order.PiiPurgedAt = clock.UtcNow;
                if (order.InternalOrderId.HasValue)
                {
                    await db.Orders
                        .Where(x => x.Id == order.InternalOrderId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.GuestName, "Cliente Rappi")
                            .SetProperty(x => x.ExternalCustomerPhone, (string?)null)
                            .SetProperty(x => x.ExternalDeliveryAddress, (string?)null), ct);
                }
            }
            var webhookEvents = await db.IntegrationWebhookEvents
                .Where(x =>
                    x.ConnectionId == connection.Id
                    && x.CreatedAt < cutoff
                    && x.PayloadJson != "{}")
                .Take(500)
                .ToListAsync(ct);
            foreach (var webhookEvent in webhookEvents)
                webhookEvent.PayloadJson = "{}";
            if (orders.Count > 0 || webhookEvents.Count > 0)
                await db.SaveChangesAsync(ct);
        }
    }

    private static string? FindStoreIdentifier(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        if (root.TryGetProperty("store", out var store))
            return GetString(store, "internal_id") ?? GetString(store, "external_id");
        if (root.TryGetProperty("order_detail", out var detail)
            && detail.TryGetProperty("store", out store))
            return GetString(store, "internal_id") ?? GetString(store, "external_id");
        return null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
            ? value.ToString()
            : null;

}
