using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.Queries
{
    public class GetCustomerByIdHandler : IRequestHandler<GetCustomerByIdQuery, CustomerDto?>
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMapper _mapper;
        private readonly ICurrentUser _currentUser;
        private readonly ILoyaltyCycleService _loyaltyCycle;

        public GetCustomerByIdHandler(
            ICustomerRepository customerRepository,
            IMapper mapper,
            ICurrentUser currentUser,
            ILoyaltyCycleService loyaltyCycle)
        {
            _customerRepository = customerRepository;
            _mapper = mapper;
            _currentUser = currentUser;
            _loyaltyCycle = loyaltyCycle;
        }

        public async Task<CustomerDto?> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
        {
            var customer = await _customerRepository.GetByIdWithAddressesAsync(request.Id, cancellationToken);
            if (customer == null)
                return null;

            // The repository already scopes the customer to the resolved tenant.
            // BranchId is only the origin branch, so it must not block a POS user
            // from reading a customer selected by global mobile identity.

            var customerDto = _mapper.Map<CustomerDto>(customer);

            var orderStatistics = await _customerRepository.GetOrderStatisticsForCustomersAsync(
                new[] { customer.Id }, cancellationToken);
            orderStatistics.TryGetValue(customer.Id, out var stats);
            customerDto.ApplyOrderStatistics(stats);
            await _loyaltyCycle.ApplyLoyaltyPreviewsToCustomerDtosAsync(new[] { customerDto }, cancellationToken);

            return customerDto;
        }
    }

}
