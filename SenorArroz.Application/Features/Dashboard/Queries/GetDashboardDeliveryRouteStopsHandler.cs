using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.Dashboard.DTOs;
using SenorArroz.Application.Features.Dashboard.Services;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Dashboard.Queries;

public class GetDashboardDeliveryRouteStopsHandler
    : IRequestHandler<GetDashboardDeliveryRouteStopsQuery, DashboardDeliveryRouteStopsResponseDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetDashboardDeliveryRouteStopsHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<DashboardDeliveryRouteStopsResponseDto> Handle(
        GetDashboardDeliveryRouteStopsQuery request,
        CancellationToken cancellationToken)
    {
        var route = await _db.DeliveryRoutes.AsNoTracking()
            .Include(r => r.Branch)
            .Include(r => r.Stops).ThenInclude(s => s.Order)
            .FirstOrDefaultAsync(r => r.Id == request.RouteId, cancellationToken);

        if (route is null || route.Status == DeliveryRouteStatus.Cancelled)
            throw new NotFoundException("Ruta no encontrada.");

        AssertCanViewRoute(route.BranchId, route.DeliverymanId, request.BranchId);
        var resolvedReturns = await DeliveryRouteCompletion.ResolveHistoryAsync(_db, [route], cancellationToken);

        var stops = await _db.DeliveryRouteStops
            .AsNoTracking()
            .Where(s => s.DeliveryRouteId == request.RouteId)
            .OrderBy(s => s.StopSequence)
            .Select(s => new DashboardDeliveryRouteStopItemDto
            {
                OrderId = s.OrderId,
                IsPending = s.Order.Status != OrderStatus.Delivered && s.Order.Status != OrderStatus.Cancelled,
                IsOnTheWay = s.Order.Status == OrderStatus.OnTheWay,
                StopSequence = s.StopSequence,
                AddressSnapshotText = s.AddressSnapshotText,
                CustomerName = s.Order.Customer != null ? s.Order.Customer.Name : null,
                AddressText = s.Order.Address != null ? s.Order.Address.AddressText : null,
                Latitude = s.Order.Address != null ? s.Order.Address.Latitude : null,
                Longitude = s.Order.Address != null ? s.Order.Address.Longitude : null,
            })
            .ToListAsync(cancellationToken);

        var orders = route.Stops.ToDictionary(s => s.OrderId, s => s.Order);
        foreach (var stop in stops)
        {
            var order = orders.GetValueOrDefault(stop.OrderId);
            stop.DeliveredAtUtc = order?.Status == OrderStatus.Delivered
                ? DeliveryPerformanceAnalytics.StatusTime(order.StatusTimes, "delivered") : null;
            stop.OnTheWayAtUtc = DeliveryPerformanceAnalytics.StatusTime(order?.StatusTimes, "ontheway");
        }
        var observationEnd = route.CompletedAtUtc ?? DateTime.UtcNow;
        var points = route.DepartedAtUtc.HasValue && route.WorkSessionId.HasValue
            ? await _db.DeliverymanLocations.AsNoTracking().Where(p => p.DeliveryRouteId == route.Id
                && p.DeliverymanId == route.DeliverymanId && p.WorkSessionId == route.WorkSessionId
                && p.RecordedAt >= route.DepartedAtUtc && p.RecordedAt <= observationEnd)
                .OrderBy(p => p.RecordedAt).ToListAsync(cancellationToken)
            : new List<SenorArroz.Domain.Entities.DeliverymanLocation>();
        var track = DeliveryPerformanceAnalytics.AnalyzeTrack(route, points, observationEnd);

        var sessions = await DeliveryPerformanceAnalytics.LoadWorkSessionsAsync(_db, [route], cancellationToken);
        return new DashboardDeliveryRouteStopsResponseDto
        {
            RouteId = request.RouteId,
            Status = DeliveryPerformanceAnalytics.RouteState(route),
            StartedAtUtc = DeliveryPerformanceAnalytics.RouteTiming(route).Start,
            ReturnIsInconsistent = !resolvedReturns.Contains(route.Id) && DeliveryPerformanceAnalytics.IsReturnInconsistent(route, sessions),
            BranchLatitude = route.Branch.Latitude,
            BranchLongitude = route.Branch.Longitude,
            DepartedAtUtc = route.DepartedAtUtc,
            CompletedAtUtc = route.CompletedAtUtc,
            Segments = track.Segments,
            GpsCoveragePercent = track.CoveragePercent,
            Stops = stops,
        };
    }

    private void AssertCanViewRoute(int routeBranchId, int routeDeliverymanId, int? requestedBranchId)
    {
        var role = _currentUser.Role ?? string.Empty;

        if (Roles.IsSuperadmin(role))
        {
            if (requestedBranchId.HasValue && routeBranchId != requestedBranchId.Value)
                throw new BusinessException("La ruta no pertenece a la sucursal seleccionada.");
            return;
        }

        if (Roles.IsAdmin(role))
        {
            if (_currentUser.BranchId <= 0 || routeBranchId != _currentUser.BranchId)
                throw new BusinessException("No tienes permiso para ver esta ruta.");
            return;
        }

        if (Roles.IsDeliveryman(role))
        {
            if (routeDeliverymanId != _currentUser.Id)
                throw new BusinessException("No tienes permiso para ver esta ruta.");
            if (requestedBranchId.HasValue && routeBranchId != requestedBranchId.Value)
                throw new BusinessException("La ruta no corresponde a la sucursal indicada.");
            return;
        }

        throw new BusinessException("No tienes permiso para ver esta ruta.");
    }
}
