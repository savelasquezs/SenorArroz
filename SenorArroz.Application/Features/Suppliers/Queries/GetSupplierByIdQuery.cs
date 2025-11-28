using MediatR;
using SenorArroz.Application.Features.Suppliers.DTOs;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSupplierByIdQuery : IRequest<SupplierDto?>
{
    public int Id { get; set; }
}


