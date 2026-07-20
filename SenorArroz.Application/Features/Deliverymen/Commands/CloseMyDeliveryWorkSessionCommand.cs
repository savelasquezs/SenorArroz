using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class CloseMyDeliveryWorkSessionCommand : IRequest;

public class CloseMyDeliveryWorkSessionHandler : IRequestHandler<CloseMyDeliveryWorkSessionCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public CloseMyDeliveryWorkSessionHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task Handle(CloseMyDeliveryWorkSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await _db.DeliveryWorkSessions
            .FirstOrDefaultAsync(x => x.DeliverymanId == _currentUser.Id && x.Status == DeliveryWorkSessionStatus.Active,
                cancellationToken);
        if (session is null) return;

        StartDeliveryWorkSessionHandler.Close(
            session,
            ColombiaTimeHelper.EnsureUtc(_clock.UtcNow),
            DeliveryWorkSessionEndReason.UserChange);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
