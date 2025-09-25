using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Queries;

public class GetBranchByIdHandler : IRequestHandler<GetBranchByIdQuery, BranchDto?>
{
    private readonly IBranchRepository _branchRepository;
    private readonly INeighborhoodRepository _neighborhoodRepository;
    private readonly IMapper _mapper;

    public GetBranchByIdHandler(
        IBranchRepository branchRepository,
        INeighborhoodRepository neighborhoodRepository,
        IMapper mapper)
    {
        _branchRepository = branchRepository;
        _neighborhoodRepository = neighborhoodRepository;
        _mapper = mapper;
    }

    public async Task<BranchDto?> Handle(GetBranchByIdQuery request, CancellationToken cancellationToken)
    {
        var branch = await _branchRepository.GetByIdWithDetailsAsync(request.Id);
        if (branch == null)
            return null;

        var branchDto = _mapper.Map<BranchDto>(branch);
        
        // Add statistics
        branchDto.TotalUsers = await _branchRepository.GetTotalUsersAsync(branch.Id);
        branchDto.ActiveUsers = await _branchRepository.GetActiveUsersAsync(branch.Id);
        branchDto.TotalCustomers = await _branchRepository.GetTotalCustomersAsync(branch.Id);
        branchDto.ActiveCustomers = await _branchRepository.GetActiveCustomersAsync(branch.Id);
        branchDto.TotalNeighborhoods = await _branchRepository.GetTotalNeighborhoodsAsync(branch.Id);

        // Add neighborhood statistics
        foreach (var neighborhoodDto in branchDto.Neighborhoods)
        {
            neighborhoodDto.TotalCustomers = await _neighborhoodRepository.GetTotalCustomersAsync(neighborhoodDto.Id);
            neighborhoodDto.TotalAddresses = await _neighborhoodRepository.GetTotalAddressesAsync(neighborhoodDto.Id);
        }

        return branchDto;
    }
}