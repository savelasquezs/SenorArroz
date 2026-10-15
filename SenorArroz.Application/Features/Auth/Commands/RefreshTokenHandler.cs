using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
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
        private readonly IDeliveryAppVersionPolicy _deliveryAppVersionPolicy;

        public RefreshTokenHandler(
            IAuthRepository authRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IJwtService jwtService,
            IMapper mapper,
            IClock clock,
            IDeliveryAppVersionPolicy deliveryAppVersionPolicy)
        {
            _authRepository = authRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _jwtService = jwtService;
            _mapper = mapper;
            _clock = clock;
            _deliveryAppVersionPolicy = deliveryAppVersionPolicy;
        }

        public async Task<AuthResponseDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
        {
            // Obtener usuario del token expirado
            var userId = _jwtService.GetUserIdFromExpiredToken(request.Token) ?? throw new BusinessException("Token inválido");
            var tenantId = _jwtService.GetTenantIdFromExpiredToken(request.Token) ?? throw new BusinessException("Token inválido");
            var sessionId = _jwtService.GetSessionIdFromExpiredToken(request.Token);
            var deviceInstallationId = _jwtService.GetDeviceInstallationIdFromExpiredToken(request.Token);

            // Validar refresh token
            var refreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken, cancellationToken);
            if (refreshToken == null || !refreshToken.IsActiveAt(_clock.UtcNow) || refreshToken.UserId != userId)
                throw new BusinessException("Refresh token inválido");

            // Obtener usuario actualizado
            var user = await _authRepository.GetUserByIdWithBranchAsync(userId, cancellationToken) ?? throw new BusinessException("Usuario no encontrado");
            if (!TenantAccessRules.CanAuthenticate(user)
                || user.TenantId != tenantId
                || refreshToken.TenantId != user.TenantId)
            {
                throw new BusinessException("Refresh token inválido");
            }
            // El rol firmado en el JWT anterior define qué app puede renovarlo.
            // Un cliente HTTP de administración sin cabecera web no es Flutter.
            var originalRole = _jwtService.GetRoleFromExpiredToken(request.Token);
            var isDeliveryAppClient = !request.IsWebClient
                && (user.Role == Domain.Enums.UserRole.Deliveryman
                    || string.Equals(originalRole, "Deliveryman", StringComparison.OrdinalIgnoreCase));
            if (request.IsWebClient && user.Role == Domain.Enums.UserRole.Deliveryman && !user.WebAccessEnabled)
                throw new DeliverymanWebAccessDeniedException();
            if (isDeliveryAppClient)
            {
                if (!user.CanDeliver)
                    throw new BusinessException("No tienes habilitado el acceso a la app de domicilios.");
                _deliveryAppVersionPolicy.EnsureCompatible(request.DeliveryAppVersion);
            }

            if (!sessionId.HasValue
                || user.ActiveSessionId != sessionId
                || refreshToken.SessionId != sessionId)
                throw new SessionReplacedException();

            var effectiveRole = isDeliveryAppClient ? "Deliveryman" : user.Role?.ToString() ?? string.Empty;
            if (!string.Equals(_jwtService.GetRoleFromExpiredToken(request.Token),
                    effectiveRole, StringComparison.OrdinalIgnoreCase))
                throw new BusinessException("El refresh token no pertenece a esta aplicación.");

            // Revocar el refresh token usado
            refreshToken.Revoke(request.IpAddress, _clock.UtcNow);
            await _refreshTokenRepository.UpdateAsync(refreshToken, cancellationToken);

            // Generar nuevos tokens
            var newAccessToken = isDeliveryAppClient && user.Role != Domain.Enums.UserRole.Deliveryman
                ? _jwtService.GenerateAccessToken(user, sessionId, deviceInstallationId, "Deliveryman")
                : _jwtService.GenerateAccessToken(user, sessionId,
                    isDeliveryAppClient ? deviceInstallationId : null);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            // Crear nuevo refresh token entity
            var newRefreshTokenEntity = new RefreshToken
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                SessionId = sessionId,
                Token = newRefreshToken,
                ExpiresAt = _clock.UtcNow.AddDays(7)
            };

            await _refreshTokenRepository.AddAsync(newRefreshTokenEntity, cancellationToken);

            // Mapear respuesta
            var userInfo = _mapper.Map<UserInfoDto>(user);
            if (isDeliveryAppClient)
                userInfo.Role = "Deliveryman";

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
