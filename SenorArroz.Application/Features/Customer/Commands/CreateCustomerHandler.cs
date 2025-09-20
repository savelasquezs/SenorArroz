using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;


namespace SenorArroz.Application.Features.Customers.Commands
{
    public class CreateCustomerHandler : IRequestHandler<CreateCustomerCommand, CustomerDto>
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IAddressRepository _addressRepository;
        private readonly INeighborhoodRepository _neighborhoodRepository;
        private readonly IMapper _mapper;

        public CreateCustomerHandler(
            ICustomerRepository customerRepository,
            IAddressRepository addressRepository,
            INeighborhoodRepository neighborhoodRepository,
            IMapper mapper)
        {
            _customerRepository = customerRepository;
            _addressRepository = addressRepository;
            _neighborhoodRepository = neighborhoodRepository;
            _mapper = mapper;
        }

        public async Task<CustomerDto> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
        {
            // Validate phone doesn't exist
            if (await _customerRepository.PhoneExistsAsync(request.Phone1, request.BranchId))
            {
                throw new BusinessException($"Ya existe un cliente con el teléfono {request.Phone1} en esta sucursal");
            }

            if (!string.IsNullOrEmpty(request.Phone2) &&
                await _customerRepository.PhoneExistsAsync(request.Phone2, request.BranchId))
            {
                throw new BusinessException($"Ya existe un cliente con el teléfono {request.Phone2} en esta sucursal");
            }

            // Create customer
            var customer = new Customer
            {
                Name = request.Name.Trim(),
                Phone1 = request.Phone1,
                Phone2 = request.Phone2,
                BranchId = request.BranchId,
                Active = true
            };

            customer = await _customerRepository.CreateAsync(customer);

            // Create initial address if provided
            if (request.InitialAddress != null)
            {
                // Validate neighborhood exists
                var neighborhood = await _neighborhoodRepository.GetByIdAsync(request.InitialAddress.NeighborhoodId);
                if (neighborhood == null)
                {
                    throw new NotFoundException($"Barrio con ID {request.InitialAddress.NeighborhoodId} no encontrado");
                }

                var address = new Address
                {
                    CustomerId = customer.Id,
                    NeighborhoodId = request.InitialAddress.NeighborhoodId,
                    AddressText = request.InitialAddress.Address.Trim(),
                    AdditionalInfo = request.InitialAddress.AdditionalInfo?.Trim(),
                    Latitude = request.InitialAddress.Latitude,
                    Longitude = request.InitialAddress.Longitude,
                    DeliveryFee = neighborhood.DeliveryFee
                };

                await _addressRepository.CreateAsync(address);
            }

            // Return complete customer with addresses
            var createdCustomer = await _customerRepository.GetByIdWithAddressesAsync(customer.Id);
            var customerDto = _mapper.Map<CustomerDto>(createdCustomer);

            // Add additional data
            customerDto.TotalOrders = 0; // New customer has no orders
            customerDto.LastOrderDate = null;

            return customerDto;
        }
    }
}
