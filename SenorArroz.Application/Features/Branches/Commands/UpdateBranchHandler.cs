using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Commands;

public class UpdateBranchHandler : IRequestHandler<UpdateBranchCommand, BranchDto>
{
    private readonly IBranchRepository _branchRepository;
    private readonly IApplicationDbContext _db;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IFcmPushService _fcm;
    private readonly ILogger<UpdateBranchHandler> _logger;
    private readonly IRefreshTokenRepository _refreshTokenRepository;

    public UpdateBranchHandler(
        IBranchRepository branchRepository,
        IApplicationDbContext db,
        IMapper mapper,
        ICurrentUser currentUser,
        IClock clock,
        IFcmPushService fcm,
        ILogger<UpdateBranchHandler> logger,
        IRefreshTokenRepository refreshTokenRepository)
    {
        _branchRepository = branchRepository;
        _db = db;
        _mapper = mapper;
        _currentUser = currentUser;
        _clock = clock;
        _fcm = fcm;
        _logger = logger;
        _refreshTokenRepository = refreshTokenRepository;
    }

    public async Task<BranchDto> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        var branch = await _branchRepository.GetByIdAsync(request.Id, cancellationToken);
        if (branch == null)
        {
            throw new NotFoundException($"Sucursal con ID {request.Id} no encontrada");
        }

        var role = _currentUser.Role ?? string.Empty;
        var isSuperadmin = Roles.IsSuperadmin(role);
        var isAdmin = Roles.IsAdmin(role);

        if (!isSuperadmin && isAdmin)
        {
            if (request.Id != _currentUser.BranchId)
                throw new BusinessException("No puedes editar una sucursal que no sea la tuya.");

            if (!string.Equals(request.Name.Trim(), branch.Name, StringComparison.Ordinal))
                throw new BusinessException("Solo un superadmin puede cambiar el nombre de la sucursal.");
        }

        // Validate name doesn't exist for other branches
        if (await _branchRepository.NameExistsAsync(request.Name, request.Id))
        {
            throw new BusinessException($"Ya existe otra sucursal con el nombre '{request.Name}'");
        }

        // Validate phone doesn't exist for other branches
        if (await _branchRepository.PhoneExistsAsync(request.Phone1, request.Id))
        {
            throw new BusinessException($"Ya existe otra sucursal con el teléfono {request.Phone1}");
        }

        if (!string.IsNullOrEmpty(request.Phone2) && await _branchRepository.PhoneExistsAsync(request.Phone2, request.Id))
        {
            throw new BusinessException($"Ya existe otra sucursal con el teléfono {request.Phone2}");
        }

        BranchCoordinatesValidator.EnsureValid(request.Latitude, request.Longitude);
        var autoCompleteArrivalRadius = request.DeliveryAutoCompleteArrivalRadiusMeters
                                        ?? branch.DeliveryAutoCompleteArrivalRadiusMeters;
        var autoCompleteDepartureRadius = request.DeliveryAutoCompleteDepartureRadiusMeters
                                          ?? branch.DeliveryAutoCompleteDepartureRadiusMeters;
        var autoCompleteMinPresence = request.DeliveryAutoCompleteMinPresenceSeconds
                                      ?? branch.DeliveryAutoCompleteMinPresenceSeconds;
        BranchDeliveryAutoCompletionSettingsValidator.EnsureValid(
            autoCompleteArrivalRadius,
            autoCompleteDepartureRadius,
            autoCompleteMinPresence);

