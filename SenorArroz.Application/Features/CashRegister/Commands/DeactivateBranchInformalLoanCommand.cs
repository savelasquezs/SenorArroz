using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class DeactivateBranchInformalLoanCommand : IRequest<BranchInformalLoanDto>
{
    public int Id { get; set; }
    public int? BranchId { get; set; }
    public DeactivateBranchInformalLoanDto Dto { get; set; } = new();
}
