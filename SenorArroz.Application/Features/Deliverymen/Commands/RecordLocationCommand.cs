using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class RecordLocationCommand : IRequest
{
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class RecordLocationHandler : IRequestHandler<RecordLocationCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderNotificationService _notifications;

    public RecordLocationHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IOrderNotificationService notifications)
    {
        _db = db;
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public async Task Handle(RecordLocationCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuario no autenticado.");

        var deliverymanId = _currentUser.Id;
        var branchId = _currentUser.BranchId;

        // Busca la ruta activa más reciente del domiciliario (Open o InProgress)
        var activeRoute = await _db.DeliveryRoutes
            .Where(r => r.DeliverymanId == deliverymanId &&
                        (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress))
            .OrderByDescending(r => r.LastAssignmentAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeRoute is null)
            return;

        // Solo registra si hay al menos un pedido "en camino" en esa ruta
        var hasOnTheWay = await _db.Orders
            .AnyAsync(o => o.DeliveryRouteId == activeRoute.Id &&
                           o.Status == OrderStatus.OnTheWay,
                      cancellationToken);

        if (!hasOnTheWay)
            return;

        _db.DeliverymanLocations.Add(new DeliverymanLocation
        {
            DeliverymanId = deliverymanId,
            DeliveryRouteId = activeRoute.Id,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RecordedAt = request.RecordedAt,
        });
        await _db.SaveChangesAsync(cancellationToken);

        // Propaga a admins de la sucursal vía SignalR
        await _notifications.NotifyDeliverymanLocation(
            branchId,
            deliverymanId,
            activeRoute.Id,
            (double)request.Latitude,
            (double)request.Longitude,
            request.RecordedAt);
    }
}
