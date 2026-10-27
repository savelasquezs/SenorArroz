using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
public sealed class RappiIntegrationsController(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IIntegrationSecretProtector protector,
    IRappiDeliveryProvider rappi,
    IRappiOrderProcessor orderProcessor,
    IOptions<ApiPublicOptions> apiPublicOptions,
    IBackgroundWorkSignal<RappiWork> workSignal,
    ITenantExecutionContext tenantExecutionContext,
    IOptions<RappiOptions>? rappiOptions = null) : ControllerBase
{
    private readonly RappiOptions options = rappiOptions?.Value ?? new();
    private static readonly string[] WebhookEvents =
    [
        "NEW_ORDER",
        "ORDER_EVENT_CANCEL",
        "ORDER_OTHER_EVENT",
        "MENU_APPROVED",
        "MENU_REJECTED",
        "PING",
        "STORE_CONNECTIVITY"
    ];

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpGet("api/branches/{branchId:int}/integrations/apps")]
    public async Task<ActionResult<ApiResponse<object>>> GetApps(int branchId, CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();
        var connection = await ConnectionQuery()
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            providers = new object[]
            {
                new
                {
                    key = "rappi",
                    name = "Rappi",
                    environment = options.Environment,
                    readyForPickupAllowed = options.Environment == "sandbox" || options.ProductionReadyForPickupApproved,
                    available = true,
                    connection = connection is null ? null : ToDto(connection)
                },
                new { key = "didi_food", name = "DiDi Food", available = false, connection = (object?)null }
            }
        }));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPut("api/branches/{branchId:int}/integrations/apps/rappi")]
    public async Task<ActionResult<ApiResponse<object>>> Upsert(
        int branchId,
        [FromBody] UpsertRappiConnectionDto dto,
        CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();
        if (options.Environment == "production" && !options.ProductionReadyForPickupApproved
            && dto.Stores.Any(x => x.ManualReadyForPickupEnabled))
            return BadRequest(ApiResponse<object>.ErrorResponse("Rappi aún no ha autorizado READY_FOR_PICKUP en producción."));
        var branchTenantId = await db.Branches.Where(x => x.Id == branchId)
            .Select(x => (int?)x.TenantId).FirstOrDefaultAsync(ct);
        if (!branchTenantId.HasValue)
            return NotFound();
        if (!await db.Apps.AnyAsync(x =>
                x.Id == dto.FinancialAppId
                && x.Bank.BranchId == branchId
                && x.Active, ct))
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "Selecciona una app financiera activa de esta sucursal."));
        if (!await db.Customers.AnyAsync(x =>
                x.Id == dto.CustomerId
                && x.BranchId == branchId
                && x.Active, ct))
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "Selecciona el cliente interno Rappi de esta sucursal."));
        if (dto.EstimatedCommissionRate is < 0 or > 1)
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "La comisión estimada debe estar entre 0 y 1."));
        if (dto.Stores is { Count: > 0 } && dto.Stores.Count(x => x.IsParent) != 1)
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "Debe existir exactamente una tienda padre."));

        var technicalUserId = dto.TechnicalUserId
            ?? await db.Users
                .Where(x =>
                    x.BranchId == branchId
                    && x.Email == "integracion-rappi@senorarroz.internal")
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(ct);
        if (!technicalUserId.HasValue
            || !await db.Users.AnyAsync(x => x.Id == technicalUserId && x.BranchId == branchId, ct))
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "Ejecuta el script Rappi v2 para crear el usuario técnico de la sucursal."));

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        // Store identifiers are tenant-wide routing keys. Serialize this small configuration
        // transaction across branches as well, including first-time connection creation.
        if (transaction is not null && db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(1380012113, {branchTenantId.Value})", ct);
        if (transaction is not null)
            await db.DeliveryAppConnections
                .Where(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt, x => x.UpdatedAt), ct);

        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        if (connection is null)
        {
            connection = new DeliveryAppConnection
            {
                BranchId = branchId,
                Provider = "rappi",
                Environment = options.Environment,
                PublicId = Guid.NewGuid(),
                CreatedAt = clock.UtcNow
            };
            db.DeliveryAppConnections.Add(connection);
        }

        // Validate the complete proposed store set without changing historical tracked rows.
        var proposed = new DeliveryAppConnection
        {
            Environment = options.Environment,
            Stores = connection.Stores.Select(x => new DeliveryAppStore
            {
                RappiStoreId = x.RappiStoreId,
                StoreIntegrationId = x.StoreIntegrationId,
                Name = x.Name,
                IsParent = x.IsParent,
                ManualReadyForPickupEnabled = x.ManualReadyForPickupEnabled
            }).ToList()
        };
        var storesError = UpsertStores(proposed, dto.Stores);
        if (storesError is not null)
            return BadRequest(ApiResponse<object>.ErrorResponse(storesError));
        var proposedIds = proposed.Stores.Select(x => x.RappiStoreId.Trim()).ToList();
        var proposedIntegrationIds = proposed.Stores.Where(x => !string.IsNullOrWhiteSpace(x.StoreIntegrationId))
            .Select(x => x.StoreIntegrationId!.Trim()).ToList();
        if (await db.DeliveryAppStores.AnyAsync(x => x.ConnectionId != connection.Id
                && x.Connection.Provider == "rappi" && x.Connection.Environment == options.Environment
                && (proposedIds.Contains(x.RappiStoreId.Trim())
                    || proposedIntegrationIds.Contains(x.RappiStoreId.Trim())
                    || (x.StoreIntegrationId != null && (proposedIntegrationIds.Contains(x.StoreIntegrationId.Trim())
                        || proposedIds.Contains(x.StoreIntegrationId.Trim())))), ct))
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "Una tienda Rappi ya está asignada a otra conexión de este negocio; revisa el mapeo entre sucursales."));
        var routingChanged = !connection.Stores.Select(x =>
                (x.RappiStoreId.Trim(), x.StoreIntegrationId?.Trim() ?? "", x.IsParent)).ToHashSet()
            .SetEquals(proposed.Stores.Select(x =>
                (x.RappiStoreId.Trim(), x.StoreIntegrationId?.Trim() ?? "", x.IsParent)));
        UpsertStores(connection, dto.Stores);
        if (routingChanged)
        {
            connection.IsVerified = false;
            connection.LastVerifiedAt = null;
        }

        connection.DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName)
            ? "Rappi"
            : dto.DisplayName.Trim();
        connection.FinancialAppId = dto.FinancialAppId;
        connection.CustomerId = dto.CustomerId;
        connection.TechnicalUserId = technicalUserId;
        connection.DefaultCookingTimeMinutes = Math.Clamp(dto.DefaultCookingTimeMinutes, 5, 180);
        connection.EstimatedCommissionRate = dto.EstimatedCommissionRate;
        connection.PiiRetentionDays = 90;
        connection.IsActive = dto.IsActive;
        connection.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);

        var saved = await ConnectionQuery().FirstAsync(x => x.Id == connection.Id, ct);
        return Ok(ApiResponse<object>.SuccessResponse(ToDto(saved), "Configuración Rappi guardada."));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpDelete("api/branches/{branchId:int}/integrations/apps/rappi")]
    public async Task<ActionResult<ApiResponse<string>>> Disable(int branchId, CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();
        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        if (connection is null)
            return NotFound();
        foreach (var store in connection.Stores)
        {
            var result = await rappi.SetStoreIntegratedAsync(store.RappiStoreId, false, ct);
            if (!result.Success)
                return BadRequest(ApiResponse<string>.ErrorResponse(
                    result.Error ?? $"Rappi no permitió desintegrar la tienda {store.RappiStoreId}."));
        }
        connection.IsActive = false;
        connection.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<string>.SuccessResponse(
            "Integración desactivada. Se conservaron configuración e historial."));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPost("api/branches/{branchId:int}/integrations/apps/rappi/test-connection")]
    public async Task<ActionResult<ApiResponse<object>>> TestConnection(int branchId, CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();
        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        if (connection is null)
            return NotFound();

        var result = await rappi.TestConnectionAsync(ct);
        var expectedIds = connection.Stores.Select(x => x.RappiStoreId).ToHashSet();
        var returnedIds = result.Stores?.Select(x => x.StoreId).ToHashSet() ?? [];
        var missing = expectedIds.Except(returnedIds).ToList();
        if (!result.Success || missing.Count > 0)
        {
            connection.IsVerified = false;
            connection.LastError = result.Error
                ?? $"Las credenciales no devolvieron las tiendas: {string.Join(", ", missing)}.";
            await db.SaveChangesAsync(ct);
            return BadRequest(ApiResponse<object>.ErrorResponse(connection.LastError));
        }

        foreach (var store in connection.Stores)
        {
            var remote = result.Stores!.First(x => x.StoreId == store.RappiStoreId);
            if (!string.IsNullOrWhiteSpace(remote.IntegrationId))
                store.StoreIntegrationId = remote.IntegrationId;
        }
        connection.IsVerified = true;
        connection.LastVerifiedAt = clock.UtcNow;
        connection.LastError = null;
        await db.SaveChangesAsync(ct);
        var saved = await ConnectionQuery().FirstAsync(x => x.Id == connection.Id, ct);
        return Ok(ApiResponse<object>.SuccessResponse(
            ToDto(saved),
            "Credenciales válidas y ambas tiendas verificadas."));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPost("api/branches/{branchId:int}/integrations/apps/rappi/webhooks/configure")]
    public async Task<ActionResult<ApiResponse<object>>> ConfigureWebhooks(
        int branchId,
        CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();
        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .Include(x => x.WebhookSubscriptions)
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        if (connection is null)
            return NotFound();
        if (!connection.IsVerified || !connection.IsActive)
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "Activa y verifica la conexión antes de registrar webhooks."));

        var baseUrl = apiPublicOptions.Value.BaseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "Configura ApiPublic__BaseUrl antes de registrar webhooks."));
        var storeIds = connection.Stores.Select(x => x.RappiStoreId).ToArray();
        foreach (var eventType in WebhookEvents)
        {
            var webhookUrl =
                $"{baseUrl}/api/integrations/rappi/webhooks/{connection.PublicId:D}/{eventType}";
            var subscription = connection.WebhookSubscriptions
                .FirstOrDefault(x => x.EventType == eventType);
            var remote = await rappi.GetWebhookAsync(eventType, ct);
            var remoteConfigured = remote.Success
                && remote.EnabledStoreIds is not null
                && storeIds.All(remote.EnabledStoreIds.Contains);
            var hasLocalSecret = !string.IsNullOrWhiteSpace(subscription?.EncryptedSecret);
            var result = remoteConfigured && hasLocalSecret
                ? new RappiWebhookResult(
                    true,
                    protector.Unprotect(subscription!.EncryptedSecret))
                : remoteConfigured
                    ? await rappi.ResetWebhookSecretAsync(eventType, ct)
                    : await rappi.ConfigureWebhookAsync(eventType, webhookUrl, storeIds, ct);
            if (!(remoteConfigured && hasLocalSecret) && result.Success)
                remote = await rappi.GetWebhookAsync(eventType, ct);
            var missingStores = remote.EnabledStoreIds is null
                ? storeIds
                : storeIds.Except(remote.EnabledStoreIds).ToArray();
            if (result.Success && (!remote.Success || missingStores.Length > 0))
            {
                result = new RappiWebhookResult(
                    false,
                    Error: remote.Error
                        ?? $"Rappi no confirmó {eventType} para: {string.Join(", ", missingStores)}.");
            }
            if (subscription is null)
            {
                subscription = new DeliveryAppWebhookSubscription
                {
                    EventType = eventType,
                    CreatedAt = clock.UtcNow
                };
                connection.WebhookSubscriptions.Add(subscription);
            }
            subscription.IsActive = result.Success;
            subscription.EncryptedSecret = result.Success
                ? protector.Protect(result.Secret!)
                : string.Empty;
            subscription.LastError = result.Error;
            subscription.UpdatedAt = clock.UtcNow;
            if (!result.Success)
            {
                connection.WebhookConfigured = false;
                connection.LastError = $"No se pudo registrar {eventType}: {result.Error}";
                await db.SaveChangesAsync(ct);
                return BadRequest(ApiResponse<object>.ErrorResponse(connection.LastError));
            }
        }

        connection.WebhookConfigured = true;
        connection.LastError = null;
        await db.SaveChangesAsync(ct);
        var saved = await ConnectionQuery().FirstAsync(x => x.Id == connection.Id, ct);
        return Ok(ApiResponse<object>.SuccessResponse(
            ToDto(saved),
            "Webhooks Rappi registrados para ambas tiendas."));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpGet("api/branches/{branchId:int}/integrations/apps/rappi/catalog")]
    public async Task<ActionResult<ApiResponse<object>>> GetCatalog(int branchId, CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();
        var connection = await db.DeliveryAppConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        if (connection is null)
            return NotFound();
        return Ok(ApiResponse<object>.SuccessResponse(await CatalogResponse(connection.Id, branchId, ct)));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPut("api/branches/{branchId:int}/integrations/apps/rappi/catalog/{productId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateCatalogProduct(
        int branchId,
        int productId,
        [FromBody] UpdateRappiCatalogProductDto dto,
        CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();
        var connection = await db.DeliveryAppConnections
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        var product = await db.Products
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == productId && x.Category.BranchId == branchId, ct);
        if (connection is null || product is null)
            return NotFound();
        if (dto.OverridePrice is <= 0)
            return BadRequest(ApiResponse<object>.ErrorResponse("El precio Rappi debe ser mayor que cero."));

        var mapping = await db.DeliveryAppProductMappings
            .FirstOrDefaultAsync(x => x.ConnectionId == connection.Id && x.ProductId == productId, ct);
        if (mapping is null)
        {
            mapping = new DeliveryAppProductMapping
            {
                ConnectionId = connection.Id,
                ProductId = productId,
                Sku = $"product-{productId}",
                CategorySku = $"category-{product.CategoryId}",
                CreatedAt = clock.UtcNow
            };
            db.DeliveryAppProductMappings.Add(mapping);
        }
        mapping.IsSelected = dto.IsSelected;
        mapping.OverrideName = Clean(dto.OverrideName, 300);
        mapping.OverrideDescription = Clean(dto.OverrideDescription, 1000);
        mapping.OverrideImageUrl = Clean(dto.OverrideImageUrl, 1000);
        mapping.OverridePrice = dto.OverridePrice;
        mapping.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.SuccessResponse(
            await CatalogResponse(connection.Id, branchId, ct)));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpGet("api/branches/{branchId:int}/integrations/apps/rappi/menu/preview")]
    public async Task<ActionResult<ApiResponse<object>>> PreviewMenu(int branchId, CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();
        var build = await BuildMenuAsync(branchId, ct);
        if (build.Error is not null)
            return BadRequest(ApiResponse<object>.ErrorResponse(build.Error));
        return Ok(ApiResponse<object>.SuccessResponse(build.Menu!));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPost("api/branches/{branchId:int}/integrations/apps/rappi/menu/publish")]
    public async Task<ActionResult<ApiResponse<object>>> PublishMenu(int branchId, CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();

        // Serialize reservation of the existing connection, not the remote HTTP call.
        // The tenant-filtered no-op UPDATE obtains a row lock without a new schema/lock service.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        if (transaction is not null)
            await db.DeliveryAppConnections
                .Where(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt, x => x.UpdatedAt), ct);

        var build = await BuildMenuAsync(branchId, ct);
        if (build.Error is not null)
            return BadRequest(ApiResponse<object>.ErrorResponse(build.Error));
        if (await db.RappiMenuPublications.AnyAsync(x =>
                x.ConnectionId == build.Connection!.Id
                && x.StoreId == build.Menu!.StoreId
                && (x.Status == "submitting" || x.Status == "submitted" || x.Status == "approval_review_required"), ct))
            return Conflict(ApiResponse<object>.ErrorResponse(
                "Ya hay un menú pendiente para esta tienda. Espera su aprobación o revisa el envío incierto antes de publicar otro."));

        var serialized = JsonSerializer.Serialize(build.Menu, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant();
        var publication = new RappiMenuPublication
        {
            ConnectionId = build.Connection!.Id,
            StoreId = build.Menu!.StoreId,
            PayloadHash = hash,
            PayloadJson = serialized,
            Status = "submitting",
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.RappiMenuPublications.Add(publication);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);

        var result = await rappi.PublishMenuAsync(build.Menu, ct);
        // A transport failure does not prove Rappi rejected the menu. Do not open a
        // second submission while the outcome is unknown; preserve the durable reservation.
        var uncertain = !result.Success && (result.StatusCode is null or 408 or >= 500);
        publication.Status = result.Success ? "submitted" : uncertain ? "submitting" : "failed";
        publication.Error = uncertain
            ? "Envío de menú con resultado incierto; requiere verificar en Rappi antes de volver a publicar."
            : result.Error;
        publication.UpdatedAt = clock.UtcNow;
        build.Connection.LastError = result.Success ? null : publication.Error;
        try
        {
            // Status is an EF concurrency token: a callback hold wins over this stale
            // tracked response, and the implicit SaveChanges transaction preserves its diagnostics.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(ApiResponse<object>.ErrorResponse(
                "El estado del menú cambió mientras Rappi respondía. Recarga y revisa la publicación antes de reintentar."));
        }
        if (!result.Success)
            return BadRequest(ApiResponse<object>.ErrorResponse(
                publication.Error ?? "Rappi rechazó la publicación del menú."));
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            publication.Id,
            publication.Status,
            publication.PayloadHash
        }, "Menú enviado a Rappi y pendiente de aprobación."));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPost("api/branches/{branchId:int}/integrations/apps/rappi/availability/reconcile")]
    public async Task<ActionResult<ApiResponse<object>>> ReconcileAvailability(
        int branchId,
        CancellationToken ct)
    {
        if (!CanAdminister(branchId))
            return Forbid();
        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        if (connection is null)
            return NotFound();
        var missingIds = connection.Stores
            .Where(x => string.IsNullOrWhiteSpace(x.StoreIntegrationId))
            .Select(x => x.Name)
            .ToList();
        if (missingIds.Count > 0)
            return BadRequest(ApiResponse<object>.ErrorResponse(
                $"Falta store_integration_id para: {string.Join(", ", missingIds)}."));

        await db.RappiAvailabilityStates
            .Where(x => x.ConnectionId == connection.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "pending")
                .SetProperty(x => x.NextAttemptAt, (DateTime?)null), ct);
        workSignal.Pulse();
        return Ok(ApiResponse<object>.SuccessResponse(new { queued = true },
            "Disponibilidad programada para reconciliación."));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpGet("api/integrations/apps/status")]
    public async Task<ActionResult<ApiResponse<object>>> OperationalStatus(
        [FromQuery] int? branchId,
        CancellationToken ct)
    {
        var resolved = ResolveBranch(branchId);
        if (!resolved.HasValue)
            return Forbid();
        var connection = await ConnectionQuery()
            .FirstOrDefaultAsync(x => x.BranchId == resolved && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        var pending = connection is null
            ? 0
            : await db.ExternalDeliveryOrders.CountAsync(x =>
                x.ConnectionId == connection.Id
                && (x.Status == ExternalOrderStatus.PendingAcceptance
                    || x.Status == ExternalOrderStatus.BlockedMapping
                    || x.Status == ExternalOrderStatus.SyncError
                    || x.Status == ExternalOrderStatus.ReconciliationRequired), ct);
        var readySyncFailed = connection is null
            ? 0
            : await db.IntegrationWebhookEvents.CountAsync(x =>
                x.ConnectionId == connection.Id
                && x.Provider == "rappi"
                && x.EventType == RappiReadyOutboxPolicy.EventType
                && (x.Status == RappiReadyOutboxPolicy.Failed
                    || x.Status == RappiReadyOutboxPolicy.Pending
                        && x.AttemptCount >= RappiReadyOutboxPolicy.MaxAttempts), ct);
        // Legacy workers did not timestamp their processing claim. Report these
        // rows for review, but never automatically replay or rewrite them on rollout.
        var webhookLegacyProcessing = connection is null
            ? 0
            : await db.IntegrationWebhookEvents.CountAsync(x =>
                x.ConnectionId == connection.Id && x.Provider == "rappi"
                && x.Status == "processing", ct);
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            rappi = connection is null ? null : ToDto(connection),
            pending,
            readySyncFailed,
            webhookLegacyProcessing
        }));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpGet("api/integrations/apps/rappi/orders")]
    public async Task<ActionResult<ApiResponse<object>>> GetOrders(
        [FromQuery] int? branchId,
        CancellationToken ct,
        [FromQuery] int? page = null,
        [FromQuery] int pageSize = 25)
    {
        var resolved = ResolveBranch(branchId);
        if (!resolved.HasValue)
            return Forbid();
        var pageNumber = Math.Max(1, page ?? 1);
        pageSize = page.HasValue ? Math.Clamp(pageSize, 1, 100) : 100;
        var query = db.ExternalDeliveryOrders
            .AsNoTracking()
            .Include(x => x.Store)
            .Include(x => x.Connection)
            .Include(x => x.InternalOrder)
            .Where(x => x.BranchId == resolved && x.Connection.Environment == options.Environment);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var readyKeys = rows.Select(x => RappiReadyOutboxPolicy.EventKey(x.ConnectionId, x.ExternalOrderId))
            .ToList();
        var readyEvents = await db.IntegrationWebhookEvents.AsNoTracking()
            .Where(x => x.Provider == "rappi"
                && x.EventType == RappiReadyOutboxPolicy.EventType
                && readyKeys.Contains(x.EventKey))
            .ToDictionaryAsync(x => x.EventKey, ct);
        var items = rows.Select(x => new
        {
            x.Id,
            x.ExternalOrderId,
            x.Status,
            storeName = x.Store == null ? x.ExternalStoreId : x.Store.Name,
            x.CustomerName,
            x.CustomerPhone,
            x.DeliveryAddress,
            x.DeliveryMethod,
            x.PaymentMethod,
            x.Total,
            x.TotalProducts,
            x.TotalDiscounts,
            x.TotalDiscountByPartner,
            x.TotalDiscountByRappi,
            x.TotalCharges,
            x.CookingTimeMinutes,
            lines = DeserializeLines(x.LinesJson),
            discounts = DeserializeDiscounts(x.DiscountsJson),
            validationErrors = DeserializeStrings(x.ValidationErrorsJson),
            x.InternalOrderId,
            x.LastError,
            x.AcceptedAt,
            x.PiiPurgedAt,
            x.CreatedAt,
            readyForPickup = readyEvents.TryGetValue(
                RappiReadyOutboxPolicy.EventKey(x.ConnectionId, x.ExternalOrderId), out var ready)
                ? new
                {
                    ready.Status,
                    ready.AttemptCount,
                    ready.ProcessedAt,
                    ready.UpdatedAt,
                    // Old stored errors may contain raw provider content.
                    error = string.IsNullOrWhiteSpace(ready.LastError) ? null
                        : "READY_FOR_PICKUP requiere revisión; consulta el estado y los intentos.",
                    retryable = RappiReadyOutboxPolicy.CanRequeue(ready) && RappiReadyOutboxPolicy.CanSend(x)
                        && (options.Environment == "sandbox" || options.ProductionReadyForPickupApproved)
                }
                : null
        });
        if (!page.HasValue)
            return Ok(ApiResponse<object>.SuccessResponse(items));
        return Ok(ApiResponse<object>.SuccessResponse(new PagedResult<object>
        {
            Items = items, TotalCount = total, Page = pageNumber, PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        }));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpGet("api/integrations/apps/rappi/events")]
    public async Task<ActionResult<ApiResponse<object>>> GetEvents(
        [FromQuery] int? branchId, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var resolved = ResolveBranch(branchId);
        if (!resolved.HasValue) return Forbid();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var connections = db.DeliveryAppConnections.Where(x => x.BranchId == resolved
            && x.Provider == "rappi" && x.Environment == options.Environment);
        var query = db.IntegrationWebhookEvents.AsNoTracking().Where(x =>
            connections.Any(c => c.Id == x.ConnectionId) && x.Provider == "rappi"
            && (x.Status == "failed" || x.Status == "processing" || x.Status == "awaiting_order"
                || x.Status == "accept_effects_pending" || x.Status == "cancel_effects_pending"
                || x.Status == "manual_retry_received"));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var retryableConnections = await connections.Where(x => x.IsActive && x.IsVerified)
            .Select(x => x.Id).ToHashSetAsync(ct);
        return Ok(ApiResponse<object>.SuccessResponse(new PagedResult<object>
        {
            TotalCount = total, Page = page, PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
            Items = rows.Select(x => new
            {
                x.Id, x.EventType, x.Status, x.AttemptCount, x.CreatedAt, x.UpdatedAt,
                error = string.IsNullOrWhiteSpace(x.LastError) ? null : "El evento requiere revisión de su resultado.",
                retryable = x.Status == "failed" && CanRetryWebhook(x.EventType)
                    && retryableConnections.Contains(x.ConnectionId)
            })
        }));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpPost("api/integrations/apps/rappi/events/{id:int}/retry")]
    public async Task<ActionResult<ApiResponse<object>>> RetryWebhook(int id, CancellationToken ct)
    {
        var row = await db.IntegrationWebhookEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Provider == "rappi", ct);
        if (row?.ConnectionId is not int connectionId) return NotFound();
        var connection = await db.DeliveryAppConnections.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == connectionId && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        if (connection is null) return NotFound();
        if (!CanOperate(connection.BranchId)) return Forbid();
        if (!connection.IsActive || !connection.IsVerified || row.Status != "failed" || !CanRetryWebhook(row.EventType))
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "Solo se reintentan efectos y estados fallidos de una integración activa y verificada. Menú, aceptación incierta y reclamos legados requieren revisión específica."));
        var updated = await db.IntegrationWebhookEvents.Where(x => x.Id == id && x.ConnectionId == connectionId
                && x.Status == "failed" && x.AttemptCount == row.AttemptCount && x.UpdatedAt == row.UpdatedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "manual_retry_received")
                .SetProperty(x => x.LastError, (string?)null).SetProperty(x => x.ProcessedAt, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, clock.UtcNow), ct);
        if (updated == 0) return Conflict(ApiResponse<object>.ErrorResponse("El evento cambió; actualiza antes de reintentar."));
        workSignal.Pulse();
        return Ok(ApiResponse<object>.SuccessResponse(new { queued = true }));
    }

    private static bool CanRetryWebhook(string eventType) => eventType is
        "ORDER_EVENT_CANCEL" or "ORDER_OTHER_EVENT" or "LOCAL_RAPPI_CANCEL_EFFECTS" or "LOCAL_RAPPI_ACCEPT_EFFECTS";

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpPost("api/integrations/apps/rappi/orders/{id:int}/retry-ready-for-pickup")]
    public async Task<ActionResult<ApiResponse<object>>> RetryReadyForPickup(int id, CancellationToken ct)
    {
        if (options.Environment == "production" && !options.ProductionReadyForPickupApproved)
            return BadRequest(ApiResponse<object>.ErrorResponse("Rappi aún no ha autorizado READY_FOR_PICKUP en producción."));
        var external = await db.ExternalDeliveryOrders.AsNoTracking()
            .Include(x => x.Connection)
            .Include(x => x.Store)
            .Include(x => x.InternalOrder)
            .FirstOrDefaultAsync(x => x.Id == id && x.Connection.Environment == options.Environment, ct);
        if (external is null)
            return NotFound();
        if (!CanOperate(external.BranchId))
            return Forbid();
        if (!RappiReadyOutboxPolicy.CanSend(external))
            return BadRequest(ApiResponse<object>.ErrorResponse(
                "El pedido debe estar listo, aceptado y con la integración y READY_FOR_PICKUP habilitados."));

        var eventKey = RappiReadyOutboxPolicy.EventKey(external.ConnectionId, external.ExternalOrderId);
        var row = await db.IntegrationWebhookEvents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ConnectionId == external.ConnectionId
                && x.Provider == "rappi"
                && x.EventType == RappiReadyOutboxPolicy.EventType
                && x.EventKey == eventKey, ct);
        if (row is null)
            return NotFound(ApiResponse<object>.ErrorResponse("No existe un envío READY_FOR_PICKUP para reintentar."));
        if (row.Status == "processed")
            return Ok(ApiResponse<object>.SuccessResponse(new { queued = false, alreadyProcessed = true }));
        if (row.Status == RappiReadyOutboxPolicy.Pending && row.AttemptCount < RappiReadyOutboxPolicy.MaxAttempts)
            return Ok(ApiResponse<object>.SuccessResponse(new { queued = true, alreadyQueued = true }));
        if (!RappiReadyOutboxPolicy.CanRequeue(row))
            return Conflict(ApiResponse<object>.ErrorResponse("El envío no admite reintento en su estado actual."));

        var now = clock.UtcNow;
        // Compare-and-set: repeated/concurrent clicks cannot reset an active retry.
        // The correlated eligibility check also rejects an order changed after the initial read.
        var changed = await db.IntegrationWebhookEvents
            .Where(x => x.Id == row.Id
                && x.ConnectionId == external.ConnectionId
                && x.Provider == "rappi"
                && x.EventType == RappiReadyOutboxPolicy.EventType
                && x.AttemptCount == row.AttemptCount
                && x.Status == row.Status
                && x.UpdatedAt == row.UpdatedAt
                && db.ExternalDeliveryOrders.Any(order => order.Id == id
                    && order.ConnectionId == x.ConnectionId
                    && order.Connection.Provider == "rappi"
                    && order.Connection.IsActive && order.Connection.IsVerified
                    && order.Status == ExternalOrderStatus.Accepted
                    && order.Store != null && order.Store.ManualReadyForPickupEnabled
                    && order.InternalOrder != null && order.InternalOrder.Status == OrderStatus.Ready))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RappiReadyOutboxPolicy.Pending)
                .SetProperty(x => x.AttemptCount, 0)
                .SetProperty(x => x.LastError, (string?)null)
                .SetProperty(x => x.ProcessedAt, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (changed == 0)
        {
            var alreadyQueued = await db.IntegrationWebhookEvents.AsNoTracking()
                .AnyAsync(x => x.Id == row.Id && x.Status == RappiReadyOutboxPolicy.Pending
                    && x.AttemptCount < RappiReadyOutboxPolicy.MaxAttempts, ct);
            if (alreadyQueued)
                return Ok(ApiResponse<object>.SuccessResponse(new { queued = true, alreadyQueued = true }));
            return Conflict(ApiResponse<object>.ErrorResponse("La orden o el envío cambió; recarga antes de reintentar."));
        }
        workSignal.Pulse();
        return Ok(ApiResponse<object>.SuccessResponse(new { queued = true, alreadyQueued = false },
            "READY_FOR_PICKUP programado nuevamente; aún no está confirmado por Rappi."));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpPost("api/integrations/apps/rappi/orders/{id:int}/revalidate-and-accept")]
    public async Task<ActionResult<ApiResponse<object>>> RevalidateAndAccept(
        int id,
        CancellationToken ct)
    {
        var external = await db.ExternalDeliveryOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.Connection.Environment == options.Environment, ct);
        if (external is null)
            return NotFound();
        if (!CanOperate(external.BranchId))
            return Forbid();
        var result = await orderProcessor.RevalidateAndAcceptAsync(id, currentUser.Id, ct);
        if (!result.Success)
            return BadRequest(ApiResponse<object>.ErrorResponse(
                result.Error ?? "La orden no superó la revalidación."));
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            externalOrderId = result.ExternalOrderId,
            internalOrderId = result.InternalOrderId
        }, "Orden Rappi aceptada y enviada a cocina."));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpPost("api/integrations/apps/rappi/orders/{id:int}/reject")]
    public async Task<ActionResult<ApiResponse<string>>> Reject(
        int id,
        [FromBody] RejectRappiOrderDto dto,
        CancellationToken ct)
    {
        var external = await db.ExternalDeliveryOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.Connection.Environment == options.Environment, ct);
        if (external is null)
            return NotFound();
        if (!CanOperate(external.BranchId))
            return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length > 200)
            return BadRequest(ApiResponse<string>.ErrorResponse(
                "Escribe un motivo de rechazo válido."));
        var result = await orderProcessor.RejectAsync(id, dto.Reason.Trim(), ct);
        return result.Success
            ? Ok(ApiResponse<string>.SuccessResponse("Pedido rechazado."))
            : BadRequest(ApiResponse<string>.ErrorResponse(
                result.Error ?? "No se pudo rechazar el pedido."));
    }

    [AllowAnonymous]
    [EnableRateLimiting("rappi-webhook")]
    [HttpPost("api/integrations/rappi/webhooks/{publicId:guid}/{eventType}")]
    public async Task<IActionResult> Webhook(
        Guid publicId,
        string eventType,
        CancellationToken ct)
    {
        var normalizedEvent = eventType.Trim().ToUpperInvariant();
        if (!WebhookEvents.Contains(normalizedEvent))
            return NotFound();
        DeliveryAppConnection? connection;
        using (tenantExecutionContext.BeginSystemScope())
        {
            connection = await db.DeliveryAppConnections
                .Include(x => x.Stores)
                .Include(x => x.WebhookSubscriptions)
                .FirstOrDefaultAsync(x =>
                    x.PublicId == publicId
                    && x.Provider == "rappi" && x.Environment == options.Environment
                    && x.IsActive, ct);
        }
        var subscription = connection?.WebhookSubscriptions
            .FirstOrDefault(x => x.TenantId == connection.TenantId
                && x.ConnectionId == connection.Id && x.EventType == normalizedEvent && x.IsActive);
        if (connection is null || subscription is null)
            return NotFound();
        using var tenantScope = tenantExecutionContext.BeginTenantScope(connection.TenantId);

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(ct);
        var signatureHeaders = Request.Headers["Rappi-Signature"];
        if (signatureHeaders.Count != 1 || !RappiWebhookSignature.TryValidate(
                signatureHeaders[0],
                payload,
                protector.Unprotect(subscription.EncryptedSecret),
                out var timestamp,
                allowSandboxTesterPayload: connection.Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase)))
            return Unauthorized();
        DeliveryAppStore? pingStore = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (normalizedEvent == "PING")
            {
                var root = document.RootElement;
                var storeId = root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("store_id", out var storeNode)
                    ? storeNode.ToString() : null;
                var matches = connection.Stores.Where(x => x.TenantId == connection.TenantId
                    && x.ConnectionId == connection.Id && !string.IsNullOrWhiteSpace(storeId)
                    && (x.RappiStoreId == storeId || x.StoreIntegrationId == storeId)).ToList();
                if (matches.Count != 1)
                    return BadRequest(new { status = "ERROR", description = "Tienda no configurada para esta integración." });
                pingStore = matches[0];
                // PING reports local POS readiness, not the provider's on/off flag:
                // gating on ConnectivityEnabled would prevent a healthy POS reconnecting.
                // Evaluate before duplicate detection so an old successful PING cannot
                // mask a subsequently disabled branch or suspended tenant.
                if (!await db.Branches.AnyAsync(x => x.Id == connection.BranchId && x.IsActive, ct)
                    || !await db.Tenants.AnyAsync(x => x.Id == connection.TenantId
                        && x.IsActive && x.Status == TenantStatus.Active, ct))
                    return StatusCode(StatusCodes.Status503ServiceUnavailable,
                        new { status = "ERROR", description = "Tienda no disponible." });
            }
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        var payloadHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var eventKey = $"{normalizedEvent}:{timestamp}:{payloadHash}";
        var exists = await db.IntegrationWebhookEvents.AnyAsync(x =>
            x.ConnectionId == connection.Id && x.EventKey == eventKey, ct);
        if (exists)
            return normalizedEvent == "PING"
                ? Ok(new { status = "OK", description = "Tienda prendida" })
                : Ok(new { status = "OK" });

        var storedEvent = new IntegrationWebhookEvent
        {
            ConnectionId = connection.Id,
            Provider = "rappi",
            EventKey = eventKey,
            EventType = normalizedEvent,
            PayloadHash = payloadHash,
            PayloadJson = payload,
            Status = normalizedEvent == "PING" ? "processed" : "received",
            ProcessedAt = normalizedEvent == "PING" ? clock.UtcNow : null,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.IntegrationWebhookEvents.Add(storedEvent);
        subscription.LastReceivedAt = clock.UtcNow;
        subscription.LastError = null;
        connection.LastWebhookAt = clock.UtcNow;

        if (pingStore is not null)
            pingStore.LastPingAt = clock.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
            if (normalizedEvent != "PING")
                workSignal.Pulse();
        }
        catch (DbUpdateException)
        {
            var duplicate = await db.IntegrationWebhookEvents
                .AsNoTracking()
                .AnyAsync(x =>
                    x.ConnectionId == connection.Id
                    && x.EventKey == eventKey, ct);
            if (!duplicate)
                throw;
            return normalizedEvent == "PING"
                ? Ok(new { status = "OK", description = "Tienda prendida" })
                : Ok(new { status = "OK" });
        }
        return normalizedEvent == "PING"
            ? Ok(new { status = "OK", description = "Tienda prendida" })
            : Ok(new { status = "OK" });
    }

    private IQueryable<DeliveryAppConnection> ConnectionQuery() =>
        db.DeliveryAppConnections
            .Where(x => x.Environment == options.Environment)
            .AsNoTracking()
            .Include(x => x.FinancialApp)
            .Include(x => x.Customer)
            .Include(x => x.TechnicalUser)
            .Include(x => x.Stores)
            .Include(x => x.WebhookSubscriptions)
            .Include(x => x.ProductMappings)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.CommercialProfile);

    private object ToDto(DeliveryAppConnection connection)
    {
        var requiredSubscriptions = WebhookEvents.All(eventType =>
            connection.WebhookSubscriptions.Any(x => x.EventType == eventType && x.IsActive));
        var selectedMappings = connection.ProductMappings.Where(x => x.IsSelected).ToList();
        var menuApproved = selectedMappings.Count > 0
            && selectedMappings.All(x => x.PublishedAt.HasValue);
        var catalogDirty = selectedMappings.Count == 0
            || connection.ProductMappings.Any(x => !x.IsSelected && x.PublishedAt.HasValue)
            || selectedMappings.Any(x =>
                x.PublishedName != EffectiveName(x)
                || x.PublishedDescription != EffectiveDescription(x)
                || x.PublishedImageUrl != EffectiveImageUrl(x)
                || x.PublishedPrice != EffectivePrice(x));
        var storeIdsComplete = connection.Stores.Count > 0
            && connection.Stores.All(x => !string.IsNullOrWhiteSpace(x.StoreIntegrationId));
        var ready = connection.IsActive
            && connection.IsVerified
            && requiredSubscriptions
            && menuApproved
            && storeIdsComplete
            && string.IsNullOrWhiteSpace(connection.LastError);

        return new
        {
            connection.Id,
            connection.BranchId,
            connection.Provider,
            connection.Environment,
            connection.DisplayName,
            credentialsConfigured = rappi.CredentialsConfigured,
            connection.FinancialAppId,
            financialAppName = connection.FinancialApp?.Name,
            connection.CustomerId,
            customerName = connection.Customer?.Name,
            connection.TechnicalUserId,
            technicalUserName = connection.TechnicalUser?.Name,
            connection.DefaultCookingTimeMinutes,
            connection.EstimatedCommissionRate,
            connection.IsActive,
            connection.IsVerified,
            webhookConfigured = requiredSubscriptions,
            menuApproved,
            catalogDirty,
            storeIdsComplete,
            ready,
            connection.LastVerifiedAt,
            connection.LastMenuPublishedAt,
            connection.LastAvailabilitySyncAt,
            connection.LastWebhookAt,
            connection.LastError,
            stores = connection.Stores
                .OrderByDescending(x => x.IsParent)
                .Select(x => new
                {
                    x.Id,
                    x.RappiStoreId,
                    x.StoreIntegrationId,
                    x.Name,
                    x.IsParent,
                    x.ManualReadyForPickupEnabled,
                    x.ConnectivityEnabled,
                    x.LastPingAt,
                    x.LastConnectivityAt,
                    x.LastError
                }),
            subscriptions = WebhookEvents.Select(eventType =>
            {
                var subscription = connection.WebhookSubscriptions
                    .FirstOrDefault(x => x.EventType == eventType);
                return new
                {
                    eventType,
                    active = subscription?.IsActive == true,
                    subscription?.LastReceivedAt,
                    subscription?.LastError
                };
            }),
            selectedProductCount = selectedMappings.Count,
            publishedProductCount = selectedMappings.Count(x => x.PublishedAt.HasValue),
            webhookBaseUrl =
                $"{apiPublicOptions.Value.BaseUrl?.TrimEnd('/')}/api/integrations/rappi/webhooks/{connection.PublicId:D}"
        };
    }

    private async Task<object> CatalogResponse(int connectionId, int branchId, CancellationToken ct)
    {
        var mappings = await db.DeliveryAppProductMappings
            .AsNoTracking()
            .Where(x => x.ConnectionId == connectionId)
            .ToListAsync(ct);
        var byProduct = mappings.ToDictionary(x => x.ProductId);
        var products = await db.Products
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.CommercialProfile)
            .Where(x => x.Category.BranchId == branchId)
            .OrderBy(x => x.Category.Name)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
        return new
        {
            products = products.Select(product =>
            {
                byProduct.TryGetValue(product.Id, out var mapping);
                return new
                {
                    product.Id,
                    product.Name,
                    product.Price,
                    product.Active,
                    product.Stock,
                    categoryName = product.Category.Name,
                    sku = mapping?.Sku ?? $"product-{product.Id}",
                    categorySku = mapping?.CategorySku ?? $"category-{product.CategoryId}",
                    isSelected = mapping?.IsSelected == true,
                    mapping?.OverrideName,
                    mapping?.OverrideDescription,
                    mapping?.OverrideImageUrl,
                    mapping?.OverridePrice,
                    mapping?.PublishedName,
                    mapping?.PublishedDescription,
                    mapping?.PublishedImageUrl,
                    mapping?.PublishedPrice,
                    mapping?.PublishedAt,
                    effectiveName = mapping?.OverrideName ?? product.Name,
                    effectiveDescription = mapping?.OverrideDescription
                        ?? product.CommercialProfile?.Description,
                    effectiveImageUrl = mapping?.OverrideImageUrl
                        ?? product.CommercialProfile?.PhotoUrl,
                    effectivePrice = mapping?.OverridePrice ?? product.Price
                };
            }),
            selectedCount = mappings.Count(x => x.IsSelected),
            publishedCount = mappings.Count(x => x.IsSelected && x.PublishedAt.HasValue)
        };
    }

    private async Task<MenuBuildResult> BuildMenuAsync(int branchId, CancellationToken ct)
    {
        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .Include(x => x.ProductMappings)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.Category)
            .Include(x => x.ProductMappings)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.CommercialProfile)
            .FirstOrDefaultAsync(x =>
                x.BranchId == branchId
                && x.Provider == "rappi" && x.Environment == options.Environment, ct);
        if (connection is null)
            return new(null, null, "La integración Rappi no existe.");
        if (!connection.IsVerified || !connection.IsActive)
            return new(connection, null, "Activa y verifica la conexión antes de publicar.");
        var parents = connection.Stores.Where(x => x.IsParent).ToList();
        if (parents.Count != 1)
            return new(connection, null, "Debe existir exactamente una tienda padre.");
        var parent = parents[0];
        var selected = connection.ProductMappings
            .Where(x => x.IsSelected)
            .OrderBy(x => x.Product.Category.Name)
            .ThenBy(x => x.Product.Name)
            .ToList();
        if (selected.Count == 0)
            return new(connection, null, "Selecciona al menos un producto para Rappi.");

        var categoryPositions = selected
            .Select(x => x.Product.Category)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Name)
            .Select((category, index) => new { category.Id, Position = index })
            .ToDictionary(x => x.Id, x => x.Position);
        var itemPositions = new Dictionary<int, int>();
        var items = new List<RappiMenuItem>();
        foreach (var mapping in selected)
        {
            var name = EffectiveName(mapping);
            var price = EffectivePrice(mapping);
            var description = EffectiveDescription(mapping);
            var categoryName = mapping.Product.Category.Name;
            var imageUrl = EffectiveImageUrl(mapping);
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2 || price <= 0)
                return new(connection, null,
                    $"El producto {mapping.Product.Name} tiene nombre o precio inválido.");
            if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < 2)
                return new(connection, null,
                    $"El producto {mapping.Product.Name} requiere descripción para Rappi.");
            if (string.IsNullOrWhiteSpace(categoryName) || categoryName.Trim().Length < 2)
                return new(connection, null,
                    $"El producto {mapping.Product.Name} requiere una categoría válida para Rappi.");
            if (!IsValidHttpUrl(imageUrl))
                return new(connection, null,
                    $"El producto {mapping.Product.Name} tiene una URL de imagen inválida.");
            var position = itemPositions.TryGetValue(mapping.Product.CategoryId, out var current)
                ? current + 1
                : 0;
            itemPositions[mapping.Product.CategoryId] = position;
            items.Add(new RappiMenuItem(
                new RappiMenuCategory(
                    mapping.CategorySku,
                    0,
                    0,
                    categoryName,
                    categoryPositions[mapping.Product.CategoryId]),
                [],
                name,
                description,
                price,
                mapping.Sku,
                position,
                "PRODUCT",
                imageUrl));
        }
        return new(connection, new RappiMenuRequest(parent.RappiStoreId, items), null);
    }

    private static string? UpsertStores(
        DeliveryAppConnection connection,
        IReadOnlyCollection<UpsertRappiStoreDto>? input)
    {
        // Omitted configuration keeps persisted stores; never replace historical FK targets
        // or silently append the sandbox defaults to a previously configured connection.
        var existing = connection.Stores.ToList();
        if (connection.Environment != "sandbox" && existing.Count == 0 && input is not { Count: > 0 })
            return "Configura las tiendas reales entregadas por Rappi para este ambiente.";
        if (existing.Any(x => string.IsNullOrWhiteSpace(x.RappiStoreId) || x.RappiStoreId.Trim().Length > 120)
            || existing.Select(x => x.RappiStoreId.Trim()).Distinct(StringComparer.Ordinal).Count() != existing.Count)
            return "La configuración contiene identificadores de tienda inválidos o duplicados; requiere revisión antes de editar.";
        IReadOnlyCollection<UpsertRappiStoreDto> requested = input is { Count: > 0 }
            ? input
            : existing.Count > 0
                ? existing.Select(x => new UpsertRappiStoreDto
                {
                    RappiStoreId = x.RappiStoreId, StoreIntegrationId = x.StoreIntegrationId,
                    Name = x.Name, IsParent = x.IsParent,
                    ManualReadyForPickupEnabled = x.ManualReadyForPickupEnabled
                }).ToList()
                :
            [
                new UpsertRappiStoreDto
                {
                    RappiStoreId = "900173116",
                    StoreIntegrationId = "900173116",
                    Name = "Señor Arroz Dev1",
                    IsParent = true
                },
                new UpsertRappiStoreDto
                {
                    RappiStoreId = "900173117",
                    StoreIntegrationId = "900173117",
                    Name = "Señor Arroz Dev2",
                    IsParent = false
                }
            ];
        if (requested.Count(x => x.IsParent) != 1)
            return "Debe existir exactamente una tienda padre.";
        var normalized = requested.Select(x => new { Dto = x, Id = x.RappiStoreId?.Trim() ?? "" }).ToList();
        if (normalized.Any(x => x.Id.Length is 0 or > 120 || (x.Dto.StoreIntegrationId?.Trim().Length ?? 0) > 120))
            return "Cada tienda requiere identificadores Rappi válidos de hasta 120 caracteres.";
        if (normalized.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != normalized.Count)
            return "No se puede repetir el identificador de una tienda Rappi.";
        var finalParents = existing.ToDictionary(x => x.RappiStoreId.Trim(), x => x.IsParent, StringComparer.Ordinal);
        var finalIntegrationIds = existing.ToDictionary(x => x.RappiStoreId.Trim(), x => x.StoreIntegrationId?.Trim(), StringComparer.Ordinal);
        foreach (var item in normalized)
        {
            finalParents[item.Id] = item.Dto.IsParent;
            finalIntegrationIds[item.Id] = item.Dto.StoreIntegrationId?.Trim();
        }
        var nonblankIntegrationIds = finalIntegrationIds.Values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (nonblankIntegrationIds.Distinct(StringComparer.Ordinal).Count() != nonblankIntegrationIds.Count
            || finalIntegrationIds.Any(x => !string.IsNullOrWhiteSpace(x.Value)
                && x.Value != x.Key && finalIntegrationIds.ContainsKey(x.Value)))
            return "Los identificadores de integración deben resolver una sola tienda; hay identificadores duplicados o ambiguos.";
        if (finalParents.Count(x => x.Value) != 1)
            return "El cambio dejaría varias tiendas padre. Incluye explícitamente la tienda anterior como hija; no se eliminan tiendas históricas.";

        // Validate the merged final collection completely before mutating tracked entities.
        foreach (var item in normalized)
        {
            var dto = item.Dto;
            var store = existing.FirstOrDefault(x => string.Equals(x.RappiStoreId.Trim(), item.Id, StringComparison.Ordinal));
            if (store is null)
            {
                store = new DeliveryAppStore { RappiStoreId = item.Id, CreatedAt = DateTime.UtcNow };
                connection.Stores.Add(store);
            }
            store.RappiStoreId = item.Id;
            store.Name = dto.Name.Trim();
            store.StoreIntegrationId = Clean(dto.StoreIntegrationId, 120);
            store.IsParent = dto.IsParent;
            store.ManualReadyForPickupEnabled = dto.ManualReadyForPickupEnabled;
            store.UpdatedAt = DateTime.UtcNow;
        }
        return null;
    }

    private bool CanAdminister(int branchId) =>
        Roles.IsSuperadmin(currentUser.Role)
        || (Roles.IsAdmin(currentUser.Role) && currentUser.BranchId == branchId);

    private bool CanOperate(int branchId) =>
        Roles.IsSuperadmin(currentUser.Role)
        || (Roles.IsAdminOrCashier(currentUser.Role) && currentUser.BranchId == branchId);

    private int? ResolveBranch(int? requested) =>
        Roles.IsSuperadmin(currentUser.Role) ? requested : currentUser.BranchId;

    private static string EffectiveName(DeliveryAppProductMapping mapping) =>
        mapping.OverrideName ?? mapping.Product.Name;

    private static int EffectivePrice(DeliveryAppProductMapping mapping) =>
        mapping.OverridePrice ?? mapping.Product.Price;

    private static string? EffectiveDescription(DeliveryAppProductMapping mapping) =>
        mapping.OverrideDescription ?? mapping.Product.CommercialProfile?.Description;

    private static string? EffectiveImageUrl(DeliveryAppProductMapping mapping) =>
        mapping.OverrideImageUrl ?? mapping.Product.CommercialProfile?.PhotoUrl;

    private static bool IsValidHttpUrl(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return true;
        return !value.Any(char.IsWhiteSpace)
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static IReadOnlyList<ExternalDeliveryOrderLine> DeserializeLines(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ExternalDeliveryOrderLine>>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ExternalDeliveryDiscount> DeserializeDiscounts(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ExternalDeliveryDiscount>>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> DeserializeStrings(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private record MenuBuildResult(
        DeliveryAppConnection? Connection,
        RappiMenuRequest? Menu,
        string? Error);
}

public sealed class UpsertRappiConnectionDto
{
    public string DisplayName { get; set; } = "Rappi";
    public int FinancialAppId { get; set; }
    public int CustomerId { get; set; }
    public int? TechnicalUserId { get; set; }
    public int DefaultCookingTimeMinutes { get; set; } = 30;
    public decimal EstimatedCommissionRate { get; set; } = 0.25m;
    public bool IsActive { get; set; }
    public List<UpsertRappiStoreDto> Stores { get; set; } = [];
}

public sealed class UpsertRappiStoreDto
{
    public string RappiStoreId { get; set; } = string.Empty;
    public string? StoreIntegrationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsParent { get; set; }
    public bool ManualReadyForPickupEnabled { get; set; }
}

public sealed class UpdateRappiCatalogProductDto
{
    public bool IsSelected { get; set; }
    public string? OverrideName { get; set; }
    public string? OverrideDescription { get; set; }
    public string? OverrideImageUrl { get; set; }
    public int? OverridePrice { get; set; }
}

public sealed class RejectRappiOrderDto
{
    public string Reason { get; set; } = "The order has invalid items";
}
