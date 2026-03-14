using MediatR;
using SenorArroz.Application.Features.BankTransfers.DTOs;

namespace SenorArroz.Application.Features.BankTransfers.Commands;

public class CreateBankTransferCommand : IRequest<BankTransferDto>
{
    public int FromBankId { get; set; }
    public int ToBankId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}
