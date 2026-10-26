using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Services;
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
        IApplicationDbContext db,
        IDeliveryAppVersionPolicy deliveryAppVersionPolicy) : IRequestHandler<LoginCommand, AuthResponseDto>
    {
        private readonly IAuthRepository _authRepository = authRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository = refreshTokenRepository;
        private readonly IJwtService _jwtService = jwtService;
        private readonly IMapper _mapper = mapper;
        private readonly IClock _clock = clock;
        private readonly IApplicationDbContext _db = db;
        private readonly IDeliveryAppVersionPolicy _deliveryAppVersionPolicy = deliveryAppVersionPolicy;

        public async Task<AuthResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            // Validar usuario
            var user = await _authRepository.GetUserByEmailAsync(request.Email, cancellationToken) ?? throw new BusinessException("Credenciales inválidas");

            // Validar contraseña
            if (!await _authRepository.ValidatePasswordAsync(user, request.Password))
                throw new BusinessException("Credenciales inválidas");

            if (!TenantAccessRules.CanAuthenticate(user))
                throw new BusinessException("Credenciales inválidas");

            var isDeliveryman = user.Role == UserRole.Deliveryman;
            // Otros clientes de la API que no envían cabecera web siguen usando
            // su rol habitual. Flutter se identifica por versión/instalación.
            var hasDeliveryAppMetadata = !string.IsNullOrWhiteSpace(request.DeviceInstallationId)
                || request.DeliveryAppVersion?.VersionName is not null
                || request.DeliveryAppVersion?.BuildNumber is not null
                || request.DeliveryAppVersion?.PackageName is not null;
            var isDeliveryAppLogin = !request.IsWebClient && (isDeliveryman || hasDeliveryAppMetadata);
            if (user.Role == UserRole.Kitchen && !user.WebAccessEnabled)
                throw new BusinessException("Esta cuenta registra almuerzos desde la pantalla compartida de cocina.");
            if (request.IsWebClient && isDeliveryman && !user.WebAccessEnabled)
                throw new DeliverymanWebAccessDeniedException();
            if (isDeliveryAppLogin)
            {
                if (!user.CanDeliver)
                    throw new BusinessException("No tienes habilitado el acceso a la app de domicilios.");
                _deliveryAppVersionPolicy.EnsureCompatible(request.DeliveryAppVersion);
            }

            // La sesión es global, no por aplicación.
            var sessionId = Guid.NewGuid();
            var deviceInstallationId = isDeliveryAppLogin
                ? NormalizeDeviceId(request.DeviceInstallationId)
                : null;

            await using var transaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction is null
                ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
            await DeliveryRouteScope.LockDriversAsync(_db, [user.Id], cancellationToken);
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

                // session_id reemplaza cualquier JWT anterior (incluidos los web).
                await _db.SaveChangesAsync(cancellationToken);

            // session_id invalida JWT y refresh anteriores sin revocaciones
            // masivas que puedan interferir con un login simultáneo.

            var accessToken = isDeliveryAppLogin && !isDeliveryman
                ? _jwtService.GenerateAccessToken(user, sessionId, deviceInstallationId, "Deliveryman")
                : _jwtService.GenerateAccessToken(user, sessionId, deviceInstallationId);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Crear refresh token entity
            var refreshTokenEntity = new RefreshToken
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                SessionId = sessionId,
                Token = refreshToken,
                ExpiresAt = _clock.UtcNow.AddDays(7) // 7 días de validez
            };

            await _refreshTokenRepository.AddAsync(refreshTokenEntity, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);

            // Mapear respuesta
            var userInfo = _mapper.Map<UserInfoDto>(user);
            if (isDeliveryAppLogin)
                userInfo.Role = "Deliveryman";

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
