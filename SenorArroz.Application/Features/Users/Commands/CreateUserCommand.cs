using MediatR;
using SenorArroz.Application.Features.Users.DTOs;

namespace SenorArroz.Application.Features.Users.Commands
{
    public record CreateUserCommand(CreateUserDto UserData) : IRequest<UserDto>;
}