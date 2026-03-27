using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Dashboard.DTOs;
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
            .FirstOrDefaultAsync(r => r.Id == request.RouteId, cancellationToken);

        if (route is null || route.Status != DeliveryRouteStatus.Completed)
            throw new NotFoundException("Ruta no encontrada o aún no cerrada.");

        AssertCanViewRoute(route.BranchId, route.DeliverymanId, request.BranchId);

        var stops = await _db.DeliveryRouteStops
            .AsNoTracking()
            .Where(s => s.DeliveryRouteId == request.RouteId)
            .OrderBy(s => s.StopSequence)
            .Select(s => new DashboardDeliveryRouteStopItemDto
            {
                OrderId = s.OrderId,
                StopSequence = s.StopSequence,
                AddressSnapshotText = s.AddressSnapshotText,
                CustomerName = s.Order.Customer != null ? s.Order.Customer.Name : null,
                AddressText = s.Order.Address != null ? s.Order.Address.AddressText : null,
            })
            .ToListAsync(cancellationToken);

        return new DashboardDeliveryRouteStopsResponseDto
        {
            RouteId = request.RouteId,
            Stops = stops,
        };
    }

    private void AssertCanViewRoute(int routeBranchId, int routeDeliverymanId, int? requestedBranchId)
    {
        var role = _currentUser.Role ?? string.Empty;

        if (string.Equals(role, "superadmin", StringComparison.OrdinalIgnoreCase))
        {
            if (requestedBranchId.HasValue && routeBranchId != requestedBranchId.Value)
                throw new BusinessException("La ruta no pertenece a la sucursal seleccionada.");
            return;
        }

        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            if (_currentUser.BranchId <= 0 || routeBranchId != _currentUser.BranchId)
                throw new BusinessException("No tienes permiso para ver esta ruta.");
            return;
        }

        if (string.Equals(role, "deliveryman", StringComparison.OrdinalIgnoreCase))
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
