using MediatR;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Auth.Commands;

public class ChangePasswordHandler(
    IAuthRepository authRepository,
    IRefreshTokenRepository refreshTokenRepository) : IRequestHandler<ChangePasswordCommand, bool>
{
    private readonly IAuthRepository _authRepository = authRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository = refreshTokenRepository;

    public async Task<bool> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var success = await _authRepository.ChangePasswordAsync(
            request.UserId,
            request.CurrentPassword,
            request.NewPassword);

        if (success)
        {
            // Revocar todos los refresh tokens al cambiar contraseña
            await _refreshTokenRepository.RevokeAllByUserIdAsync(request.UserId, "password_change");
        }

        return success;
    }
}