using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Common.Helpers;
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
        private readonly ILoyaltyCycleService _loyaltyCycle;
        private readonly ICurrentTenant _currentTenant;

        public CreateCustomerHandler(
            ICustomerRepository customerRepository,
            IAddressRepository addressRepository,
            INeighborhoodRepository neighborhoodRepository,
            IMapper mapper,
            ILoyaltyCycleService loyaltyCycle,
            ICurrentTenant currentTenant)
        {
            _customerRepository = customerRepository;
            _addressRepository = addressRepository;
            _neighborhoodRepository = neighborhoodRepository;
            _mapper = mapper;
            _loyaltyCycle = loyaltyCycle;
            _currentTenant = currentTenant;
        }

        public async Task<CustomerDto> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
        {
            var phone1 = string.IsNullOrWhiteSpace(request.Phone1) ? null : ColombianPhoneNormalizer.NormalizeCustomerContact(request.Phone1);
            var phone2 = string.IsNullOrWhiteSpace(request.Phone2) ? null : ColombianPhoneNormalizer.NormalizeCustomerContact(request.Phone2);
            if (phone1 is not null && phone2 == phone1)
                phone2 = null;
            if (request.InitialAddress is not null)
                await ValidateInitialAddressAsync(request.InitialAddress, cancellationToken);

            // Only mobile numbers identify a person. A shared 604 landline stays
            // searchable in the POS legacy fields but never reuses or merges customers.
            var existing = phone1 is not null && ColombianPhoneNormalizer.TryNormalize(phone1, out _)
                ? await _customerRepository.GetByPhoneAsync(phone1, _currentTenant.TenantId, cancellationToken)
                : phone2 is not null && ColombianPhoneNormalizer.TryNormalize(phone2, out _)
                    ? await _customerRepository.GetByPhoneAsync(phone2, _currentTenant.TenantId, cancellationToken)
                    : null;
            if (existing is not null)
            {
                var existingWithAddresses = await _customerRepository.GetByIdWithAddressesAsync(existing.Id, cancellationToken);
                if (request.InitialAddress is not null && existingWithAddresses is not null)
                {
                    var initialAddress = request.InitialAddress;
                    var alreadyExists = existingWithAddresses.Addresses.Any(address =>
                        address.NeighborhoodId == initialAddress.NeighborhoodId
                        && string.Equals(address.AddressText.Trim(), initialAddress.Address.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (!alreadyExists)
                    {
                        if (initialAddress.IsPrimary)
                            await _addressRepository.UnsetPrimaryAddressesAsync(existing.Id, cancellationToken);
                        await _addressRepository.CreateAsync(ToAddress(existing, initialAddress), cancellationToken);
                        existingWithAddresses = await _customerRepository.GetByIdWithAddressesAsync(existing.Id, cancellationToken);
                    }
                }

                var reused = _mapper.Map<CustomerDto>(existingWithAddresses);
                reused.WasCreated = false;
                return reused;
            }

            // Create customer
            var customer = new Customer
            {
                Name = request.Name.Trim(),
                Phone1 = phone1,
                Phone2 = phone2,
                WhatsAppUsername = WhatsAppIdentityNormalizer.NormalizeUsername(request.WhatsAppUsername),
                BranchId = request.BranchId,
                TenantId = _currentTenant.TenantId,
                Active = true
            };
            if (phone1 is not null)
                customer.Phones.Add(new CustomerPhone { TenantId = customer.TenantId, PhoneNormalized = phone1, IsPrimary = true });
            if (phone2 is not null)
                customer.Phones.Add(new CustomerPhone { TenantId = customer.TenantId, PhoneNormalized = phone2, IsPrimary = phone1 is null });

            var candidate = customer;
            customer = await _customerRepository.CreateAsync(customer, cancellationToken);
            var wasCreated = ReferenceEquals(candidate, customer);

            // Create initial address if provided
            if (request.InitialAddress != null)
            {
                await _addressRepository.CreateAsync(ToAddress(customer, request.InitialAddress), cancellationToken);
            }

            // Return complete customer with addresses
            var createdCustomer = await _customerRepository.GetByIdWithAddressesAsync(customer.Id, cancellationToken);
            var customerDto = _mapper.Map<CustomerDto>(createdCustomer);
            customerDto.WasCreated = wasCreated;

            // Add additional data
            customerDto.TotalOrders = 0;
            customerDto.FirstOrderDate = null;
            customerDto.LastOrderDate = null;
            customerDto.TotalAccumulated = 0;
            await _loyaltyCycle.ApplyLoyaltyPreviewToCustomerDtoAsync(customerDto, cancellationToken);

            return customerDto;
        }

        private async Task ValidateInitialAddressAsync(CreateCustomerAddressDto initialAddress, CancellationToken cancellationToken)
        {
            var neighborhood = await _neighborhoodRepository.GetByIdAsync(initialAddress.NeighborhoodId, cancellationToken);
            if (neighborhood is null)
                throw new NotFoundException($"Barrio con ID {initialAddress.NeighborhoodId} no encontrado");
            if (neighborhood.TenantId != _currentTenant.TenantId)
                throw new BusinessException("El barrio y el cliente pertenecen a restaurantes diferentes");
            if (!neighborhood.Active || neighborhood.Branch is null || !neighborhood.Branch.IsActive)
                throw new BusinessException("El barrio seleccionado no está disponible");
        }

        private static Address ToAddress(Customer customer, CreateCustomerAddressDto initialAddress) => new()
        {
            CustomerId = customer.Id,
            TenantId = customer.TenantId,
            NeighborhoodId = initialAddress.NeighborhoodId,
            AddressText = initialAddress.Address.Trim(),
            AdditionalInfo = initialAddress.AdditionalInfo?.Trim(),
            Latitude = initialAddress.Latitude,
            Longitude = initialAddress.Longitude,
            DeliveryFee = initialAddress.DeliveryFee,
            IsPrimary = initialAddress.IsPrimary
        };
    }
}
