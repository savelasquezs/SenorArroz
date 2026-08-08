using MediatR;

namespace SenorArroz.Application.Features.BankPayments.Commands;

public sealed class UnverifyTodayBankPaymentsCommand : IRequest<int>
{
    public int BankId { get; init; }
}
