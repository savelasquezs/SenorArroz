using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;

namespace SenorArroz.Application.Features.Branches.Queries;

public class GetBranchByIdQuery : IRequest<BranchDto?>
{
    public int Id { get; set; }
}
