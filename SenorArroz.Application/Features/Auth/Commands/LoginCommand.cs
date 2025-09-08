using MediatR;
using SenorArroz.Application.Features.Auth.DTOs;


namespace SenorArroz.Application.Features.Auth.Commands;

public class LoginCommand : IRequest<AuthResponseDto>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}