        // Update branch
        branch.Name = request.Name.Trim();
        branch.BusinessName = NullIfWhiteSpace(request.BusinessName);
        branch.Nit = NullIfWhiteSpace(request.Nit);
        branch.Address = request.Address.Trim();
        branch.Phone1 = request.Phone1;
        branch.Phone2 = request.Phone2;
        branch.Latitude = request.Latitude;
        branch.Longitude = request.Longitude;
        if (isSuperadmin && request.IsActive.HasValue)
            branch.IsActive = request.IsActive.Value;
        if (request.MaxFreeDeliveryDiscount.HasValue)
            branch.MaxFreeDeliveryDiscount = Math.Max(0, request.MaxFreeDeliveryDiscount.Value);
        branch.PosCopyEtaMinMinutes = BranchEtaLimits.ClampMinutes(request.PosCopyEtaMinMinutes, 30);
        branch.PosCopyEtaRangeMinutes = BranchEtaLimits.ClampMinutes(request.PosCopyEtaRangeMinutes, 15);
        var sessionsWithUpdatedCutoff = new List<DeliveryWorkSession>();
        if (request.DeliveryTrackingAutoCloseTime.HasValue
            && request.DeliveryTrackingAutoCloseTime.Value != branch.DeliveryTrackingAutoCloseTime)
        {
            branch.DeliveryTrackingAutoCloseTime = request.DeliveryTrackingAutoCloseTime.Value;
            sessionsWithUpdatedCutoff = await _db.DeliveryWorkSessions
                .Where(x => x.BranchId == branch.Id && x.Status == DeliveryWorkSessionStatus.Active)
                .ToListAsync(cancellationToken);
            var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
            ApplyAutoCloseTimeToActiveSessions(
                sessionsWithUpdatedCutoff,
                branch.DeliveryTrackingAutoCloseTime,
                nowUtc);
            foreach (var closedSession in sessionsWithUpdatedCutoff
                         .Where(x => x.Status == DeliveryWorkSessionStatus.Closed))
            {
                _db.DeliveryDeviceEvents.Add(DeliveryDeviceEvent.ForClosure(
                    closedSession,
                    nowUtc,
                    DeliveryWorkSessionEndReason.AutomaticClosure));
                await _refreshTokenRepository.RevokeAllByUserIdAsync(
                    closedSession.DeliverymanId,
                    "updated-work-session-cutoff",
                    cancellationToken);
            }
        }
        if (request.DeliveryTrackingLightIntervalSeconds.HasValue)
            branch.DeliveryTrackingLightIntervalSeconds = request.DeliveryTrackingLightIntervalSeconds.Value;
        if (request.DeliveryTrackingActiveIntervalSeconds.HasValue)
            branch.DeliveryTrackingActiveIntervalSeconds = request.DeliveryTrackingActiveIntervalSeconds.Value;
        if (request.DeliveryTrackingStayThresholdMinutes.HasValue)
            branch.DeliveryTrackingStayThresholdMinutes = request.DeliveryTrackingStayThresholdMinutes.Value;
        if (request.DeliveryTrackingStayRadiusMeters.HasValue)
            branch.DeliveryTrackingStayRadiusMeters = request.DeliveryTrackingStayRadiusMeters.Value;
        if (request.DeliveryTrackingAllowedDistanceMeters.HasValue)
            branch.DeliveryTrackingAllowedDistanceMeters = request.DeliveryTrackingAllowedDistanceMeters.Value;
        if (request.DeliveryTrackingLocationRetentionDays.HasValue)
            branch.DeliveryTrackingLocationRetentionDays = request.DeliveryTrackingLocationRetentionDays.Value;
        if (request.DeliveryTrackingIncidentRetentionDays.HasValue)
            branch.DeliveryTrackingIncidentRetentionDays = request.DeliveryTrackingIncidentRetentionDays.Value;
        if (request.DeliveryAutoCompleteEnabled.HasValue)
            branch.DeliveryAutoCompleteEnabled = request.DeliveryAutoCompleteEnabled.Value;
        branch.DeliveryAutoCompleteArrivalRadiusMeters = autoCompleteArrivalRadius;
        branch.DeliveryAutoCompleteDepartureRadiusMeters = autoCompleteDepartureRadius;
        branch.DeliveryAutoCompleteMinPresenceSeconds = autoCompleteMinPresence;

