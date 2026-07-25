using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Queries;

public sealed class GetBranchOptionsHandler
    : IRequestHandler<GetBranchOptionsQuery, IReadOnlyList<BranchOptionDto>>
{
    private readonly IBranchRepository _branchRepository;

    public GetBranchOptionsHandler(IBranchRepository branchRepository)
    {
        _branchRepository = branchRepository;
    }

    public async Task<IReadOnlyList<BranchOptionDto>> Handle(
        GetBranchOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var branches = await _branchRepository.GetAllAsync(cancellationToken);
        return branches
            .Select(branch => new BranchOptionDto { Id = branch.Id, Name = branch.Name })
            .OrderBy(branch => branch.Name)
            .ToList();
    }
}
