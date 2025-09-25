using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Queries;

public class GetBranchStatsHandler : IRequestHandler<GetBranchStatsQuery, BranchStatsDto>
{
    private readonly IBranchRepository _branchRepository;

    public GetBranchStatsHandler(IBranchRepository branchRepository)
    {
        _branchRepository = branchRepository;
    }

    public async Task<BranchStatsDto> Handle(GetBranchStatsQuery request, CancellationToken cancellationToken)
    {
        var branch = await _branchRepository.GetByIdAsync(request.BranchId);
        if (branch == null)
            throw new NotFoundException($"Sucursal con ID {request.BranchId} no encontrada");


        return new BranchStatsDto
        {
            BranchId = branch.Id,
            BranchName = branch.Name,

            // User Statistics
            TotalUsers = await _branchRepository.GetTotalUsersAsync(request.BranchId),
            ActiveUsers = await _branchRepository.GetActiveUsersAsync(request.BranchId),
        

            // Customer Statistics
            TotalCustomers = await _branchRepository.GetTotalCustomersAsync(request.BranchId),
            ActiveCustomers = await _branchRepository.GetActiveCustomersAsync(request.BranchId),
            CustomersThisMonth = await _branchRepository.GetCustomersThisMonthAsync(request.BranchId),

            // Operational Statistics
            TotalNeighborhoods = await _branchRepository.GetTotalNeighborhoodsAsync(request.BranchId),
            TotalOrders = await _branchRepository.GetTotalOrdersAsync(request.BranchId),
            OrdersThisMonth = await _branchRepository.GetOrdersThisMonthAsync(request.BranchId),

            // Financial Statistics
    
        };
    }
}