        var staysToReclassify = await (
            from stay in _db.DeliveryStays
            join session in _db.DeliveryWorkSessions on stay.WorkSessionId equals session.Id
            where session.BranchId == branch.Id
            select stay)
            .ToListAsync(cancellationToken);
        foreach (var stay in staysToReclassify)
            stay.InvalidateClassification();

        branch = await _branchRepository.UpdateAsync(branch, cancellationToken);

        var branchDto = _mapper.Map<BranchDto>(branch);

        // Add current statistics
        branchDto.TotalUsers = await _branchRepository.GetTotalUsersAsync(branch.Id, cancellationToken);
        branchDto.ActiveUsers = await _branchRepository.GetActiveUsersAsync(branch.Id, cancellationToken);
        branchDto.TotalCustomers = await _branchRepository.GetTotalCustomersAsync(branch.Id, cancellationToken);
        branchDto.ActiveCustomers = await _branchRepository.GetActiveCustomersAsync(branch.Id, cancellationToken);
        branchDto.TotalNeighborhoods = await _branchRepository.GetTotalNeighborhoodsAsync(branch.Id, cancellationToken);

        var ps = await _db.BranchPrintSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchId == branch.Id, cancellationToken);
        branchDto.PrintSettings = ps is null ? null : _mapper.Map<BranchPrintSettingsDto>(ps);

        await NotifyUpdatedWorkSessionsAsync(sessionsWithUpdatedCutoff, cancellationToken);

        return branchDto;
    }

    private static string? NullIfWhiteSpace(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static void ApplyAutoCloseTimeToActiveSessions(
        IEnumerable<DeliveryWorkSession> sessions,
        TimeOnly autoCloseTime,
        DateTime nowUtc)
    {
        foreach (var session in sessions)
        {
            var sessionDateColombia = ColombiaTimeHelper
                .GetNowInColombiaFromUtc(session.StartedAt)
                .Date;
            var cutoffLocal = sessionDateColombia.Add(autoCloseTime.ToTimeSpan());
            session.AutoCloseAt = ColombiaTimeHelper.ConvertColombiaToUtc(cutoffLocal);

            if (session.AutoCloseAt <= nowUtc)
            {
                session.Close(nowUtc, DeliveryWorkSessionEndReason.AutomaticClosure);
            }
        }
    }

    private async Task NotifyUpdatedWorkSessionsAsync(
        IReadOnlyCollection<DeliveryWorkSession> sessions,
        CancellationToken cancellationToken)
    {
        foreach (var session in sessions)
        {
            try
            {
                var tokens = await _db.UserDeviceTokens
                    .AsNoTracking()
                    .Where(x => x.UserId == session.DeliverymanId)
                    .Select(x => x.Token)
                    .ToListAsync(cancellationToken);
                var wasClosed = session.Status == DeliveryWorkSessionStatus.Closed;
                var cutoffColombia = ColombiaTimeHelper.GetNowInColombiaFromUtc(session.AutoCloseAt);
                await _fcm.SendToTokensAsync(
                    tokens,
                    wasClosed ? "Jornada finalizada" : "Horario de jornada actualizado",
                    wasClosed
                        ? "La nueva hora de cierre ya se cumplió. El seguimiento fue detenido."
                        : $"La jornada cerrará a las {cutoffColombia:HH:mm} (Colombia).",
                    new Dictionary<string, string>
                    {
                        ["type"] = wasClosed ? "work_session_closed" : "work_session_updated",
                        ["workSessionId"] = session.Id.ToString(),
                        ["autoCloseAt"] = session.AutoCloseAt.ToString("O"),
                    },
                    cancellationToken,
                    $"work_session_cutoff:{session.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "No se pudo notificar el nuevo cierre de la jornada {WorkSessionId}.",
                    session.Id);
            }
        }
    }
}
