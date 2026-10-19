using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.Queries
{
    public class GetCustomersHandler : IRequestHandler<GetCustomersQuery, PagedResult<CustomerDto>>
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMapper _mapper;
        private readonly ICurrentUser _currentUser;
        private readonly ILoyaltyCycleService _loyaltyCycle;

        public GetCustomersHandler(
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

        public async Task<PagedResult<CustomerDto>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
        {
            // Determine branch filter based on user role
            int? branchFilter = null;
            if (!Roles.IsSuperadmin(_currentUser.Role))
            {
                branchFilter = _currentUser.BranchId;
            }
            else if (request.BranchId > 0)
            {
                // Superadmin can optionally filter by specific branch
                branchFilter = request.BranchId;
            }
            // If branchFilter is null, superadmin gets all customers from all branches

            var pagedCustomers = await _customerRepository.GetPagedAsync(
                branchFilter,
                request.Search,
                request.Name,
                request.Phone,
                request.WhatsAppUsername,
                request.Active,
                request.Page,
                request.PageSize,
                request.SortBy,
                request.SortOrder,
                cancellationToken);

            var customers = pagedCustomers.Items.ToList();
            var orderStats = await _customerRepository.GetOrderStatisticsForCustomersAsync(
                customers.Select(customer => customer.Id).ToArray(), cancellationToken);
            var customerDtos = new List<CustomerDto>(customers.Count);

            foreach (var customer in customers)
            {
                var customerDto = _mapper.Map<CustomerDto>(customer);
                if (orderStats.TryGetValue(customer.Id, out var stats))
                {
                    customerDto.TotalOrders = stats.TotalOrders;
                    customerDto.FirstOrderDate = stats.FirstOrderDate;
                    customerDto.LastOrderDate = stats.LastOrderDate;
                    customerDto.TotalAccumulated = stats.TotalAccumulated;
                }
                else
                {
                    customerDto.TotalOrders = 0;
                    customerDto.FirstOrderDate = null;
                    customerDto.LastOrderDate = null;
                    customerDto.TotalAccumulated = 0;
                }

                await _loyaltyCycle.ApplyLoyaltyPreviewToCustomerDtoAsync(customerDto, cancellationToken);
                customerDtos.Add(customerDto);
            }

            return new PagedResult<CustomerDto>
            {
                Items = customerDtos,
                TotalCount = pagedCustomers.TotalCount,
                Page = pagedCustomers.Page,
                PageSize = pagedCustomers.PageSize,
                TotalPages = pagedCustomers.TotalPages
            };
        }
    }
}
