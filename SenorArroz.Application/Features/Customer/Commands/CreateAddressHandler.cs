using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Commands;

public class CreateAddressHandler : IRequestHandler<CreateAddressCommand, CustomerAddressDto>
{
    private readonly IAddressRepository _addressRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly INeighborhoodRepository _neighborhoodRepository;
    private readonly IMapper _mapper;

    public CreateAddressHandler(
        IAddressRepository addressRepository,
        ICustomerRepository customerRepository,
        INeighborhoodRepository neighborhoodRepository,
        IMapper mapper)
    {
        _addressRepository = addressRepository;
        _customerRepository = customerRepository;
        _neighborhoodRepository = neighborhoodRepository;
        _mapper = mapper;
    }

    public async Task<CustomerAddressDto> Handle(CreateAddressCommand request, CancellationToken cancellationToken)
    {
        // Validate customer exists
        if (!await _customerRepository.ExistsAsync(request.CustomerId))
        {
            throw new NotFoundException($"Cliente con ID {request.CustomerId} no encontrado");
        }

        // Validate neighborhood exists
        var neighborhood = await _neighborhoodRepository.GetByIdAsync(request.NeighborhoodId);
        if (neighborhood == null)
        {
            throw new NotFoundException($"Barrio con ID {request.NeighborhoodId} no encontrado");
        }

        // If this address should be primary, first unset all other primary addresses
        if (request.IsPrimary)
        {
            await _addressRepository.UnsetPrimaryAddressesAsync(request.CustomerId);
        }

        var address = new Address
        {
            CustomerId = request.CustomerId,
            NeighborhoodId = request.NeighborhoodId,
            AddressText = request.Address.Trim(),
            AdditionalInfo = request.AdditionalInfo?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            DeliveryFee = neighborhood.DeliveryFee,
            IsPrimary = request.IsPrimary
        };

        address = await _addressRepository.CreateAsync(address);

        return _mapper.Map<CustomerAddressDto>(address);
    }
}