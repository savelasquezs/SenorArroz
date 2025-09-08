using MediatR;
using SenorArroz.Application.Features.Auth.DTOs;


namespace SenorArroz.Application.Features.Auth.Commands;

public class RefreshTokenCommand : IRequest<AuthResponseDto>
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}