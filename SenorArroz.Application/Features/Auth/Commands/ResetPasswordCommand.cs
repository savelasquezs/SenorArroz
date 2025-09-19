using MediatR;

namespace SenorArroz.Application.Features.Auth.Commands;

public class ResetPasswordCommand : IRequest<bool>
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}