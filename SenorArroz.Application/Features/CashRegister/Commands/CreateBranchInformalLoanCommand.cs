using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CreateBranchInformalLoanCommand : IRequest<BranchInformalLoanDto>
{
    public int? BranchId { get; set; }
    public CreateBranchInformalLoanDto Dto { get; set; } = null!;
}
