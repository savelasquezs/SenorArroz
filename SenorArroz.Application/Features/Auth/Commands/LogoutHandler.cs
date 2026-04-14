using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Auth.Commands
{
    public class LogoutHandler : IRequestHandler<LogoutCommand, bool>
    {
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IClock _clock;

        public LogoutHandler(IRefreshTokenRepository refreshTokenRepository, IClock clock)
        {
            _refreshTokenRepository = refreshTokenRepository;
            _clock = clock;
        }

        public async Task<bool> Handle(LogoutCommand request, CancellationToken cancellationToken)
        {
            var refreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken, cancellationToken);
            if (refreshToken == null || !refreshToken.IsActiveAt(_clock.UtcNow))
                return false;

            refreshToken.Revoke(request.IpAddress, _clock.UtcNow);
            await _refreshTokenRepository.UpdateAsync(refreshToken, cancellationToken);

            return true;
        }
    }
}
