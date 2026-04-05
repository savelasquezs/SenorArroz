using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CreateCashVaultMovementCommand : IRequest<CashVaultMovementDto>
{
    public int? BranchId { get; set; }
    public CreateCashVaultMovementDto Dto { get; set; } = null!;
}
