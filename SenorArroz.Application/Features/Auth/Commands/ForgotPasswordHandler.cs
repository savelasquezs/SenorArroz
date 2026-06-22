using MediatR;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Auth.Commands
{
    public class ForgotPasswordHandler(
        IAuthRepository authRepository,
        IPasswordResetRepository passwordResetRepository,
        IEmailService emailService,
        ILogger<ForgotPasswordHandler> logger,
        IClock clock) : IRequestHandler<ForgotPasswordCommand, bool>
    {
        private readonly IAuthRepository _authRepository = authRepository;
        private readonly IPasswordResetRepository _passwordResetRepository = passwordResetRepository;
        private readonly IEmailService _emailService = emailService;
        private readonly ILogger<ForgotPasswordHandler> _logger = logger;
        private readonly IClock _clock = clock;

        public async Task<bool> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
        {
            try
            {
                // Find user by email
                var user = await _authRepository.GetUserByEmailAsync(request.Email, cancellationToken);
                if (user == null || !user.Active)
                {
                    // Don't reveal if user exists for security
                    _logger.LogWarning("Password reset requested for non-existent user: {Email}", request.Email);
                    return true; // Return true to prevent email enumeration
                }

                // Invalidate existing tokens
                await _passwordResetRepository.InvalidateAllUserTokensAsync(user.Id, cancellationToken);

                // Create new reset token
                var resetToken = PasswordResetToken.Create(user.Id, request.Email, expirationMinutes: 60, _clock.UtcNow);
                await _passwordResetRepository.CreateAsync(resetToken, cancellationToken);

                // Send email
                var emailResult = await _emailService.SendPasswordResetEmailAsync(
                    request.Email,
                    user.Name,
                    resetToken.Token,
                    request.ResetUrl);

                if (emailResult.Success)
                {
                    _logger.LogInformation("Password reset email queued successfully for {Email}", request.Email);
                }
                else
                {
                    _logger.LogError(
                        "Failed to queue password reset email for {Email}. Provider: {Provider}. Error: {Error}",
                        request.Email,
                        emailResult.Provider,
                        emailResult.ErrorMessage);
                }

                return emailResult.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing forgot password request for {Email}", request.Email);
                return false;
            }
        }
    }
}
