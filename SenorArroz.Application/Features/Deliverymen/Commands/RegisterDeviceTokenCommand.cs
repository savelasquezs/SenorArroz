using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class RegisterDeviceTokenCommand : IRequest
{
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
}

public class RegisterDeviceTokenHandler : IRequestHandler<RegisterDeviceTokenCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public RegisterDeviceTokenHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task Handle(RegisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        var userId = _currentUser.Id;

        var existing = await _db.UserDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

        if (existing is not null)
        {
            // Actualiza el usuario asociado (puede cambiar de dispositivo) y timestamp
            existing.UserId = userId;
            existing.Platform = request.Platform;
            existing.LastSeenAt = DateTime.UtcNow;
        }
        else
        {
            _db.UserDeviceTokens.Add(new UserDeviceToken
            {
                UserId = userId,
                Token = request.Token,
                Platform = request.Platform,
                LastSeenAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
