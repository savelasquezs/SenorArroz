using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetCustomerAddressByIdHandler : IRequestHandler<GetCustomerAddressByIdQuery, CustomerAddressDto?>
{
    private readonly IAddressRepository _addressRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IMapper _mapper;

    public GetCustomerAddressByIdHandler(
        IAddressRepository addressRepository,
        ICustomerRepository customerRepository,
        IMapper mapper)
    {
        _addressRepository = addressRepository;
        _customerRepository = customerRepository;
        _mapper = mapper;
    }

    public async Task<CustomerAddressDto?> Handle(GetCustomerAddressByIdQuery request, CancellationToken cancellationToken)
    {
        // Get the address
        var address = await _addressRepository.GetByIdAsync(request.AddressId, cancellationToken);
        if (address == null)
        {
            return null;
        }

        // Verify customer exists and get branch info for security check
        var customer = await _customerRepository.GetByIdAsync(address.CustomerId, cancellationToken);
        if (customer == null)
        {
            throw new BusinessException("El cliente asociado a esta dirección no existe");
        }

        return _mapper.Map<CustomerAddressDto>(address);
    }
}

