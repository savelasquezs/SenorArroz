using MediatR;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Auth.Commands
{
    public class ResetPasswordHandler(
        IPasswordResetRepository passwordResetRepository,
        IAuthRepository authRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordService passwordService,
        IEmailService emailService,
        IUserRepository userRepository,
        ILogger<ResetPasswordHandler> logger) : IRequestHandler<ResetPasswordCommand, bool>
    {
        private readonly IPasswordResetRepository _passwordResetRepository = passwordResetRepository;
        private readonly IAuthRepository _authRepository = authRepository;
        private readonly IUserRepository _userRepository= userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository = refreshTokenRepository;
        private readonly IPasswordService _passwordService = passwordService;
        private readonly IEmailService _emailService = emailService;
        private readonly ILogger<ResetPasswordHandler> _logger = logger;

        public async Task<bool> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Validate reset token
           
                var resetToken = await _passwordResetRepository.GetByTokenAsync(request.Token);
           

                if (resetToken == null )
                {
                    throw new BusinessException("Token de recuperación inválido ");
                }
                if (resetToken.IsUsed)
                {
                    throw new BusinessException("El token de recuperación ya ha sido utilizado");
                }
                if (resetToken.IsExpired)
                {
                    throw new BusinessException($"El token de recuperación ha expirado,expira en : {resetToken.ExpiresAt} y hora actual {DateTime.UtcNow}");
                }

                // Verify email matches
                if (!string.Equals(resetToken.Email, request.Email, StringComparison.OrdinalIgnoreCase))
                {
                    throw new BusinessException("El email no coincide con el token de recuperación");
                }

                // Get user
                var user = await _authRepository.GetUserByIdWithBranchAsync(resetToken.UserId);
                if (user == null || !user.Active)
                {
                    throw new BusinessException("Usuario no encontrado o inactivo");
                }

                // Update password
                user.PasswordHash = _passwordService.HashPassword(request.NewPassword);
                user.UpdatedAt = DateTime.UtcNow;

                // This would need to be added to AuthRepository or create a UserRepository method
                // For now, we'll assume we have this method in AuthRepository
                var passwordUpdated = await _userRepository.UpdateUserPasswordAsync(user, cancellationToken);
                if (!passwordUpdated)
                {
                    throw new BusinessException("Error al actualizar la contraseña");
                }

                // Mark token as used
                resetToken.MarkAsUsed(request.IpAddress);
                await _passwordResetRepository.UpdateAsync(resetToken);

                // Invalidate all refresh tokens
                await _refreshTokenRepository.RevokeAllByUserIdAsync(user.Id, "password_reset");

                // Send confirmation email
                await _emailService.SendPasswordResetConfirmationAsync(user.Email, user.Name);

                _logger.LogInformation("Password reset successfully for user {UserId}", user.Id);
                return true;
            }
            catch (BusinessException)
            {
                throw; // Re-throw business exceptions
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password with token {Token}", request.Token);
                throw new BusinessException("Error interno al restablecer la contraseña");
            }
        }

  
    }
}
