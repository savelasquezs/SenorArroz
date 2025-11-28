using MediatR;

namespace SenorArroz.Application.Features.Suppliers.Commands;

public class DeleteSupplierCommand : IRequest<bool>
{
    public int Id { get; set; }
}


