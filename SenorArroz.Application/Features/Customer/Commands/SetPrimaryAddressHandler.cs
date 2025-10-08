using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Commands;

public class SetPrimaryAddressHandler : IRequestHandler<SetPrimaryAddressCommand, CustomerAddressDto>
{
    private readonly IAddressRepository _addressRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IMapper _mapper;

    public SetPrimaryAddressHandler(
        IAddressRepository addressRepository,
        ICustomerRepository customerRepository,
        IMapper mapper)
    {
        _addressRepository = addressRepository;
        _customerRepository = customerRepository;
        _mapper = mapper;
    }

    public async Task<CustomerAddressDto> Handle(SetPrimaryAddressCommand request, CancellationToken cancellationToken)
    {
        // Validate customer exists
        if (!await _customerRepository.ExistsAsync(request.CustomerId))
        {
            throw new NotFoundException($"Cliente con ID {request.CustomerId} no encontrado");
        }

        // Validate address exists and belongs to the customer
        var address = await _addressRepository.GetByIdAsync(request.AddressId);
        if (address == null)
        {
            throw new NotFoundException($"Dirección con ID {request.AddressId} no encontrada");
        }

        if (address.CustomerId != request.CustomerId)
        {
            throw new BusinessException("La dirección no pertenece al cliente especificado");
        }

        // First, unset all primary addresses for the customer
        await _addressRepository.UnsetPrimaryAddressesAsync(request.CustomerId);

        // Then set the specified address as primary
        var success = await _addressRepository.SetPrimaryAddressAsync(request.CustomerId, request.AddressId);
        
        if (!success)
        {
            throw new BusinessException("No se pudo establecer la dirección como primaria");
        }

        // Get the updated address to return
        var updatedAddress = await _addressRepository.GetByIdAsync(request.AddressId);
        
        return _mapper.Map<CustomerAddressDto>(updatedAddress);
    }
}
