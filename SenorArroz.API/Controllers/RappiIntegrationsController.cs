using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
public class RappiIntegrationsController : ControllerBase
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IRappiDeliveryProvider _rappi;
    private readonly IOrderRepository _orders;
    private readonly IMapper _mapper;
    private readonly IOrderNotificationService _notifications;

    public RappiIntegrationsController(IApplicationDbContext db, ICurrentUser currentUser, IClock clock,
        IIntegrationSecretProtector protector, IRappiDeliveryProvider rappi, IOrderRepository orders,
        IMapper mapper, IOrderNotificationService notifications)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _protector = protector; _rappi = rappi;
        _orders = orders; _mapper = mapper; _notifications = notifications;
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpGet("api/branches/{branchId:int}/integrations/apps")]
    public async Task<ActionResult<ApiResponse<object>>> GetApps(int branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var connection = await ConnectionQuery().FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
        var data = new
        {
            providers = new object[]
            {
                new { key = "rappi", name = "Rappi", available = true, connection = connection is null ? null : ToDto(connection) },
                new { key = "didi_food", name = "DiDi Food", available = false, connection = (object?)null }
            }
        };
        return Ok(ApiResponse<object>.SuccessResponse(data));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPut("api/branches/{branchId:int}/integrations/apps/rappi")]
    public async Task<ActionResult<ApiResponse<object>>> Upsert(int branchId, [FromBody] UpsertRappiConnectionDto dto, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        if (!await _db.Branches.AnyAsync(x => x.Id == branchId, ct)) return NotFound();
        var appValid = await _db.Apps.AsNoTracking().AnyAsync(x => x.Id == dto.FinancialAppId && x.Bank.BranchId == branchId && x.Active, ct);
        if (!appValid) return BadRequest(ApiResponse<object>.ErrorResponse("Selecciona una App financiera activa de esta sucursal."));
        if (dto.Environment is not ("sandbox" or "production" or "simulator")) return BadRequest(ApiResponse<object>.ErrorResponse("Ambiente inválido."));
        if (string.IsNullOrWhiteSpace(dto.ClientId) || string.IsNullOrWhiteSpace(dto.ExternalStoreId)) return BadRequest(ApiResponse<object>.ErrorResponse("Client ID y Store ID son obligatorios."));

        var entity = await _db.DeliveryAppConnections.FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
        if (entity is null)
        {
            if (string.IsNullOrWhiteSpace(dto.ClientSecret)) return BadRequest(ApiResponse<object>.ErrorResponse("Client Secret es obligatorio al crear Rappi."));
            entity = new DeliveryAppConnection { BranchId = branchId, Provider = "rappi", CreatedAt = _clock.UtcNow };
            _db.DeliveryAppConnections.Add(entity);
        }
        var criticalChange = entity.ClientId != dto.ClientId.Trim() || entity.ExternalStoreId != dto.ExternalStoreId.Trim()
            || entity.Environment != dto.Environment || !string.IsNullOrWhiteSpace(dto.ClientSecret);
        entity.DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? "Rappi" : dto.DisplayName.Trim();
        entity.Environment = dto.Environment; entity.ClientId = dto.ClientId.Trim(); entity.ExternalStoreId = dto.ExternalStoreId.Trim();
        entity.FinancialAppId = dto.FinancialAppId; entity.DefaultCookingTimeMinutes = Math.Clamp(dto.DefaultCookingTimeMinutes, 5, 180);
        entity.IsActive = dto.IsActive; entity.UpdatedAt = _clock.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.ClientSecret)) entity.EncryptedClientSecret = _protector.Protect(dto.ClientSecret.Trim());
        if (criticalChange) { entity.IsVerified = false; entity.WebhookConfigured = false; entity.LastError = null; }
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.SuccessResponse(ToDto(await ConnectionQuery().SingleAsync(x => x.Id == entity.Id, ct)), "Configuración Rappi guardada."));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpDelete("api/branches/{branchId:int}/integrations/apps/rappi")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(int branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var entity = await _db.DeliveryAppConnections.FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
        if (entity is null) return NotFound();
        if (await _db.ExternalDeliveryOrders.AnyAsync(x => x.ConnectionId == entity.Id && x.InternalOrderId != null, ct))
            return BadRequest(ApiResponse<string>.ErrorResponse("Rappi ya tiene pedidos vinculados. Desactívalo en lugar de eliminarlo."));
        _db.DeliveryAppConnections.Remove(entity); await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<string>.SuccessResponse("Rappi eliminado."));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPost("api/branches/{branchId:int}/integrations/apps/rappi/test-connection")]
    public async Task<ActionResult<ApiResponse<object>>> TestConnection(int branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var entity = await _db.DeliveryAppConnections.FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
        if (entity is null) return NotFound();
        var secret = _protector.Unprotect(entity.EncryptedClientSecret);
        var result = await _rappi.TestConnectionAsync(entity, secret, ct);
        if (result.Success)
        {
            var webhookUrl = $"{Request.Scheme}://{Request.Host}/api/integrations/rappi/webhook/{entity.Id}";
            var webhook = await _rappi.ConfigureWebhookAsync(entity, secret, webhookUrl, ct);
            entity.WebhookConfigured = webhook.Success;
            if (webhook.Success && !string.IsNullOrWhiteSpace(webhook.Secret)) entity.EncryptedWebhookSecret = _protector.Protect(webhook.Secret);
            entity.IsVerified = webhook.Success; entity.LastVerifiedAt = webhook.Success ? _clock.UtcNow : null; entity.LastError = webhook.Error;
        }
        else { entity.IsVerified = false; entity.LastError = result.Error; }
        await _db.SaveChangesAsync(ct);
        if (!entity.IsVerified) return BadRequest(ApiResponse<object>.ErrorResponse(entity.LastError ?? "No se pudo verificar Rappi."));
        return Ok(ApiResponse<object>.SuccessResponse(ToDto(await ConnectionQuery().SingleAsync(x => x.Id == entity.Id, ct)), "Rappi conectado y webhook configurado."));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPost("api/branches/{branchId:int}/integrations/apps/rappi/sync-catalog")]
    public async Task<ActionResult<ApiResponse<object>>> SyncCatalog(int branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var entity = await _db.DeliveryAppConnections.Include(x => x.ProductMappings).FirstOrDefaultAsync(x => x.BranchId == branchId && x.Provider == "rappi", ct);
        if (entity is null) return NotFound();
        try
        {
            var catalog = await _rappi.GetCatalogAsync(entity, _protector.Unprotect(entity.EncryptedClientSecret), ct);
            foreach (var item in catalog)
            {
                var existing = entity.ProductMappings.FirstOrDefault(x => x.ExternalProductId == item.ExternalProductId && x.ItemType == item.ItemType);
                if (existing is null) entity.ProductMappings.Add(new DeliveryAppProductMapping { ExternalProductId = item.ExternalProductId, CreatedAt = _clock.UtcNow, ItemType = item.ItemType });
                existing ??= entity.ProductMappings.Last();
                existing.ExternalSku = item.Sku; existing.ExternalName = item.Name; existing.IsActive = item.IsActive; existing.UpdatedAt = _clock.UtcNow;
            }
            entity.LastCatalogSyncAt = _clock.UtcNow; entity.LastError = null; await _db.SaveChangesAsync(ct);
            return Ok(ApiResponse<object>.SuccessResponse(await MappingResponse(entity.Id, branchId, ct), $"Catálogo sincronizado: {catalog.Count} elementos."));
        }
        catch (Exception ex) { entity.LastError = ex.Message; await _db.SaveChangesAsync(ct); return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message)); }
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpGet("api/branches/{branchId:int}/integrations/apps/rappi/mappings")]
    public async Task<ActionResult<ApiResponse<object>>> GetMappings(int branchId, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var id = await _db.DeliveryAppConnections.Where(x => x.BranchId == branchId && x.Provider == "rappi").Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (id is null) return NotFound();
        return Ok(ApiResponse<object>.SuccessResponse(await MappingResponse(id.Value, branchId, ct)));
    }

    [Authorize(Roles = "Superadmin, Admin")]
    [HttpPut("api/branches/{branchId:int}/integrations/apps/rappi/mappings/{mappingId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> MapProduct(int branchId, int mappingId, [FromBody] MapProductDto dto, CancellationToken ct)
    {
        if (!CanAccess(branchId)) return Forbid();
        var mapping = await _db.DeliveryAppProductMappings.Include(x => x.Connection).FirstOrDefaultAsync(x => x.Id == mappingId && x.Connection.BranchId == branchId, ct);
        if (mapping is null) return NotFound();
        if (!await _db.Products.AnyAsync(x => x.Id == dto.ProductId && x.Category.BranchId == branchId && x.Active, ct)) return BadRequest(ApiResponse<object>.ErrorResponse("Producto interno inválido."));
        mapping.ProductId = dto.ProductId; mapping.UpdatedAt = _clock.UtcNow; await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.SuccessResponse(await MappingResponse(mapping.ConnectionId, branchId, ct)));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpGet("api/integrations/apps/status")]
    public async Task<ActionResult<ApiResponse<object>>> OperationalStatus([FromQuery] int? branchId, CancellationToken ct)
    {
        var resolved = ResolveBranch(branchId); if (resolved is null) return Forbid();
        var connection = await ConnectionQuery().FirstOrDefaultAsync(x => x.BranchId == resolved && x.Provider == "rappi", ct);
        var pending = connection is null ? 0 : await _db.ExternalDeliveryOrders.CountAsync(x => x.ConnectionId == connection.Id && (x.Status == ExternalOrderStatus.PendingAcceptance || x.Status == ExternalOrderStatus.BlockedMapping), ct);
        return Ok(ApiResponse<object>.SuccessResponse(new { rappi = connection is null ? null : ToDto(connection), pending }));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpGet("api/integrations/apps/rappi/orders")]
    public async Task<ActionResult<ApiResponse<object>>> GetOrders([FromQuery] int? branchId, CancellationToken ct)
    {
        var resolved = ResolveBranch(branchId); if (resolved is null) return Forbid();
        var rows = await _db.ExternalDeliveryOrders.AsNoTracking().Where(x => x.BranchId == resolved)
            .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
        return Ok(ApiResponse<object>.SuccessResponse(rows.Select(x => new { x.Id, x.ExternalOrderId, x.Status, x.CustomerName, x.CustomerPhone, x.DeliveryAddress, x.DeliveryMethod, x.PaymentMethod, x.Total, x.CookingTimeMinutes, lines = JsonSerializer.Deserialize<List<ExternalDeliveryOrderLine>>(x.LinesJson) ?? [], x.InternalOrderId, x.LastError, x.CreatedAt })));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpPost("api/integrations/apps/rappi/orders/{id:int}/accept")]
    public async Task<ActionResult<ApiResponse<object>>> Accept(int id, CancellationToken ct)
    {
        var external = await _db.ExternalDeliveryOrders.Include(x => x.Connection).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (external is null) return NotFound(); if (!CanOperate(external.BranchId)) return Forbid();
        if (external.InternalOrderId.HasValue) return Ok(ApiResponse<object>.SuccessResponse(new { external.InternalOrderId }, "El pedido ya fue aceptado."));
        var lines = JsonSerializer.Deserialize<List<ExternalDeliveryOrderLine>>(external.LinesJson) ?? [];
        var mappingRows = await _db.DeliveryAppProductMappings.Where(x => x.ConnectionId == external.ConnectionId && x.ProductId != null).ToListAsync(ct);
        var mappings = mappingRows
            .GroupBy(x => MappingKey(x.ItemType, x.ExternalProductId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var missing = lines.Where(x => !mappings.ContainsKey(MappingKey(x.ItemType, x.ExternalProductId))).Select(x => x.Name).Distinct().ToList();
        if (missing.Count > 0) { external.Status = ExternalOrderStatus.BlockedMapping; external.LastError = $"Productos sin mapear: {string.Join(", ", missing)}"; await _db.SaveChangesAsync(ct); return BadRequest(ApiResponse<object>.ErrorResponse(external.LastError)); }
        external.Status = ExternalOrderStatus.Processing; external.AcceptedByUserId = _currentUser.Id; await _db.SaveChangesAsync(ct);
        var result = await _rappi.AcceptOrderAsync(external.Connection, _protector.Unprotect(external.Connection.EncryptedClientSecret), external.ExternalOrderId, external.CookingTimeMinutes, ct);
        if (!result.Success) { external.Status = ExternalOrderStatus.SyncError; external.LastError = result.Error; await _db.SaveChangesAsync(ct); return BadRequest(ApiResponse<object>.ErrorResponse(result.Error ?? "Rappi rechazó la operación.")); }

        var order = new Order { BranchId = external.BranchId, TakenById = _currentUser.Id, GuestName = external.CustomerName, Type = OrderType.Delivery, Status = OrderStatus.Taken,
            Notes = $"Rappi #{external.ExternalOrderId}", DeliveryAppConnectionId = external.ConnectionId, ExternalOrderId = external.ExternalOrderId, OrderSource = "rappi", ExternalFulfillmentProvider = "rappi", CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow };
        foreach (var line in lines) order.OrderDetails.Add(new OrderDetail { ProductId = mappings[MappingKey(line.ItemType, line.ExternalProductId)].ProductId!.Value, Quantity = line.Quantity, UnitPrice = line.UnitPrice, Discount = 0, Notes = line.Notes });
        OrderTotalsHelper.RecalculateFromOrderDetails(order); order.AddStatusTime(OrderStatus.Taken, _clock.UtcNow);
        var created = await _orders.CreateAsync(order, ct);
        _db.AppPayments.Add(new AppPayment { OrderId = created.Id, AppId = external.Connection.FinancialAppId, Amount = external.Total > 0 ? external.Total : created.Total, IsSetted = false });
        external.InternalOrderId = created.Id; external.Status = ExternalOrderStatus.Accepted; external.AcceptedAt = _clock.UtcNow; external.LastError = null; await _db.SaveChangesAsync(ct);
        var full = await _orders.GetByIdWithFullDetailsAsync(created.Id, ct);
        if (full is not null) await _notifications.NotifyNewOrderToKitchen(_mapper.Map<SenorArroz.Application.Features.Orders.DTOs.OrderDto>(full));
        return Ok(ApiResponse<object>.SuccessResponse(new { internalOrderId = created.Id }, "Pedido Rappi aceptado y enviado a cocina."));
    }

    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    [HttpPost("api/integrations/apps/rappi/orders/{id:int}/reject")]
    public async Task<ActionResult<ApiResponse<string>>> Reject(int id, CancellationToken ct)
    {
        var external = await _db.ExternalDeliveryOrders.Include(x => x.Connection).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (external is null) return NotFound(); if (!CanOperate(external.BranchId)) return Forbid();
        var result = await _rappi.RejectOrderAsync(external.Connection, _protector.Unprotect(external.Connection.EncryptedClientSecret), external.ExternalOrderId, ct);
        external.Status = result.Success ? ExternalOrderStatus.Rejected : ExternalOrderStatus.SyncError; external.LastError = result.Error; await _db.SaveChangesAsync(ct);
        return result.Success ? Ok(ApiResponse<string>.SuccessResponse("Pedido rechazado.")) : BadRequest(ApiResponse<string>.ErrorResponse(result.Error ?? "No se pudo rechazar."));
    }

    [AllowAnonymous]
    [HttpPost("api/integrations/rappi/webhook/{connectionId:int}")]
    public async Task<IActionResult> Webhook(int connectionId, CancellationToken ct)
    {
        var connection = await _db.DeliveryAppConnections.FirstOrDefaultAsync(x => x.Id == connectionId && x.Provider == "rappi" && x.IsActive, ct);
        if (connection is null) return NotFound();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8); var payload = await reader.ReadToEndAsync(ct);
        if (!ValidateSignature(connection, payload)) return Unauthorized();
        try
        {
            using var doc = JsonDocument.Parse(payload); var root = doc.RootElement;
            var detail = root.TryGetProperty("order_detail", out var od) ? od : root;
            var externalId = Value(detail, "order_id") ?? Value(root, "order_id") ?? throw new InvalidOperationException("order_id ausente");
            var eventType = Value(root, "event") ?? Value(root, "event_type") ?? "NEW_ORDER";
            var eventKey = Value(root, "event_id") ?? $"{eventType}:{externalId}";
            if (await _db.IntegrationWebhookEvents.AnyAsync(x => x.ConnectionId == connectionId && x.EventKey == eventKey, ct)) return Ok();
            var lines = ParseLines(detail);
            var mappedItems = await _db.DeliveryAppProductMappings.Where(x => x.ConnectionId == connectionId && x.ProductId != null)
                .Select(x => new { x.ItemType, x.ExternalProductId }).ToListAsync(ct);
            var mappedKeys = mappedItems.Select(x => MappingKey(x.ItemType, x.ExternalProductId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var status = lines.All(x => mappedKeys.Contains(MappingKey(x.ItemType, x.ExternalProductId))) ? ExternalOrderStatus.PendingAcceptance : ExternalOrderStatus.BlockedMapping;
            var external = await _db.ExternalDeliveryOrders.FirstOrDefaultAsync(x => x.ConnectionId == connectionId && x.ExternalOrderId == externalId, ct);
            external ??= new ExternalDeliveryOrder { ConnectionId = connectionId, BranchId = connection.BranchId, ExternalOrderId = externalId, CreatedAt = _clock.UtcNow };
            if (external.Id == 0) _db.ExternalDeliveryOrders.Add(external);
            var customer = root.TryGetProperty("customer", out var c) ? c : default;
            var delivery = detail.TryGetProperty("delivery_information", out var d) ? d : default;
            external.ExternalStoreId = connection.ExternalStoreId; external.Status = status; external.CustomerName = $"{Value(customer, "first_name")} {Value(customer, "last_name")}".Trim();
            external.CustomerPhone = Value(customer, "phone_number"); external.DeliveryAddress = Value(delivery, "complete_address"); external.DeliveryMethod = Value(detail, "delivery_method") ?? "delivery";
            external.PaymentMethod = Value(detail, "payment_method") ?? "unknown"; external.Total = IntValue(detail, "total") ?? NestedInt(detail, "totals", "total_order") ?? lines.Sum(x => x.UnitPrice * x.Quantity);
            external.CookingTimeMinutes = IntValue(detail, "cooking_time") ?? connection.DefaultCookingTimeMinutes; external.RawPayloadJson = payload; external.LinesJson = JsonSerializer.Serialize(lines); external.UpdatedAt = _clock.UtcNow;
            if (eventType.Contains("CANCEL", StringComparison.OrdinalIgnoreCase))
            {
                external.Status = ExternalOrderStatus.Cancelled;
                if (external.InternalOrderId.HasValue)
                {
                    var internalOrder = await _db.Orders.FirstOrDefaultAsync(x => x.Id == external.InternalOrderId.Value, ct);
                    if (internalOrder is not null && internalOrder.Status != OrderStatus.Delivered)
                    {
                        internalOrder.Status = OrderStatus.Cancelled;
                        internalOrder.CancelledReason = "Cancelado desde Rappi";
                        internalOrder.AddStatusTime(OrderStatus.Cancelled, _clock.UtcNow);
                    }
                }
            }
            _db.IntegrationWebhookEvents.Add(new IntegrationWebhookEvent { ConnectionId = connectionId, Provider = "rappi", EventKey = eventKey, EventType = eventType, PayloadJson = payload, Status = "processed", ProcessedAt = _clock.UtcNow, CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow });
            await _db.SaveChangesAsync(ct); return Accepted();
        }
        catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
    }

    private IQueryable<DeliveryAppConnection> ConnectionQuery() => _db.DeliveryAppConnections.AsNoTracking().Include(x => x.FinancialApp).Include(x => x.ProductMappings);
    private object ToDto(DeliveryAppConnection x)
    {
        var mappingComplete = x.ProductMappings.Where(m => m.IsActive).All(m => m.ProductId != null) && x.ProductMappings.Any();
        var ready = x.IsActive && x.IsVerified && x.WebhookConfigured && mappingComplete && string.IsNullOrWhiteSpace(x.LastError);
        return new { x.Id, x.BranchId, x.Provider, x.Environment, x.DisplayName, x.ClientId, clientSecretConfigured = !string.IsNullOrWhiteSpace(x.EncryptedClientSecret), x.ExternalStoreId, x.FinancialAppId, financialAppName = x.FinancialApp?.Name, x.DefaultCookingTimeMinutes, x.IsActive, x.IsVerified, x.WebhookConfigured, x.LastVerifiedAt, x.LastCatalogSyncAt, x.LastError, mappingComplete, ready, mappedCount = x.ProductMappings.Count(m => m.IsActive && m.ProductId != null), mappingCount = x.ProductMappings.Count(m => m.IsActive), webhookUrl = $"{Request.Scheme}://{Request.Host}/api/integrations/rappi/webhook/{x.Id}" };
    }

    private async Task<object> MappingResponse(int connectionId, int branchId, CancellationToken ct)
    {
        var mappings = await _db.DeliveryAppProductMappings.AsNoTracking().Where(x => x.ConnectionId == connectionId).Include(x => x.Product).OrderBy(x => x.ExternalName).ToListAsync(ct);
        var products = await _db.Products.AsNoTracking().Where(x => x.Active && x.Category.BranchId == branchId).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        return new { mappings = mappings.Select(x => new { x.Id, x.ExternalProductId, x.ExternalSku, x.ExternalName, x.ItemType, x.IsActive, x.ProductId, productName = x.Product == null ? null : x.Product.Name }), products, complete = mappings.Any() && mappings.Where(x => x.IsActive).All(x => x.ProductId != null) };
    }

    private bool CanAccess(int branchId) => Roles.IsSuperadmin(_currentUser.Role) || (Roles.IsAdmin(_currentUser.Role) && _currentUser.BranchId == branchId);
    private bool CanOperate(int branchId) => Roles.IsSuperadmin(_currentUser.Role) || (Roles.IsAdminOrCashier(_currentUser.Role) && _currentUser.BranchId == branchId);
    private int? ResolveBranch(int? requested) => Roles.IsSuperadmin(_currentUser.Role) ? requested : _currentUser.BranchId;
    private bool ValidateSignature(DeliveryAppConnection c, string payload)
    {
        if (c.Environment == "simulator") return true;
        if (string.IsNullOrWhiteSpace(c.EncryptedWebhookSecret)) return false;
        var supplied = Request.Headers["X-Rappi-Signature"].FirstOrDefault() ?? Request.Headers["X-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_protector.Unprotect(c.EncryptedWebhookSecret)));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var normalized = supplied.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase) ? supplied[7..] : supplied;
        var expectedHex = Convert.ToHexString(digest).ToLowerInvariant();
        var expectedBase64 = Convert.ToBase64String(digest);
        return FixedEquals(expectedHex, normalized.ToLowerInvariant()) || FixedEquals(expectedBase64, normalized);
    }
    private static bool FixedEquals(string expected, string supplied) => expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(supplied));
    private static string MappingKey(string itemType, string externalProductId) => $"{itemType.Trim().ToLowerInvariant()}:{externalProductId.Trim()}";
    private static List<ExternalDeliveryOrderLine> ParseLines(JsonElement detail)
    {
        if (!detail.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return [];
        var result = new List<ExternalDeliveryOrderLine>();
        foreach (var item in items.EnumerateArray()) result.Add(new(Value(item, "id") ?? Value(item, "sku") ?? Guid.NewGuid().ToString(), Value(item, "sku") ?? "", Value(item, "name") ?? "Producto Rappi", Value(item, "type") ?? "product", IntValue(item, "quantity") ?? 1, IntValue(item, "price") ?? IntValue(item, "unit_price_with_discount") ?? 0, Value(item, "comments")));
        return result;
    }
    private static string? Value(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ToString() : null;
    private static int? IntValue(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : null;
    private static int? NestedInt(JsonElement e, string obj, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(obj, out var nested) ? IntValue(nested, name) : null;
}

public sealed class UpsertRappiConnectionDto
{
    public string DisplayName { get; set; } = "Rappi";
    public string Environment { get; set; } = "sandbox";
    public string ClientId { get; set; } = string.Empty;
    public string? ClientSecret { get; set; }
    public string ExternalStoreId { get; set; } = string.Empty;
    public int FinancialAppId { get; set; }
    public int DefaultCookingTimeMinutes { get; set; } = 30;
    public bool IsActive { get; set; }
}
public sealed class MapProductDto { public int ProductId { get; set; } }
