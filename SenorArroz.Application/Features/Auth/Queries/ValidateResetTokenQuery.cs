using MediatR;
using SenorArroz.Application.Features.Auth.DTOs;


namespace SenorArroz.Application.Features.Auth.Queries;

public class ValidateResetTokenQuery : IRequest<ResetTokenValidationResult>
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
