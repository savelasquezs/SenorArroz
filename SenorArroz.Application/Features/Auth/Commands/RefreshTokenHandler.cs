using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Auth.Commands;

public class RefreshTokenHandler : IRequestHandler<RefreshTokenCommand, AuthResponseDto>
{
    private readonly IAuthRepository _authRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IJwtService _jwtService;
    private readonly IMapper _mapper;

    public RefreshTokenHandler(
        IAuthRepository authRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IJwtService jwtService,
        IMapper mapper)
    {
        _authRepository = authRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtService = jwtService;
        _mapper = mapper;
    }

    public async Task<AuthResponseDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        // Obtener usuario del token expirado
        var userId = _jwtService.GetUserIdFromExpiredToken(request.Token) ?? throw new BusinessException("Token inválido");

        // Validar refresh token
        var refreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken);
        if (refreshToken == null || !refreshToken.IsActive || refreshToken.UserId != userId)
            throw new BusinessException("Refresh token inválido");

        // Obtener usuario actualizado
        var user = await _authRepository.GetUserByIdWithBranchAsync(userId) ?? throw new BusinessException("Usuario no encontrado");

        // Revocar el refresh token usado
        refreshToken.Revoke(request.IpAddress);
        await _refreshTokenRepository.UpdateAsync(refreshToken);

        // Generar nuevos tokens
        var newAccessToken = _jwtService.GenerateAccessToken(user);
        var newRefreshToken = _jwtService.GenerateRefreshToken();

        // Crear nuevo refresh token entity
        var newRefreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            Token = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        await _refreshTokenRepository.AddAsync(newRefreshTokenEntity);

        // Mapear respuesta
        var userInfo = _mapper.Map<UserInfoDto>(user);

        return new AuthResponseDto
        {
            Token = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60),
            User = userInfo
        };
    }
}
