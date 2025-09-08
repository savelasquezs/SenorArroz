using MediatR;

namespace SenorArroz.Application.Features.Auth.Commands;

public class LogoutCommand : IRequest<bool>
{
    public string RefreshToken { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}