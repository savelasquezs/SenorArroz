using MediatR;
using SenorArroz.Application.Features.Suppliers.DTOs;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSupplierExpensesQuery : IRequest<List<SupplierExpenseDto>>
{
    public int SupplierId { get; set; }
}


