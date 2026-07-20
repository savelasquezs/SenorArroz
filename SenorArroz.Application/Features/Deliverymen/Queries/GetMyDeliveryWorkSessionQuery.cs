using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Deliverymen.Commands;
using SenorArroz.Application.Features.Deliverymen.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetMyDeliveryWorkSessionQuery : IRequest<DeliveryWorkSessionDto?>;

public class GetMyDeliveryWorkSessionHandler : IRequestHandler<GetMyDeliveryWorkSessionQuery, DeliveryWorkSessionDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public GetMyDeliveryWorkSessionHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<DeliveryWorkSessionDto?> Handle(GetMyDeliveryWorkSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await _db.DeliveryWorkSessions
            .FirstOrDefaultAsync(x => x.DeliverymanId == _currentUser.Id && x.Status == DeliveryWorkSessionStatus.Active,
                cancellationToken);
        if (session is null) return null;

        var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
        if (nowUtc >= session.AutoCloseAt)
        {
            session.Close(nowUtc, DeliveryWorkSessionEndReason.AutomaticClosure);
            _db.DeliveryDeviceEvents.Add(DeliveryDeviceEvent.ForClosure(
                session,
                nowUtc,
                DeliveryWorkSessionEndReason.AutomaticClosure));
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        session.LastCommunicationAt = nowUtc;
        var branch = await _db.Branches.FirstAsync(x => x.Id == session.BranchId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return DeliveryWorkSessionDtoMapper.Map(session, branch);
    }
}
