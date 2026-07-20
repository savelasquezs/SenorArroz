using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Deliverymen.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class StartDeliveryWorkSessionCommand : IRequest<DeliveryWorkSessionDto>
{
    public string DeviceInstallationId { get; set; } = string.Empty;
    public string DevicePlatform { get; set; } = string.Empty;
    public string? DeviceDescription { get; set; }
    public string? AppVersion { get; set; }
}

public class StartDeliveryWorkSessionHandler : IRequestHandler<StartDeliveryWorkSessionCommand, DeliveryWorkSessionDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public StartDeliveryWorkSessionHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<DeliveryWorkSessionDto> Handle(StartDeliveryWorkSessionCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        if (string.IsNullOrWhiteSpace(request.DeviceInstallationId))
            throw new BusinessException("El identificador del dispositivo es obligatorio.");

        var branch = await _db.Branches.FirstOrDefaultAsync(x => x.Id == _currentUser.BranchId, cancellationToken)
            ?? throw new BusinessException("La sucursal del domiciliario no existe.");
        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        var nowColombia = ColombiaTimeHelper.GetNowInColombiaFromUtc(nowUtc);
        var cutoffLocal = nowColombia.Date.Add(branch.DeliveryTrackingAutoCloseTime.ToTimeSpan());

        var dayIsBlocked = await _db.DeliverymanDayStates.AsNoTracking()
            .AnyAsync(x => x.BranchId == branch.Id
                           && x.DeliverymanId == _currentUser.Id
                           && x.Date == DateOnly.FromDateTime(nowColombia)
                           && x.Blocked,
                cancellationToken);
        if (dayIsBlocked)
            throw new BusinessException(
                "La jornada fue cerrada por liquidación total. Un administrador debe habilitarla para continuar.");

        var active = await _db.DeliveryWorkSessions
            .FirstOrDefaultAsync(x => x.DeliverymanId == _currentUser.Id && x.Status == DeliveryWorkSessionStatus.Active,
                cancellationToken);

        if (active is not null && nowUtc >= active.AutoCloseAt)
        {
            active.Close(nowUtc, DeliveryWorkSessionEndReason.AutomaticClosure);
            _db.DeliveryDeviceEvents.Add(DeliveryDeviceEvent.ForClosure(
                active,
                nowUtc,
                DeliveryWorkSessionEndReason.AutomaticClosure));
            await _db.SaveChangesAsync(cancellationToken);
            active = null;
        }

        if (nowColombia >= cutoffLocal)
            throw new BusinessException(
                $"No se puede iniciar una jornada después de las {branch.DeliveryTrackingAutoCloseTime:HH:mm}.");

        var installationId = request.DeviceInstallationId.Trim();
        if (active is not null)
        {
            if (active.DeviceInstallationId == installationId)
            {
                UpdateDevice(active, request, nowUtc);
                await _db.SaveChangesAsync(cancellationToken);
                return DeliveryWorkSessionDtoMapper.Map(active, branch);
            }

            active.Close(nowUtc, DeliveryWorkSessionEndReason.UserChange);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var session = new DeliveryWorkSession
        {
            DeliverymanId = _currentUser.Id,
            BranchId = branch.Id,
            DeviceInstallationId = installationId,
            DevicePlatform = Normalize(request.DevicePlatform, 30) ?? "unknown",
            DeviceDescription = Normalize(request.DeviceDescription, 300),
            AppVersion = Normalize(request.AppVersion, 40),
            StartedAt = nowUtc,
            AutoCloseAt = ColombiaTimeHelper.ConvertColombiaToUtc(cutoffLocal),
            Status = DeliveryWorkSessionStatus.Active,
            LastCommunicationAt = nowUtc,
        };
        _db.DeliveryWorkSessions.Add(session);
        await _db.SaveChangesAsync(cancellationToken);

        return DeliveryWorkSessionDtoMapper.Map(session, branch);
    }

    private static void UpdateDevice(DeliveryWorkSession session, StartDeliveryWorkSessionCommand request, DateTime nowUtc)
    {
        session.DevicePlatform = Normalize(request.DevicePlatform, 30) ?? session.DevicePlatform;
        session.DeviceDescription = Normalize(request.DeviceDescription, 300);
        session.AppVersion = Normalize(request.AppVersion, 40);
        session.LastCommunicationAt = nowUtc;
    }

    private static string? Normalize(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is { Length: > 0 } && normalized.Length > maxLength
            ? normalized[..maxLength]
            : normalized;
    }

}
