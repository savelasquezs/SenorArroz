using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Auth.Commands
{
    public class LoginHandler(
        IAuthRepository authRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IJwtService jwtService,
        IMapper mapper,
        IClock clock,
        IApplicationDbContext db) : IRequestHandler<LoginCommand, AuthResponseDto>
    {
        private readonly IAuthRepository _authRepository = authRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository = refreshTokenRepository;
        private readonly IJwtService _jwtService = jwtService;
        private readonly IMapper _mapper = mapper;
        private readonly IClock _clock = clock;
        private readonly IApplicationDbContext _db = db;

        public async Task<AuthResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            // Validar usuario
            var user = await _authRepository.GetUserByEmailAsync(request.Email, cancellationToken) ?? throw new BusinessException("Credenciales inválidas");

            // Validar contraseña
            if (!await _authRepository.ValidatePasswordAsync(user, request.Password))
                throw new BusinessException("Credenciales inválidas");

            var isDeliveryman = user.Role == UserRole.Deliveryman;
            var sessionId = isDeliveryman ? Guid.NewGuid() : (Guid?)null;
            var deviceInstallationId = NormalizeDeviceId(request.DeviceInstallationId);

            if (isDeliveryman)
            {
                var trackedUser = await _db.Users.FirstAsync(
                    candidate => candidate.Id == user.Id && candidate.Active,
                    cancellationToken);
                trackedUser.ActiveSessionId = sessionId;
                user.ActiveSessionId = sessionId;

                var activeWorkSessions = await _db.DeliveryWorkSessions
                    .Where(candidate => candidate.DeliverymanId == user.Id
                                        && candidate.Status == DeliveryWorkSessionStatus.Active)
                    .ToListAsync(cancellationToken);
                foreach (var workSession in activeWorkSessions)
                    workSession.Close(_clock.UtcNow, DeliveryWorkSessionEndReason.UserChange);

                var previousDeviceTokens = await _db.UserDeviceTokens
                    .Where(candidate => candidate.UserId == user.Id)
                    .ToListAsync(cancellationToken);
                _db.UserDeviceTokens.RemoveRange(previousDeviceTokens);

                // session_id es la fuente de verdad. No se revocan tokens en
                // bloque porque dos logins simultáneos podrían revocar el token
                // perteneciente a la sesión que finalmente quedó vigente.
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await _refreshTokenRepository.RevokeAllByUserIdAsync(
                    user.Id,
                    request.IpAddress,
                    cancellationToken);
            }

            // Generar tokens
            var accessToken = _jwtService.GenerateAccessToken(
                user,
                sessionId,
                isDeliveryman ? deviceInstallationId : null);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Crear refresh token entity
            var refreshTokenEntity = new RefreshToken
            {
                UserId = user.Id,
                SessionId = sessionId,
                Token = refreshToken,
                ExpiresAt = _clock.UtcNow.AddDays(7) // 7 días de validez
            };

            await _refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);

            // Mapear respuesta
            var userInfo = _mapper.Map<UserInfoDto>(user);

            return new AuthResponseDto
            {
                Token = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = _clock.UtcNow.AddMinutes(720), // Access token expira en 12 horas
                User = userInfo
            };
        }

        private static string? NormalizeDeviceId(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            return normalized is { Length: > 64 } ? normalized[..64] : normalized;
        }
    }
}
