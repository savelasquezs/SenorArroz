using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Auth.DTOs;


namespace SenorArroz.Application.Features.Auth.Commands
{
    public class LoginCommand : IRequest<AuthResponseDto>
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? DeviceInstallationId { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public DeliveryAppClientVersion? DeliveryAppVersion { get; set; }
        public bool IsWebClient { get; set; }
    }
}
