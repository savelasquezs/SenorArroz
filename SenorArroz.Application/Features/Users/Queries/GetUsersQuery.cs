using MediatR;
using SenorArroz.Application.Features.Users.DTOs;

namespace SenorArroz.Application.Features.Users.Queries
{
    public record GetUsersQuery(int? BranchId = null) : IRequest<List<UserDto>>;
}