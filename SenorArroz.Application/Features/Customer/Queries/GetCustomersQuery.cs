using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.Queries
{
    public class GetCustomersQuery : IRequest<PagedResult<CustomerDto>>
    {
        public int BranchId { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public bool? Active { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string SortBy { get; set; } = "name";
        public string SortOrder { get; set; } = "asc";
    }

}
