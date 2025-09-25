using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Commands;

public class UpdateNeighborhoodHandler : IRequestHandler<UpdateNeighborhoodCommand, BranchNeighborhoodDto>
{
    private readonly INeighborhoodRepository _neighborhoodRepository;
    private readonly IMapper _mapper;

    public UpdateNeighborhoodHandler(INeighborhoodRepository neighborhoodRepository, IMapper mapper)
    {
        _neighborhoodRepository = neighborhoodRepository;
        _mapper = mapper;
    }

    public async Task<BranchNeighborhoodDto> Handle(UpdateNeighborhoodCommand request, CancellationToken cancellationToken)
    {
        var neighborhood = await _neighborhoodRepository.GetByIdAsync(request.Id);
        if (neighborhood == null)
        {
            throw new NotFoundException($"Barrio con ID {request.Id} no encontrado");
        }

        // Validate name doesn't exist for other neighborhoods in the same branch
        if (await _neighborhoodRepository.NameExistsAsync(request.Name, neighborhood.BranchId, request.Id))
        {
            throw new BusinessException($"Ya existe otro barrio con el nombre '{request.Name}' en esta sucursal");
        }

        // Update neighborhood
        neighborhood.Name = request.Name.Trim();
        neighborhood.DeliveryFee = request.DeliveryFee;

        neighborhood = await _neighborhoodRepository.UpdateAsync(neighborhood);

        var neighborhoodDto = _mapper.Map<BranchNeighborhoodDto>(neighborhood);

        // Add current statistics
        neighborhoodDto.TotalCustomers = await _neighborhoodRepository.GetTotalCustomersAsync(neighborhood.Id);
        neighborhoodDto.TotalAddresses = await _neighborhoodRepository.GetTotalAddressesAsync(neighborhood.Id);

        return neighborhoodDto;
    }
}