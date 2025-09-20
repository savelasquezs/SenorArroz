using MediatR;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetNeighborhoodsHandler : IRequestHandler<GetNeighborhoodsQuery, IEnumerable<Neighborhood>>
{
    private readonly INeighborhoodRepository _neighborhoodRepository;

    public GetNeighborhoodsHandler(INeighborhoodRepository neighborhoodRepository)
    {
        _neighborhoodRepository = neighborhoodRepository;
    }

    public async Task<IEnumerable<Neighborhood>> Handle(GetNeighborhoodsQuery request, CancellationToken cancellationToken)
    {
        return await _neighborhoodRepository.GetByBranchIdAsync(request.BranchId);
    }
}