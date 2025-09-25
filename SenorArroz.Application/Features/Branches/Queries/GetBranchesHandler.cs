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
            request.SortOrder);

        var branchDtos = new List<BranchDto>();

        foreach (var branch in pagedBranches.Items)
        {
            var branchDto = _mapper.Map<BranchDto>(branch);

            // Add statistics
            branchDto.TotalUsers = await _branchRepository.GetTotalUsersAsync(branch.Id);
            branchDto.ActiveUsers = await _branchRepository.GetActiveUsersAsync(branch.Id);
            branchDto.TotalCustomers = await _branchRepository.GetTotalCustomersAsync(branch.Id);
            branchDto.ActiveCustomers = await _branchRepository.GetActiveCustomersAsync(branch.Id);
            branchDto.TotalNeighborhoods = await _branchRepository.GetTotalNeighborhoodsAsync(branch.Id);

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