using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Commands;

public class UpdateAddressHandler : IRequestHandler<UpdateAddressCommand, CustomerAddressDto>
{
    private readonly IAddressRepository _addressRepository;
    private readonly INeighborhoodRepository _neighborhoodRepository;
    private readonly IMapper _mapper;

    public UpdateAddressHandler(
        IAddressRepository addressRepository,
        INeighborhoodRepository neighborhoodRepository,
        IMapper mapper)
    {
        _addressRepository = addressRepository;
        _neighborhoodRepository = neighborhoodRepository;
        _mapper = mapper;
    }

    public async Task<CustomerAddressDto> Handle(UpdateAddressCommand request, CancellationToken cancellationToken)
    {
        var address = await _addressRepository.GetByIdAsync(request.Id);
        if (address == null)
        {
            throw new NotFoundException($"Dirección con ID {request.Id} no encontrada");
        }

        // Validate neighborhood exists
        var neighborhood = await _neighborhoodRepository.GetByIdAsync(request.NeighborhoodId);
        if (neighborhood == null)
        {
            throw new NotFoundException($"Barrio con ID {request.NeighborhoodId} no encontrado");
        }

        // Update address
        address.NeighborhoodId = request.NeighborhoodId;
        address.AddressText = request.Address.Trim();
        address.AdditionalInfo = request.AdditionalInfo?.Trim();
        address.Latitude = request.Latitude;
        address.Longitude = request.Longitude;

        address = await _addressRepository.UpdateAsync(address);

        return _mapper.Map<CustomerAddressDto>(address);
    }
}