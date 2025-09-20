using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Auth.Commands
{
    public class LoginHandler(
        IAuthRepository authRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IJwtService jwtService,
        IMapper mapper) : IRequestHandler<LoginCommand, AuthResponseDto>
    {
        private readonly IAuthRepository _authRepository = authRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository = refreshTokenRepository;
        private readonly IJwtService _jwtService = jwtService;
        private readonly IMapper _mapper = mapper;

        public async Task<AuthResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
        {
            // Validar usuario
            var user = await _authRepository.GetUserByEmailAsync(request.Email) ?? throw new BusinessException("Credenciales inválidas");

            // Validar contraseña
            if (!await _authRepository.ValidatePasswordAsync(user, request.Password))
                throw new BusinessException("Credenciales inválidas");

            // Revocar tokens activos anteriores
            await _refreshTokenRepository.RevokeAllByUserIdAsync(user.Id, request.IpAddress);

            // Generar tokens
            var accessToken = _jwtService.GenerateAccessToken(user);
            var refreshToken = _jwtService.GenerateRefreshToken();

            // Crear refresh token entity
            var refreshTokenEntity = new RefreshToken
            {
                UserId = user.Id,
                Token = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddDays(7), // 7 días de validez
                CreatedAt = DateTime.UtcNow
            };

            await _refreshTokenRepository.AddAsync(refreshTokenEntity);

            // Mapear respuesta
            var userInfo = _mapper.Map<UserInfoDto>(user);

            return new AuthResponseDto
            {
                Token = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(60), // Access token expira en 1 hora
                User = userInfo
            };
        }
    }
}