using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetNeighborhoodsHandler : IRequestHandler<GetNeighborhoodsQuery, IEnumerable<Neighborhood>>
{
    private readonly INeighborhoodRepository _neighborhoodRepository;
    private readonly ICurrentUser _currentUser;

    public GetNeighborhoodsHandler(INeighborhoodRepository neighborhoodRepository, ICurrentUser currentUser)
    {
        _neighborhoodRepository = neighborhoodRepository;
        _currentUser = currentUser;
    }

    public async Task<IEnumerable<Neighborhood>> Handle(GetNeighborhoodsQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        int branchFilter = Roles.IsSuperadmin(_currentUser.Role) ? request.BranchId : _currentUser.BranchId;

        // Additional validation for non-superadmin users
        if (!Roles.IsSuperadmin(_currentUser.Role) && request.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No tienes permisos para acceder a barrios de otras sucursales");
        }

        return await _neighborhoodRepository.GetByBranchIdAsync(branchFilter, cancellationToken);
    }
}
