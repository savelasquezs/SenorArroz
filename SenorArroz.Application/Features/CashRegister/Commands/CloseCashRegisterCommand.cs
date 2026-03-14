using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CloseCashRegisterCommand : IRequest<CashClosureDto>
{
    public int? BranchId { get; set; }
    public CloseCashRegisterDto Dto { get; set; } = null!;
}
