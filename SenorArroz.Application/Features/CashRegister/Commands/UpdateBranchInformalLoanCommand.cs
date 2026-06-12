using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class UpdateBranchInformalLoanCommand : IRequest<BranchInformalLoanDto>
{
    public int Id { get; set; }
    public int? BranchId { get; set; }
    public UpdateBranchInformalLoanDto Dto { get; set; } = null!;
}
