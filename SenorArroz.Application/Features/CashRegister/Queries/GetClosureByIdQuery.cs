using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetClosureByIdQuery : IRequest<CashClosureDto?>
{
    public int Id { get; set; }
}
