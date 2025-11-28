using MediatR;
using SenorArroz.Application.Features.Suppliers.DTOs;

namespace SenorArroz.Application.Features.Suppliers.Commands;

public class CreateSupplierCommand : IRequest<SupplierDto>
{
    public CreateSupplierDto Supplier { get; set; } = new();
    public int? BranchId { get; set; }
}


