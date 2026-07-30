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
    IOptions<ApiPublicOptions> apiPublicOptions) : ControllerBase
{
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
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            providers = new object[]
            {
                new
                {
                    key = "rappi",
                    name = "Rappi",
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
        if (!await db.Branches.AnyAsync(x => x.Id == branchId, ct))
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
        if (dto.Stores.Count > 0 && dto.Stores.Count(x => x.IsParent) != 1)
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

        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
        if (connection is null)
        {
            connection = new DeliveryAppConnection
            {
                BranchId = branchId,
                Provider = "rappi",
                Environment = "sandbox",
                PublicId = Guid.NewGuid(),
                CreatedAt = clock.UtcNow
            };
            db.DeliveryAppConnections.Add(connection);
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
        UpsertStores(connection, dto.Stores);
        await db.SaveChangesAsync(ct);

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
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
        if (connection is null)
            return NotFound();
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
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
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
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
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
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
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
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
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
        var build = await BuildMenuAsync(branchId, ct);
        if (build.Error is not null)
            return BadRequest(ApiResponse<object>.ErrorResponse(build.Error));

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
        var result = await rappi.PublishMenuAsync(build.Menu, ct);
        publication.Status = result.Success ? "submitted" : "failed";
        publication.Error = result.Error;
        build.Connection.LastError = result.Success ? null : result.Error;
        await db.SaveChangesAsync(ct);
        if (!result.Success)
            return BadRequest(ApiResponse<object>.ErrorResponse(
                result.Error ?? "Rappi rechazó la publicación del menú."));
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
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
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
            .FirstOrDefaultAsync(x => x.BranchId == resolved && x.Provider == "rappi", ct);
        var pending = connection is null
            ? 0
            : await db.ExternalDeliveryOrders.CountAsync(x =>
                x.ConnectionId == connection.Id
                && (x.Status == ExternalOrderStatus.PendingAcceptance
                    || x.Status == ExternalOrderStatus.BlockedMapping
                    || x.Status == ExternalOrderStatus.SyncError
                    || x.Status == ExternalOrderStatus.ReconciliationRequired), ct);
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            rappi = connection is null ? null : ToDto(connection),
            pending
        }));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpGet("api/integrations/apps/rappi/orders")]
    public async Task<ActionResult<ApiResponse<object>>> GetOrders(
        [FromQuery] int? branchId,
        CancellationToken ct)
    {
        var resolved = ResolveBranch(branchId);
        if (!resolved.HasValue)
            return Forbid();
        var rows = await db.ExternalDeliveryOrders
            .AsNoTracking()
            .Include(x => x.Store)
            .Where(x => x.BranchId == resolved)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
        return Ok(ApiResponse<object>.SuccessResponse(rows.Select(x => new
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
            x.CreatedAt
        })));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpPost("api/integrations/apps/rappi/orders/{id:int}/revalidate-and-accept")]
    public async Task<ActionResult<ApiResponse<object>>> RevalidateAndAccept(
        int id,
        CancellationToken ct)
    {
        var external = await db.ExternalDeliveryOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
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
            .FirstOrDefaultAsync(x => x.Id == id, ct);
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
        var connection = await db.DeliveryAppConnections
            .Include(x => x.Stores)
            .Include(x => x.WebhookSubscriptions)
            .FirstOrDefaultAsync(x =>
                x.PublicId == publicId
                && x.Provider == "rappi"
                && x.IsActive, ct);
        var subscription = connection?.WebhookSubscriptions
            .FirstOrDefault(x => x.EventType == normalizedEvent && x.IsActive);
        if (connection is null || subscription is null)
            return NotFound();

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(ct);
        var signatureHeader = Request.Headers["Rappi-Signature"].FirstOrDefault();
        if (!RappiWebhookSignature.IsValid(
                signatureHeader,
                payload,
                protector.Unprotect(subscription.EncryptedSecret)))
            return Unauthorized();
        try
        {
            using var _ = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        var payloadHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var timestamp = RappiWebhookSignature.GetTimestamp(signatureHeader) ?? "unknown";
        var eventKey = $"{normalizedEvent}:{timestamp}:{payloadHash}";
        var exists = await db.IntegrationWebhookEvents.AnyAsync(x =>
            x.ConnectionId == connection.Id && x.EventKey == eventKey, ct);
        if (exists)
            return normalizedEvent == "PING"
                ? Ok(new { status = "OK", description = "Tienda prendida" })
                : Accepted();

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

        if (normalizedEvent == "PING")
        {
            using var document = JsonDocument.Parse(payload);
            var storeId = document.RootElement.TryGetProperty("store_id", out var storeNode)
                ? storeNode.ToString()
                : null;
            var store = connection.Stores.FirstOrDefault(x =>
                x.RappiStoreId == storeId || x.StoreIntegrationId == storeId);
            if (store is not null)
                store.LastPingAt = clock.UtcNow;
        }
        try
        {
            await db.SaveChangesAsync(ct);
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
                : Accepted();
        }
        return normalizedEvent == "PING"
            ? Ok(new { status = "OK", description = "Tienda prendida" })
            : Accepted();
    }

    private IQueryable<DeliveryAppConnection> ConnectionQuery() =>
        db.DeliveryAppConnections
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
                && x.Provider == "rappi", ct);
        if (connection is null)
            return new(null, null, "La integración Rappi no existe.");
        if (!connection.IsVerified || !connection.IsActive)
            return new(connection, null, "Activa y verifica la conexión antes de publicar.");
        var parent = connection.Stores.SingleOrDefault(x => x.IsParent);
        if (parent is null)
            return new(connection, null, "Debe existir exactamente una tienda padre.");
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
            if (string.IsNullOrWhiteSpace(name) || price <= 0)
                return new(connection, null,
                    $"El producto {mapping.Product.Name} tiene nombre o precio inválido.");
            if (string.IsNullOrWhiteSpace(description))
                return new(connection, null,
                    $"El producto {mapping.Product.Name} requiere descripción para Rappi.");
            var position = itemPositions.TryGetValue(mapping.Product.CategoryId, out var current)
                ? current + 1
                : 0;
            itemPositions[mapping.Product.CategoryId] = position;
            items.Add(new RappiMenuItem(
                new RappiMenuCategory(
                    mapping.CategorySku,
                    0,
                    0,
                    mapping.Product.Category.Name,
                    categoryPositions[mapping.Product.CategoryId]),
                [],
                name,
                description,
                price,
                mapping.Sku,
                position,
                "PRODUCT",
                mapping.OverrideImageUrl ?? mapping.Product.CommercialProfile?.PhotoUrl));
        }
        return new(connection, new RappiMenuRequest(parent.RappiStoreId, items), null);
    }

    private static void UpsertStores(
        DeliveryAppConnection connection,
        IReadOnlyCollection<UpsertRappiStoreDto>? input)
    {
        var requested = input is { Count: > 0 }
            ? input
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
            throw new ArgumentException("Debe existir exactamente una tienda padre.");
        foreach (var dto in requested)
        {
            var store = connection.Stores.FirstOrDefault(x => x.RappiStoreId == dto.RappiStoreId);
            if (store is null)
            {
                store = new DeliveryAppStore
                {
                    RappiStoreId = dto.RappiStoreId.Trim(),
                    CreatedAt = DateTime.UtcNow
                };
                connection.Stores.Add(store);
            }
            store.Name = dto.Name.Trim();
            store.StoreIntegrationId = Clean(dto.StoreIntegrationId, 120);
            store.IsParent = dto.IsParent;
            store.ManualReadyForPickupEnabled = dto.ManualReadyForPickupEnabled;
            store.UpdatedAt = DateTime.UtcNow;
        }
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
