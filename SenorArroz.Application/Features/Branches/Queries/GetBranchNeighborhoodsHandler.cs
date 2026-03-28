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
        var neighborhoodList = neighborhoods.ToList();
        var hoodStats = await _neighborhoodRepository.GetNeighborhoodStatsBulkAsync(
            neighborhoodList.Select(n => n.Id).ToList(),
            cancellationToken);

        var neighborhoodDtos = new List<BranchNeighborhoodDto>(neighborhoodList.Count);
        foreach (var neighborhood in neighborhoodList)
        {
            var dto = _mapper.Map<BranchNeighborhoodDto>(neighborhood);
            var (customers, addresses) = hoodStats[neighborhood.Id];
            dto.TotalCustomers = customers;
            dto.TotalAddresses = addresses;
            neighborhoodDtos.Add(dto);
        }

        return neighborhoodDtos;
    }
}