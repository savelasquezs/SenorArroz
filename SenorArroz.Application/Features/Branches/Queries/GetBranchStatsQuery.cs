using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;

namespace SenorArroz.Application.Features.Branches.Queries;

public class GetBranchStatsQuery : IRequest<BranchStatsDto>
{
    public int BranchId { get; set; }
}
