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
        var branch = await _branchRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken);
        if (branch == null)
            return null;

        var users = branch.Users ?? [];
        if (Roles.IsAdmin(_currentUser.Role))
        {
            users = [.. users.Where(u => u.Role != UserRole.Superadmin)];
        }
        branch.Users = [.. users.Where(u => u.Id != _currentUser.Id)];

        var branchDto = _mapper.Map<BranchDto>(branch);

        // Add statistics
        branchDto.TotalUsers = await _branchRepository.GetTotalUsersAsync(branch.Id, cancellationToken);
        branchDto.ActiveUsers = await _branchRepository.GetActiveUsersAsync(branch.Id, cancellationToken);
        branchDto.TotalCustomers = await _branchRepository.GetTotalCustomersAsync(branch.Id, cancellationToken);
        branchDto.ActiveCustomers = await _branchRepository.GetActiveCustomersAsync(branch.Id, cancellationToken);
        branchDto.TotalNeighborhoods = await _branchRepository.GetTotalNeighborhoodsAsync(branch.Id, cancellationToken);

        var hoodIds = branchDto.Neighborhoods.Select(n => n.Id).ToList();
        var hoodStats = await _neighborhoodRepository.GetNeighborhoodStatsBulkAsync(hoodIds, cancellationToken);
        foreach (var neighborhoodDto in branchDto.Neighborhoods)
        {
            var (customers, addresses) = hoodStats[neighborhoodDto.Id];
            neighborhoodDto.TotalCustomers = customers;
            neighborhoodDto.TotalAddresses = addresses;
        }

        return branchDto;
    }
}
