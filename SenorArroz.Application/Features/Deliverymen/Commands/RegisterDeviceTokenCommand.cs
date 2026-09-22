using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

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
    private readonly IClock _clock;

    public RegisterDeviceTokenHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task Handle(RegisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        var userId = _currentUser.Id;
        var tenantId = await _db.Users
            .Where(user => user.Id == userId && user.Active)
            .Select(user => (int?)user.TenantId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new BusinessException("Usuario no encontrado.");

        var existing = await _db.UserDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

        if (existing is not null)
        {
            // Actualiza el usuario asociado (puede cambiar de dispositivo) y timestamp
            existing.UserId = userId;
            existing.TenantId = tenantId;
            existing.Platform = request.Platform;
            existing.LastSeenAt = _clock.UtcNow;
        }
        else
        {
            _db.UserDeviceTokens.Add(new UserDeviceToken
            {
                TenantId = tenantId,
                UserId = userId,
                Token = request.Token,
                Platform = request.Platform,
                LastSeenAt = _clock.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
