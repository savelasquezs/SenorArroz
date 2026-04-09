using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class RemoveDeviceTokenCommand : IRequest
{
    public string Token { get; set; } = string.Empty;
}

public class RemoveDeviceTokenHandler : IRequestHandler<RemoveDeviceTokenCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public RemoveDeviceTokenHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task Handle(RemoveDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        var userId = _currentUser.Id;

        var token = await _db.UserDeviceTokens
            .FirstOrDefaultAsync(
                t => t.Token == request.Token && t.UserId == userId,
                cancellationToken);

        if (token is not null)
        {
            _db.UserDeviceTokens.Remove(token);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
