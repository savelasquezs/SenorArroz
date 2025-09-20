using MediatR;

namespace SenorArroz.Application.Features.Auth.Commands
{
    public class ForgotPasswordCommand : IRequest<bool>
    {
        public string Email { get; set; } = string.Empty;
        public string ResetUrl { get; set; } = string.Empty;
    }
}