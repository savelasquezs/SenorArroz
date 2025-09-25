using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Queries;

public class GetBranchNeighborhoodsHandler : IRequestHandler<GetBranchNeighborhoodsQuery, IEnumerable<BranchNeighborhoodDto>>
{
    private readonly INeighborhoodRepository _neighborhoodRepository;
    private readonly IMapper _mapper;

    public GetBranchNeighborhoodsHandler(INeighborhoodRepository neighborhoodRepository, IMapper mapper)
    {
        _neighborhoodRepository = neighborhoodRepository;
        _mapper = mapper;
    }

    public async Task<IEnumerable<BranchNeighborhoodDto>> Handle(GetBranchNeighborhoodsQuery request, CancellationToken cancellationToken)
    {
        var neighborhoods = await _neighborhoodRepository.GetByBranchIdAsync(request.BranchId);
        var neighborhoodDtos = new List<BranchNeighborhoodDto>();

        foreach (var neighborhood in neighborhoods)
        {
            var dto = _mapper.Map<BranchNeighborhoodDto>(neighborhood);

            // Add statistics
            dto.TotalCustomers = await _neighborhoodRepository.GetTotalCustomersAsync(neighborhood.Id);
            dto.TotalAddresses = await _neighborhoodRepository.GetTotalAddressesAsync(neighborhood.Id);

            neighborhoodDtos.Add(dto);
        }

        return neighborhoodDtos;
    }
}