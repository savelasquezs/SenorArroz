using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;


namespace SenorArroz.Application.Features.Branches.Queries;

public class GetBranchByIdHandler(
    IBranchRepository branchRepository,
    INeighborhoodRepository neighborhoodRepository,
    IMapper mapper, ICurrentUser currentUser) : IRequestHandler<GetBranchByIdQuery, BranchDto?>
{
    private readonly IBranchRepository _branchRepository = branchRepository;
    private readonly INeighborhoodRepository _neighborhoodRepository = neighborhoodRepository;
    private readonly IMapper _mapper = mapper;
    private readonly ICurrentUser _currentUser=currentUser;

    public async Task<BranchDto?> Handle(GetBranchByIdQuery request, CancellationToken cancellationToken)
    {
        var branch = await _branchRepository.GetByIdWithDetailsAsync(request.Id);
        if (branch == null)
            return null;

        if (_currentUser.Role == "admin")
        {
            branch.Users = [..branch.Users.Where(u => u.Role !=UserRole.Superadmin )];
        }
            branch.Users=[..branch.Users.Where(u => u.Id != _currentUser.Id)];


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