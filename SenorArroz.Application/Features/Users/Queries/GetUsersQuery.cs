using MediatR;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Users.Queries
{
    public record GetUsersQuery(
        int? BranchId = null,
        string? Role = null,
        bool? Active = null,
        int Page = 1,
        int PageSize = 10,
        string? SortBy = null,
        string SortOrder = "asc"
    ) : IRequest<PagedResult<UserDto>>;
}
