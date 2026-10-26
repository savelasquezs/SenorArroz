using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Services;

public sealed record DeliveryNeighborhoodDto(int Id, int BranchId, string Name, int DeliveryFee);
public sealed record DeliveryClosureDto(int RouteId, string Source, DateTime? At, int Deliveries);
public sealed record DeliveryContextDto(long Revision, Guid? LastTakeId, int? RouteId,
    int? WorkSessionId, string Phase, List<OrderDto> Orders, List<DeliveryNeighborhoodDto> Neighborhoods,
    DeliveryClosureDto? LastClosure = null);
public sealed record DeliveryOperationRequest(Guid OperationId, string Kind, DateTime RecordedAt,
    int? OrderId = null, int? RouteId = null, int? WorkSessionId = null, long ExpectedVersion = 0,
    string? AddressVersion = null, JsonElement? Changes = null,
    SelfAssignOrdersDto? Selection = null, Guid? ExpectedLastTakeId = null);
public sealed record DeliveryOperationResult(Guid OperationId, bool Replayed, DeliveryContextDto Context,
    OrderDto? Order = null);

public sealed class DeliveryOperationsService(IApplicationDbContext db, ICurrentUser actor, IClock clock,
    ISender sender, IMapper mapper, IDeliveryAutoCompletionRouteLock routeLock)
{
    public async Task<DeliveryContextDto> ContextAsync(CancellationToken ct)
    {
        DeliveryContextDto? result = null;
        await routeLock.ExecuteDriverAsync(actor.Id, async token => result = await ReadContextAsync(token), ct);
        return result!;
    }

    private async Task<DeliveryContextDto> ReadContextAsync(CancellationToken ct)
    {
        var owner = await OwnerAsync(ct);
        var route = await db.DeliveryRoutes.AsNoTracking().Where(r => r.DeliverymanId == actor.Id && r.BranchId == actor.BranchId
            && (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress))
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        List<Order> orders = route is null ? [] : await Orders().Where(o => o.DeliveryRouteId == route.Id
            && o.DeliveryManId == actor.Id && o.BranchId == route.BranchId)
            .OrderBy(o => o.Id).ToListAsync(ct);
        if (route is not null) {
            var sequence = await db.DeliveryRouteStops.AsNoTracking().Where(s => s.DeliveryRouteId == route.Id)
                .OrderBy(s => s.StopSequence).Select(s => s.OrderId).ToListAsync(ct);
            orders = orders.OrderBy(o => sequence.IndexOf(o.Id)).ToList();
        }
        var closure = await db.DeliveryRoutes.AsNoTracking().Where(r => r.DeliverymanId == actor.Id && r.BranchId == actor.BranchId
            && (r.ClosureSource == "return" || r.ClosureSource == "next_route"))
            .OrderByDescending(r => r.CompletedAtUtc).FirstOrDefaultAsync(ct);
        return new(owner.DeliveryContextVersion, owner.LastDeliveryTakeId, route?.Id, route?.WorkSessionId,
            route is null ? "idle" : orders.All(o => o.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
                ? "returning" : "delivering", mapper.Map<List<OrderDto>>(orders),
            await db.Neighborhoods.AsNoTracking().Where(n => n.BranchId == actor.BranchId)
                .Select(n => new DeliveryNeighborhoodDto(n.Id, n.BranchId, n.Name, n.DeliveryFee)).ToListAsync(ct),
            closure is null ? null : new(closure.Id, closure.ClosureSource!, closure.CompletedAtUtc,
                await db.Orders.CountAsync(o => o.DeliveryRouteId == closure.Id && o.DeliveryManId == actor.Id
                    && o.DeliveryCompletionSource == closure.ClosureSource, ct)));
    }

    private IQueryable<Order> Orders() => db.Orders.AsNoTracking().Include(o => o.Address).ThenInclude(a => a!.Neighborhood)
        .Include(o => o.Customer).Include(o => o.OrderDetails).ThenInclude(d => d.Product)
        .Include(o => o.BankPayments).Include(o => o.AppPayments).Include(o => o.Deposits).AsSplitQuery();

    private async Task<User> OwnerAsync(CancellationToken ct)
    {
        var owner = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actor.Id, ct);
        if (owner is null || !owner.Active || !owner.CanDeliver || owner.TenantId != actor.TenantId
            || owner.BranchId != actor.BranchId) throw new UnauthorizedAccessException();
        if (actor.SessionId.HasValue && owner.ActiveSessionId != actor.SessionId) throw new SessionReplacedException();
        return owner;
    }

    public async Task<DeliveryOperationResult> ExecuteAsync(DeliveryOperationRequest request, CancellationToken ct)
    {
        if (request.OperationId == Guid.Empty || request.Kind is not ("take" or "deliver" or "address")
            || request.RecordedAt.Kind != DateTimeKind.Utc || request.RecordedAt > clock.UtcNow.AddMinutes(2))
            throw new BusinessException("La operación no es válida.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var key = request.OperationId.ToString();
        var replayed = false;
        await routeLock.ExecuteDriverAsync(actor.Id, async token =>
        {
            await OwnerAsync(token);
            var receipt = await db.EntityAuditLogs.AsNoTracking().SingleOrDefaultAsync(a =>
                a.EntityType == "delivery_operation" && a.EntityId == actor.Id && a.OperationType == key, token);
            if (receipt is not null)
            {
                if (receipt.MetadataJson != JsonSerializer.Serialize(hash)) throw new OrderConcurrencyException();
                replayed = true;
                return;
            }
            if (await db.EmployeeLunches.AnyAsync(l => l.UserId == actor.Id && l.EndedAt == null, token))
                throw new BusinessException("Finaliza el almuerzo antes de sincronizar esta acción.");
            if (request.Kind == "take")
            {
                var context = await ContextAsync(token);
                if (context.LastTakeId != request.ExpectedLastTakeId
                    || context.RouteId != request.RouteId && context.RouteId is not null)
                    throw new OrderConcurrencyException();
                if (context.RouteId is null && request.RouteId.HasValue
                    && !await db.DeliveryRoutes.AnyAsync(r => r.Id == request.RouteId && r.DeliverymanId == actor.Id && r.BranchId == actor.BranchId
                        && r.Status == DeliveryRouteStatus.Completed && r.ClosureSource == "return", token))
                    throw new OrderConcurrencyException();
                var session = await DeliveryRouteScope.ActiveSessions(db, actor.BranchId, clock.UtcNow)
                    .SingleOrDefaultAsync(s => s.DeliverymanId == actor.Id, token);
                if (session is null || session.Id != request.WorkSessionId) throw new OrderConcurrencyException();
                var selection = request.Selection ?? throw new BusinessException("Selecciona los pedidos.");
                await sender.Send(new SelfAssignOrdersCommand
                {
                    OrderIds = selection.OrderIds, ProposalId = selection.ProposalId,
                    ExpectedPlanVersion = selection.ExpectedPlanVersion, ModifiedProposal = selection.ModifiedProposal,
                    SelectionFingerprint = selection.SelectionFingerprint, ReplaceActiveRoute = true
                }, token);
                await DeliveryRouteScope.AdvanceContextAsync(db, [actor.Id], token, request.OperationId);
            }
            else
            {
                var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == request.OrderId, token)
                    ?? throw new NotFoundException("Pedido no encontrado.");
                if (order.TenantId != actor.TenantId || order.BranchId != actor.BranchId
                    || order.DeliveryManId != actor.Id || order.DeliveryRouteId != request.RouteId)
                    throw new OrderConcurrencyException();
                var session = await db.DeliveryWorkSessions.AsNoTracking().SingleOrDefaultAsync(s =>
                    s.Id == request.WorkSessionId && s.DeliverymanId == actor.Id && s.BranchId == order.BranchId, token);
                if (session is null || request.RecordedAt < session.StartedAt || request.RecordedAt >= session.AutoCloseAt
                    || session.EndedAt.HasValue && request.RecordedAt > session.EndedAt
                    || actor.DeviceInstallationId is not null && actor.DeviceInstallationId != session.DeviceInstallationId)
                    throw new BusinessException("La acción no pertenece a una jornada válida.");
                if (request.Kind == "deliver")
                {
                    if (order.Status != OrderStatus.Delivered)
                    {
                        if (order.Status != OrderStatus.OnTheWay || order.ManagementVersion != request.ExpectedVersion)
                            throw new OrderConcurrencyException();
                        await sender.Send(new ChangeOrderStatusCommand { Id = order.Id,
                            RouteCompletionSource = "offline_confirmation",
                            StatusChange = new() { Status = OrderStatus.Delivered, Reason = "Entrega registrada por domiciliario" } }, token);
                    }
                }
                else {
                    if (order.Status == OrderStatus.Cancelled || order.ManagementVersion != request.ExpectedVersion)
                        throw new OrderConcurrencyException();
                    await CorrectAddressAsync(order, request, token);
                }
            }
            await DeliveryRouteScope.AdvanceContextAsync(db, [actor.Id], token);
            db.EntityAuditLogs.Add(new EntityAuditLog
            {
                TenantId = actor.TenantId, BranchId = actor.BranchId, EntityType = "delivery_operation",
                EntityId = actor.Id, OperationType = key, ChangedAt = clock.UtcNow,
                BusinessDate = clock.UtcNow.AddHours(-5).Date, ChangedByUserId = actor.Id,
                SummaryText = request.Kind, MetadataJson = JsonSerializer.Serialize(hash),
                AfterJson = JsonSerializer.Serialize(new { request.Kind, request.OrderId, request.RouteId,
                    request.RecordedAt, acceptedAt = clock.UtcNow })
            });
            await db.SaveChangesAsync(token);
        }, ct);
        var updated = request.OrderId.HasValue ? await Orders().SingleOrDefaultAsync(o => o.Id == request.OrderId
            && o.DeliveryManId == actor.Id && o.BranchId == actor.BranchId, ct) : null;
        return new(request.OperationId, replayed, await ContextAsync(ct), updated is null ? null : mapper.Map<OrderDto>(updated));
    }

    private async Task CorrectAddressAsync(Order order, DeliveryOperationRequest request, CancellationToken ct)
    {
        if (!order.AddressId.HasValue || request.Changes is not JsonElement changes || changes.ValueKind != JsonValueKind.Object)
            throw new BusinessException("La dirección no es válida.");
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM address WHERE id = {order.AddressId} AND tenant_id = {actor.TenantId} FOR UPDATE", ct);
        var address = await db.Addresses.SingleAsync(a => a.Id == order.AddressId, ct);
        if (address.UpdatedAt.Ticks.ToString() != request.AddressVersion) throw new OrderConcurrencyException();
        foreach (var field in changes.EnumerateObject())
        {
            if (field.Name == "address" && field.Value.ValueKind != JsonValueKind.String
                || field.Name == "additionalInfo" && field.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                || field.Name == "neighborhoodId" && (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt32(out _))
                || (field.Name is "latitude" or "longitude") && (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetDecimal(out _)))
                throw new BusinessException("La corrección contiene un valor no válido.");
            switch (field.Name)
            {
                case "address":
                    var text = field.Value.GetString()?.Trim();
                    if (string.IsNullOrEmpty(text) || text.Length > 200) throw new BusinessException("Revisa la dirección.");
                    address.AddressText = text; break;
                case "additionalInfo":
                    var info = field.Value.ValueKind == JsonValueKind.Null ? null : field.Value.GetString()?.Trim();
                    if (info?.Length > 150) throw new BusinessException("La información adicional es demasiado larga.");
                    address.AdditionalInfo = info; break;
                case "neighborhoodId":
                    var id = field.Value.GetInt32();
                    if (!await db.Neighborhoods.AnyAsync(n => n.Id == id && n.BranchId == order.BranchId, ct))
                        throw new BusinessException("El barrio no corresponde a esta sucursal.");
                    address.NeighborhoodId = id; break;
                case "latitude": address.Latitude = field.Value.GetDecimal(); break;
                case "longitude": address.Longitude = field.Value.GetDecimal(); break;
                default: throw new BusinessException("La corrección contiene campos no permitidos.");
            }
        }
        if (address.Latitude is < -90 or > 90 || address.Longitude is < -180 or > 180)
            throw new BusinessException("Las coordenadas no son válidas.");
        address.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteActiveAsync(string source, DateTime? observedAt, CancellationToken ct,
        int? driverId = null, int? expectedRouteId = null)
    {
        var ownerId = driverId ?? actor.Id;
        var routes = await db.DeliveryRoutes.Where(r => r.DeliverymanId == ownerId
            && (driverId.HasValue || r.BranchId == actor.BranchId)
            && (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress)
            && (!expectedRouteId.HasValue || r.Id == expectedRouteId)).ToListAsync(ct);
        foreach (var route in routes)
        {
            var orders = await db.Orders.AsNoTracking().Where(o => o.DeliveryRouteId == route.Id
                && o.DeliveryManId == ownerId && o.BranchId == route.BranchId
                && (o.Type == OrderType.Delivery || o.Type == OrderType.Reservation && o.AddressId.HasValue))
                .OrderBy(o => o.Id).ToListAsync(ct);
            foreach (var order in orders.Where(o => o.Status is not (OrderStatus.Delivered or OrderStatus.Cancelled)))
                await sender.Send(new ChangeOrderStatusCommand { Id = order.Id, RouteCompletionSource = source,
                    StatusChange = new() { Status = OrderStatus.Delivered, Reason = $"Cierre operativo de ruta: {source}" } }, ct);
            route.ClosureSource = source;
            route.CompletedAtUtc = observedAt ?? clock.UtcNow;
            route.Status = orders.Count > 0 && orders.All(o => o.Status == OrderStatus.Cancelled)
                ? DeliveryRouteStatus.Cancelled : DeliveryRouteStatus.Completed;
            route.ActualDurationSeconds = source == "return" && route.DepartedAtUtc.HasValue
                ? (int)Math.Max(0, (route.CompletedAtUtc.Value - route.DepartedAtUtc.Value).TotalSeconds) : null;
            route.MetSla = route.ActualDurationSeconds.HasValue && route.MetaDurationSeconds.HasValue
                ? route.ActualDurationSeconds <= route.MetaDurationSeconds : null;
            db.EntityAuditLogs.Add(new EntityAuditLog { TenantId = route.TenantId, BranchId = route.BranchId,
                EntityType = "delivery_route", EntityId = route.Id, OperationType = "operational_closure",
                ChangedAt = clock.UtcNow, BusinessDate = clock.UtcNow.AddHours(-5).Date,
                ChangedByUserId = ownerId, SummaryText = source,
                MetadataJson = JsonSerializer.Serialize(new { source, observedAt, recordedAt = clock.UtcNow,
                    orderIds = orders.Select(o => o.Id) }) });
        }
        if (routes.Count > 0)
        {
            await DeliveryRouteScope.AdvanceContextAsync(db, [ownerId], ct);
            await db.SaveChangesAsync(ct);
        }
    }
}
