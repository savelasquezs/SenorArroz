using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetNeighborhoodsHandler : IRequestHandler<GetNeighborhoodsQuery, IEnumerable<Neighborhood>>
{
    private readonly INeighborhoodRepository _neighborhoodRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly ICurrentUser _currentUser;

    public GetNeighborhoodsHandler(
        INeighborhoodRepository neighborhoodRepository,
        IBranchRepository branchRepository,
        ICurrentUser currentUser)
    {
        _neighborhoodRepository = neighborhoodRepository;
        _branchRepository = branchRepository;
        _currentUser = currentUser;
    }

    public async Task<IEnumerable<Neighborhood>> Handle(GetNeighborhoodsQuery request, CancellationToken cancellationToken)
    {
        var canUseRequestedBranch = request.ForOrderAddress
            ? Roles.IsSuperadminOrAdminOrCashier(_currentUser.Role)
            : Roles.IsSuperadmin(_currentUser.Role);
        if (!canUseRequestedBranch && request.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No tienes permisos para acceder a barrios de otras sucursales");
        }

        var branchFilter = canUseRequestedBranch ? request.BranchId : _currentUser.BranchId;
        var branch = await _branchRepository.GetByIdAsync(branchFilter, cancellationToken);
        if (branch is null || !branch.IsActive)
            throw new BusinessException("La sucursal seleccionada no está disponible");

        var neighborhoods = await _neighborhoodRepository.GetByBranchIdAsync(branchFilter, cancellationToken);
        return neighborhoods.Where(neighborhood => neighborhood.Active);
    }
}
