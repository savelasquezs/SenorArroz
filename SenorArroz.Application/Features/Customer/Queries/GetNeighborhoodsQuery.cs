using MediatR;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetNeighborhoodsQuery : IRequest<IEnumerable<Neighborhood>>
{
    public int BranchId { get; set; }
}
