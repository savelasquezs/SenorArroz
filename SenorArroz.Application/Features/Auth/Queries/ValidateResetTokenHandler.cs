using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Auth.Queries
{
    public class ValidateResetTokenHandler : IRequestHandler<ValidateResetTokenQuery, ResetTokenValidationResult>
    {
        private readonly IPasswordResetRepository _passwordResetRepository;
        private readonly IClock _clock;

        public ValidateResetTokenHandler(IPasswordResetRepository passwordResetRepository, IClock clock)
        {
            _passwordResetRepository = passwordResetRepository;
            _clock = clock;
        }

        public async Task<ResetTokenValidationResult> Handle(ValidateResetTokenQuery request, CancellationToken cancellationToken)
        {
            var resetToken = await _passwordResetRepository.GetByTokenAsync(request.Token, cancellationToken);

            if (resetToken == null)
            {
                return new ResetTokenValidationResult
                {
                    IsValid = false,
                    Message = "Token de recuperación no encontrado"
                };
            }

            if (!string.Equals(resetToken.Email, request.Email, StringComparison.OrdinalIgnoreCase))
            {
                return new ResetTokenValidationResult
                {
                    IsValid = false,
                    Message = "El email no coincide con el token de recuperación"
                };
            }

            if (resetToken.IsUsed)
            {
                return new ResetTokenValidationResult
                {
                    IsValid = false,
                    Message = "Este token ya ha sido utilizado"
                };
            }

            if (resetToken.IsExpiredAt(_clock.UtcNow))
            {
                return new ResetTokenValidationResult
                {
                    IsValid = false,
                    Message = "El token de recuperación ha expirado"
                };
            }

            return new ResetTokenValidationResult
            {
                IsValid = true,
                Message = "Token válido",
                UserName = resetToken.User.Name,
                ExpiresAt = resetToken.ExpiresAt
            };
        }
    }
}
