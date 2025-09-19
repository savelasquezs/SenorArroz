using MediatR;
using Microsoft.Extensions.Logging;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Auth.Commands;

public class ForgotPasswordHandler(
    IAuthRepository authRepository,
    IPasswordResetRepository passwordResetRepository,
    IEmailService emailService,
    ILogger<ForgotPasswordHandler> logger) : IRequestHandler<ForgotPasswordCommand, bool>
{
    private readonly IAuthRepository _authRepository = authRepository;
    private readonly IPasswordResetRepository _passwordResetRepository = passwordResetRepository;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger<ForgotPasswordHandler> _logger = logger;

    public async Task<bool> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Find user by email
            var user = await _authRepository.GetUserByEmailAsync(request.Email);
            if (user == null || !user.Active)
            {
                // Don't reveal if user exists for security
                _logger.LogWarning("Password reset requested for non-existent user: {Email}", request.Email);
                return true; // Return true to prevent email enumeration
            }

            // Invalidate existing tokens
            await _passwordResetRepository.InvalidateAllUserTokensAsync(user.Id);

            // Create new reset token
            var resetToken = PasswordResetToken.Create(user.Id, request.Email, expirationMinutes: 60);
            await _passwordResetRepository.CreateAsync(resetToken);

            // Send email
            var emailSent = await _emailService.SendPasswordResetEmailAsync(
                request.Email,
                user.Name,
                resetToken.Token,
                request.ResetUrl);

            if (emailSent)
            {
                _logger.LogInformation("Password reset email sent successfully to {Email}", request.Email);
            }
            else
            {
                _logger.LogError("Failed to send password reset email to {Email}", request.Email);
            }

            return emailSent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing forgot password request for {Email}", request.Email);
            return false;
        }
    }
}