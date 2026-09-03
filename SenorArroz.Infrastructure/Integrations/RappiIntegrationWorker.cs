using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

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
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IRappiOrderProcessor>();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var rappi = scope.ServiceProvider.GetRequiredService<IRappiDeliveryProvider>();
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();

                await processor.ProcessPendingWebhookEventsAsync(stoppingToken);
                await ProcessReadyOutboxAsync(db, rappi, clock, stoppingToken);
                await ReconcileAvailabilityStateAsync(db, clock, stoppingToken);
                await ProcessAvailabilityAsync(db, rappi, clock, stoppingToken);

                var now = DateTimeOffset.UtcNow;
                if (now >= nextCapabilitySync)
                {
                    await SyncCapabilitiesAsync(db, rappi, stoppingToken);
                    nextCapabilitySync = now.AddMinutes(15);
                }
                if (now >= nextRecovery)
                {
                    await RecoverSentOrdersAsync(db, rappi, processor, stoppingToken);
                    await RecoverAcceptedOrdersAsync(db, processor, stoppingToken);
                    await RecoverMenuApprovalsAsync(db, rappi, clock, stoppingToken);
                    nextRecovery = now.AddSeconds(Math.Max(15, options.RecoveryIntervalSeconds));
                }
                if (now >= nextCleanup)
                {
                    await PurgePiiAsync(db, clock, stoppingToken);
                    nextCleanup = now.AddHours(Math.Max(1, options.PiiCleanupIntervalHours));
                }
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
                x.Store != null
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

        var rows = await db.IntegrationWebhookEvents
            .Where(x =>
                x.Provider == "rappi"
                && x.EventType == "READY_FOR_PICKUP_OUTBOX"
                && x.Status == "outbox_pending"
                && x.AttemptCount < 10)
            .OrderBy(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            row.AttemptCount++;
            try
            {
                using var document = JsonDocument.Parse(row.PayloadJson);
                var externalOrderId = document.RootElement
                    .GetProperty("externalOrderId")
                    .GetString();
                if (string.IsNullOrWhiteSpace(externalOrderId))
                    throw new InvalidOperationException("Orden externa ausente en outbox.");
                var result = await rappi.ReadyForPickupAsync(externalOrderId, ct);
                row.Status = result.Success ? "processed" : "outbox_pending";
                row.ProcessedAt = result.Success ? clock.UtcNow : null;
                row.LastError = result.Error;
            }
            catch (Exception ex)
            {
                row.LastError = ex.Message.Length <= 1000 ? ex.Message : ex.Message[..1000];
            }
        }
        if (rows.Count > 0)
            await db.SaveChangesAsync(ct);
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
            foreach (var store in connection.Stores.Where(x => !string.IsNullOrWhiteSpace(x.StoreIntegrationId)))
            foreach (var mapping in connection.ProductMappings.Where(x => x.IsSelected))
            {
                var desired = IsAvailable(mapping.Product);
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
                && x.Store.StoreIntegrationId != null)
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
            .AsNoTracking()
            .Where(x => x.Status == "submitted")
            .OrderBy(x => x.CreatedAt)
            .Take(20)
            .Select(x => new { x.Id, x.ConnectionId, x.StoreId })
            .ToListAsync(ct);
        foreach (var publication in publications)
        {
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

    private static bool IsAvailable(Product product) =>
        product.Active
        && (product.Category.Name.Contains("arroz", StringComparison.OrdinalIgnoreCase)
            || !product.Stock.HasValue
            || product.Stock.Value > 0);
}
