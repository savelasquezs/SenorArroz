using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Branches.Queries;

public class GetBranchesHandler : IRequestHandler<GetBranchesQuery, PagedResult<BranchDto>>
{
    private readonly IBranchRepository _branchRepository;
    private readonly IMapper _mapper;

    public GetBranchesHandler(IBranchRepository branchRepository, IMapper mapper)
    {
        _branchRepository = branchRepository;
        _mapper = mapper;
    }

    public async Task<PagedResult<BranchDto>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        var pagedBranches = await _branchRepository.GetPagedAsync(
            request.Name,
            request.Address,
            request.Phone,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder,
            cancellationToken);

        var branches = pagedBranches.Items.ToList();
        var stats = await _branchRepository.GetListStatisticsForBranchesAsync(
            branches.Select(b => b.Id).ToArray(), cancellationToken);
        var branchDtos = new List<BranchDto>(branches.Count);
        foreach (var branch in branches)
        {
            var branchDto = _mapper.Map<BranchDto>(branch);
            stats.TryGetValue(branch.Id, out var counts);
            branchDto.TotalUsers = counts?.TotalUsers ?? 0;
            branchDto.ActiveUsers = counts?.ActiveUsers ?? 0;
            branchDto.TotalCustomers = counts?.TotalCustomers ?? 0;
            branchDto.ActiveCustomers = counts?.ActiveCustomers ?? 0;
            branchDto.TotalNeighborhoods = counts?.TotalNeighborhoods ?? 0;
            branchDtos.Add(branchDto);
        }

        return new PagedResult<BranchDto>
        {
            Items = branchDtos,
            TotalCount = pagedBranches.TotalCount,
            Page = pagedBranches.Page,
            PageSize = pagedBranches.PageSize,
            TotalPages = pagedBranches.TotalPages
        };
    }
}
