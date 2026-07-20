using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class RecordLocationCommand : IRequest
{
    public int WorkSessionId { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class RecordLocationHandler : IRequestHandler<RecordLocationCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderNotificationService _notifications;
    private readonly IClock _clock;

    public RecordLocationHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IOrderNotificationService notifications,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _notifications = notifications;
        _clock = clock;
    }

    public async Task Handle(RecordLocationCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuario no autenticado.");

        var deliverymanId = _currentUser.Id;
        var branchId = _currentUser.BranchId;
        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);

        var workSession = await _db.DeliveryWorkSessions
            .FirstOrDefaultAsync(x => x.DeliverymanId == deliverymanId &&
                                      x.BranchId == branchId &&
                                      x.Status == DeliveryWorkSessionStatus.Active,
                cancellationToken);
        if (workSession is null)
            throw new BusinessException("No existe una jornada laboral activa.");
        if (workSession.Id != request.WorkSessionId)
            throw new BusinessException("La jornada laboral del dispositivo ya no está activa.");
        if (nowUtc >= workSession.AutoCloseAt)
        {
            workSession.Close(nowUtc, DeliveryWorkSessionEndReason.AutomaticClosure);
            await _db.SaveChangesAsync(cancellationToken);
            throw new BusinessException("La jornada laboral ya finalizó.");
        }

        // Busca la ruta activa más reciente del domiciliario (Open o InProgress)
        var activeRoute = await _db.DeliveryRoutes
            .Where(r => r.DeliverymanId == deliverymanId &&
                        (r.Status == DeliveryRouteStatus.Open || r.Status == DeliveryRouteStatus.InProgress))
            .OrderByDescending(r => r.LastAssignmentAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        int? activeRouteId = null;
        if (activeRoute is not null)
        {
            var hasOnTheWay = await _db.Orders
                .AnyAsync(o => o.DeliveryRouteId == activeRoute.Id && o.Status == OrderStatus.OnTheWay,
                    cancellationToken);
            if (hasOnTheWay) activeRouteId = activeRoute.Id;
        }

        _db.DeliverymanLocations.Add(new DeliverymanLocation
        {
            DeliverymanId = deliverymanId,
            WorkSessionId = workSession.Id,
            DeliveryRouteId = activeRouteId,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RecordedAt = request.RecordedAt,
        });
        workSession.LastCommunicationAt = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);

        // Propaga a admins de la sucursal vía SignalR
        await _notifications.NotifyDeliverymanLocation(
            branchId,
            deliverymanId,
            activeRouteId,
            (double)request.Latitude,
            (double)request.Longitude,
            request.RecordedAt);
    }
}
