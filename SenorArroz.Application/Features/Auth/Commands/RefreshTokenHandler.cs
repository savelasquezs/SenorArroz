using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Auth.Commands
{
    public class RefreshTokenHandler : IRequestHandler<RefreshTokenCommand, AuthResponseDto>
    {
        private readonly IAuthRepository _authRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IJwtService _jwtService;
        private readonly IMapper _mapper;
        private readonly IClock _clock;

        public RefreshTokenHandler(
            IAuthRepository authRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IJwtService jwtService,
            IMapper mapper,
            IClock clock)
        {
            _authRepository = authRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _jwtService = jwtService;
            _mapper = mapper;
            _clock = clock;
        }

        public async Task<AuthResponseDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
        {
            // Obtener usuario del token expirado
            var userId = _jwtService.GetUserIdFromExpiredToken(request.Token) ?? throw new BusinessException("Token inválido");
            var sessionId = _jwtService.GetSessionIdFromExpiredToken(request.Token);
            var deviceInstallationId = _jwtService.GetDeviceInstallationIdFromExpiredToken(request.Token);

            // Validar refresh token
            var refreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken, cancellationToken);
            if (refreshToken == null || !refreshToken.IsActiveAt(_clock.UtcNow) || refreshToken.UserId != userId)
                throw new BusinessException("Refresh token inválido");

            // Obtener usuario actualizado
            var user = await _authRepository.GetUserByIdWithBranchAsync(userId, cancellationToken) ?? throw new BusinessException("Usuario no encontrado");
            if (user.Role == Domain.Enums.UserRole.Deliveryman
                && (user.ActiveSessionId != sessionId || refreshToken.SessionId != sessionId))
            {
                throw new SessionReplacedException();
            }

            // Revocar el refresh token usado
            refreshToken.Revoke(request.IpAddress, _clock.UtcNow);
            await _refreshTokenRepository.UpdateAsync(refreshToken, cancellationToken);

            // Generar nuevos tokens
            var newAccessToken = _jwtService.GenerateAccessToken(
                user,
                sessionId,
                deviceInstallationId);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            // Crear nuevo refresh token entity
            var newRefreshTokenEntity = new RefreshToken
            {
                UserId = user.Id,
                SessionId = sessionId,
                Token = newRefreshToken,
                ExpiresAt = _clock.UtcNow.AddDays(7)
            };

            await _refreshTokenRepository.AddAsync(newRefreshTokenEntity, cancellationToken);

            // Mapear respuesta
            var userInfo = _mapper.Map<UserInfoDto>(user);

            return new AuthResponseDto
            {
                Token = newAccessToken,
                RefreshToken = newRefreshToken,
                ExpiresAt = _clock.UtcNow.AddMinutes(720),
                User = userInfo
            };
        }
    }
}
