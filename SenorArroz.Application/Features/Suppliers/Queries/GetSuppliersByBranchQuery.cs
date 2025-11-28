using MediatR;
using SenorArroz.Application.Features.Suppliers.DTOs;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSuppliersByBranchQuery : IRequest<List<SupplierDto>>
{
    public int? BranchId { get; set; }
}


