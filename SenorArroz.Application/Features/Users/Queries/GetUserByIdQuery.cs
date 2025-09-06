using MediatR;
using SenorArroz.Application.Features.Users.DTOs;

namespace SenorArroz.Application.Features.Users.Queries
{
    public record GetUserByIdQuery(int Id) : IRequest<UserDto?>
    {
        public int UserId { get; internal set; }
    }
}