using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Services;

public sealed class DeliveryAutoCompletionService : IDeliveryAutoCompletionService
{
    internal const double MaximumAcceptedAccuracyMeters = 50;
    internal static readonly TimeSpan MaximumLocationAge = TimeSpan.FromMinutes(2);

    private readonly IApplicationDbContext _db;
    private readonly ISender _sender;
    private readonly IClock _clock;
    private readonly ILogger<DeliveryAutoCompletionService> _logger;

    public DeliveryAutoCompletionService(
        IApplicationDbContext db,
        ISender sender,
        IClock clock,
        ILogger<DeliveryAutoCompletionService> logger)
    {
        _db = db;
        _sender = sender;
        _clock = clock;
        _logger = logger;
    }

    public async Task EvaluateLocationAsync(
        DeliverymanLocation location,
        CancellationToken cancellationToken = default)
    {
        if (!location.DeliveryRouteId.HasValue)
            return;

        if (location.GpsEnabled == false
            || !location.AccuracyMeters.HasValue
            || location.AccuracyMeters.Value < 0
            || location.AccuracyMeters.Value > MaximumAcceptedAccuracyMeters)
        {
            _logger.LogInformation(
                "AutoDelivery ignored unreliable location accuracy={AccuracyMeters}",
                location.AccuracyMeters);
            return;
        }

        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var recordedAtUtc = ColombiaTimeHelper.EnsureUtc(location.RecordedAt);
        if ((nowUtc - recordedAtUtc).Duration() > MaximumLocationAge)
        {
            _logger.LogInformation(
                "AutoDelivery skipped stale location location={LocationId} recordedAt={RecordedAt}",
                location.Id,
                recordedAtUtc);
            return;
        }

        var routeId = location.DeliveryRouteId.Value;
        var settings = await (
                from route in _db.DeliveryRoutes.AsNoTracking()
                join branch in _db.Branches.AsNoTracking() on route.BranchId equals branch.Id
                where route.Id == routeId
                      && route.DeliverymanId == location.DeliverymanId
                      && (route.Status == DeliveryRouteStatus.Open
                          || route.Status == DeliveryRouteStatus.InProgress)
                select new
                {
                    route.BranchId,
                    branch.DeliveryAutoCompleteEnabled,
                    branch.DeliveryAutoCompleteArrivalRadiusMeters,
                    branch.DeliveryAutoCompleteDepartureRadiusMeters,
                    branch.DeliveryAutoCompleteMinPresenceSeconds,
                })
            .FirstOrDefaultAsync(cancellationToken);
        if (settings is null || !settings.DeliveryAutoCompleteEnabled)
            return;

        if (!SettingsAreValid(
                settings.DeliveryAutoCompleteArrivalRadiusMeters,
                settings.DeliveryAutoCompleteDepartureRadiusMeters,
                settings.DeliveryAutoCompleteMinPresenceSeconds))
        {
            _logger.LogWarning(
                "AutoDelivery skipped invalid branch settings branch={BranchId}",
                settings.BranchId);
            return;
        }

        var pendingStops = await (
                from routeStop in _db.DeliveryRouteStops
                join order in _db.Orders on routeStop.OrderId equals order.Id
                join address in _db.Addresses on order.AddressId equals address.Id
                where routeStop.DeliveryRouteId == routeId
                      && routeStop.AutoDeliveredAtUtc == null
                      && order.Type == OrderType.Delivery
                      && order.Status == OrderStatus.OnTheWay
                      && order.DeliveryManId == location.DeliverymanId
                      && order.DeliveryRouteId == routeId
                      && order.BranchId == settings.BranchId
                      && address.Latitude.HasValue
                      && address.Longitude.HasValue
                orderby routeStop.StopSequence, routeStop.Id
                select new PendingStop(
                    routeStop,
                    order.Id,
                    address.Latitude!.Value,
                    address.Longitude!.Value))
            .ToListAsync(cancellationToken);

        var distances = pendingStops
            .Where(x => CoordinatesAreValid(x.Latitude, x.Longitude))
            .Select(x => new StopDistance(
                x,
                GeoHelper.HaversineDistanceMeters(
                    (double)location.Latitude,
                    (double)location.Longitude,
                    (double)x.Latitude,
                    (double)x.Longitude)))
            .ToList();
        if (distances.Count == 0)
            return;

        var active = distances.FirstOrDefault(x => x.Pending.Stop.ArrivalConfirmedAtUtc.HasValue)
                     ?? distances.FirstOrDefault(x => x.Pending.Stop.ArrivalCandidateAtUtc.HasValue);
        if (active is not null)
        {
            await EvaluateActiveStopAsync(
                active,
                location,
                recordedAtUtc,
                nowUtc,
                settings.DeliveryAutoCompleteArrivalRadiusMeters,
                settings.DeliveryAutoCompleteDepartureRadiusMeters,
                settings.DeliveryAutoCompleteMinPresenceSeconds,
                settings.BranchId,
                cancellationToken);
            return;
        }

        var arrival = distances.FirstOrDefault(
            x => x.DistanceMeters <= settings.DeliveryAutoCompleteArrivalRadiusMeters);
        if (arrival is null)
            return;

        var stop = arrival.Pending.Stop;
        stop.ArrivalCandidateAtUtc = recordedAtUtc;
        stop.ArrivalEvidenceCount = 1;
        stop.ArrivalLastSeenAtUtc = recordedAtUtc;
        stop.ClosestDistanceMeters = arrival.DistanceMeters;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "AutoDelivery arrival candidate order={OrderId} route={RouteId} distance={DistanceMeters}m",
            arrival.Pending.OrderId,
            routeId,
            Math.Round(arrival.DistanceMeters));
    }

    private async Task EvaluateActiveStopAsync(
        StopDistance active,
        DeliverymanLocation location,
        DateTime recordedAtUtc,
        DateTime nowUtc,
        int arrivalRadiusMeters,
        int departureRadiusMeters,
        int minPresenceSeconds,
        int branchId,
        CancellationToken cancellationToken)
    {
        var stop = active.Pending.Stop;
        if (active.DistanceMeters <= arrivalRadiusMeters)
        {
            stop.ArrivalLastSeenAtUtc = Max(stop.ArrivalLastSeenAtUtc, recordedAtUtc);
            stop.ClosestDistanceMeters = stop.ClosestDistanceMeters.HasValue
                ? Math.Min(stop.ClosestDistanceMeters.Value, active.DistanceMeters)
                : active.DistanceMeters;

            if (!stop.ArrivalConfirmedAtUtc.HasValue
                && stop.ArrivalCandidateAtUtc.HasValue
                && recordedAtUtc - stop.ArrivalCandidateAtUtc.Value >= TimeSpan.FromSeconds(minPresenceSeconds))
            {
                stop.ArrivalEvidenceCount = Math.Max(2, stop.ArrivalEvidenceCount + 1);
                stop.ArrivalConfirmedAtUtc = recordedAtUtc;
                _logger.LogInformation(
                    "AutoDelivery arrival confirmed order={OrderId} route={RouteId} evidence={EvidenceCount}",
                    active.Pending.OrderId,
                    stop.DeliveryRouteId,
                    stop.ArrivalEvidenceCount);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (active.DistanceMeters < departureRadiusMeters)
            return;

        if (!stop.ArrivalConfirmedAtUtc.HasValue)
        {
            ResetCandidate(stop);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var stillEligible = await _db.Orders.AsNoTracking().AnyAsync(
            order => order.Id == active.Pending.OrderId
                     && order.Type == OrderType.Delivery
                     && order.Status == OrderStatus.OnTheWay
                     && order.DeliveryManId == location.DeliverymanId
                     && order.DeliveryRouteId == stop.DeliveryRouteId
                     && order.BranchId == branchId
                     && order.AddressId.HasValue
                     && _db.Addresses.Any(address =>
                         address.Id == order.AddressId.Value
                         && address.Latitude.HasValue
                         && address.Longitude.HasValue
                         && address.Latitude.Value >= -90
                         && address.Latitude.Value <= 90
                         && address.Longitude.Value >= -180
                         && address.Longitude.Value <= 180)
                     && _db.DeliveryRoutes.Any(route =>
                         route.Id == stop.DeliveryRouteId
                         && route.DeliverymanId == location.DeliverymanId
                         && route.BranchId == branchId
                         && (route.Status == DeliveryRouteStatus.Open
                             || route.Status == DeliveryRouteStatus.InProgress)),
            cancellationToken);
        if (!stillEligible)
        {
            _logger.LogInformation(
                "AutoDelivery skipped order state changed order={OrderId} route={RouteId}",
                active.Pending.OrderId,
                stop.DeliveryRouteId);
            return;
        }

        try
        {
            var delivered = await _sender.Send(new ChangeOrderStatusCommand
            {
                Id = active.Pending.OrderId,
                StatusChange = new ChangeOrderStatusDto
                {
                    Status = OrderStatus.Delivered,
                    Reason = "Entrega automatica por ubicacion GPS",
                },
                IsAutomaticDelivery = true,
                AutoDeliveredAtUtc = nowUtc,
                AutoDeliveryTriggerLocationId = location.Id,
                AutoDeliveryDepartureDistanceMeters = active.DistanceMeters,
            }, cancellationToken);
            if (delivered.Status != OrderStatus.Delivered)
                return;

            _logger.LogInformation(
                "AutoDelivery completed order={OrderId} route={RouteId} departureDistance={DistanceMeters}m",
                active.Pending.OrderId,
                stop.DeliveryRouteId,
                Math.Round(active.DistanceMeters));
        }
        catch (BusinessException ex)
        {
            _logger.LogInformation(
                ex,
                "AutoDelivery skipped order state changed order={OrderId} route={RouteId}",
                active.Pending.OrderId,
                stop.DeliveryRouteId);
        }
    }

    private static bool SettingsAreValid(int arrivalRadiusMeters, int departureRadiusMeters, int minPresenceSeconds) =>
        arrivalRadiusMeters is >= 10 and <= 150
        && departureRadiusMeters is >= 20 and <= 500
        && departureRadiusMeters > arrivalRadiusMeters
        && minPresenceSeconds is >= 5 and <= 300;

    private static bool CoordinatesAreValid(decimal latitude, decimal longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static DateTime Max(DateTime? current, DateTime value) =>
        !current.HasValue || value > current.Value ? value : current.Value;

    private static void ResetCandidate(DeliveryRouteStop stop)
    {
        stop.ArrivalCandidateAtUtc = null;
        stop.ArrivalConfirmedAtUtc = null;
        stop.ArrivalEvidenceCount = 0;
        stop.ArrivalLastSeenAtUtc = null;
        stop.ClosestDistanceMeters = null;
    }

    private sealed record PendingStop(
        DeliveryRouteStop Stop,
        int OrderId,
        decimal Latitude,
        decimal Longitude);

    private sealed record StopDistance(PendingStop Pending, double DistanceMeters);
}
