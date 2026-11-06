using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Helpers;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Auth.Commands
{
    public class LogoutHandler : IRequestHandler<LogoutCommand, bool>
    {
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IAuthRepository _authRepository;
        private readonly IClock _clock;
        private readonly IApplicationDbContext _db;
        private readonly DeliveryOperationsService _operations;
        private readonly ICurrentUser _actor;

        public LogoutHandler(
            IRefreshTokenRepository refreshTokenRepository,
            IAuthRepository authRepository,
            IClock clock, IApplicationDbContext db, DeliveryOperationsService operations, ICurrentUser actor)
        {
            _refreshTokenRepository = refreshTokenRepository;
            _authRepository = authRepository;
            _clock = clock;
            _db = db;
            _operations = operations;
            _actor = actor;
        }

        public async Task<bool> Handle(LogoutCommand request, CancellationToken cancellationToken)
        {
            var refreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken, cancellationToken);
            if (refreshToken == null || !refreshToken.IsActiveAt(_clock.UtcNow))
                return false;
            if (refreshToken.UserId != _actor.Id || refreshToken.TenantId != _actor.TenantId)
                throw new UnauthorizedAccessException();
            if (_actor.SessionId.HasValue && refreshToken.SessionId != _actor.SessionId)
                throw new UnauthorizedAccessException();

            await using var transaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
                ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
            await DeliveryRouteScope.LockDriversAsync(_db, [refreshToken.UserId], cancellationToken);
            var owner = await _db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == refreshToken.UserId, cancellationToken);
            var isDeliverySession = owner is not null && (!UserSessionRules.HasSeparateDeliverySession(owner)
                || owner.ActiveDeliverySessionId == refreshToken.SessionId);
            if (owner is not null && refreshToken.SessionId.HasValue && isDeliverySession
                && UserSessionRules.GetSessionId(owner, true) == refreshToken.SessionId)
            {
                await _operations.CompleteActiveAsync("logout", null, cancellationToken);
                var sessions = await _db.DeliveryWorkSessions.Where(s => s.DeliverymanId == owner.Id
                    && s.BranchId == owner.BranchId && s.Status == DeliveryWorkSessionStatus.Active)
                    .ToListAsync(cancellationToken);
                foreach (var session in sessions)
                    session.Close(_clock.UtcNow, DeliveryWorkSessionEndReason.UserChange);
                await _db.SaveChangesAsync(cancellationToken);
            }

            refreshToken.Revoke(request.IpAddress, _clock.UtcNow);
            await _refreshTokenRepository.UpdateAsync(refreshToken, cancellationToken);
            if (refreshToken.SessionId.HasValue)
            {
                await _authRepository.EndSessionIfCurrentAsync(
                    refreshToken.UserId,
                    refreshToken.SessionId.Value,
                    cancellationToken);
            }

            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return true;
        }
    }
}
