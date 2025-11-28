using MediatR;
using SenorArroz.Application.Features.Suppliers.DTOs;

namespace SenorArroz.Application.Features.Suppliers.Commands;

public class UpdateSupplierCommand : IRequest<SupplierDto>
{
    public int Id { get; set; }
    public UpdateSupplierDto Supplier { get; set; } = new();
}


