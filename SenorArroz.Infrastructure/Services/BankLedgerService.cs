using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Infrastructure.Services;

public class BankLedgerService : IBankLedgerService
{
    private readonly IBankRepository _bankRepository;

    public BankLedgerService(IBankRepository bankRepository)
    {
        _bankRepository = bankRepository;
    }

    public async Task<BankBalanceBreakdownDto> GetRunningBalanceBreakdownAsync(int bankId, CancellationToken cancellationToken = default)
    {
        var bankPaymentsIn = await _bankRepository.GetTotalBankPaymentsAsync(bankId);
        var expenseOut = await _bankRepository.GetTotalExpenseBankPaymentsAsync(bankId);
        var outgoing = await _bankRepository.GetTotalOutgoingTransfersAsync(bankId);
        var incoming = await _bankRepository.GetTotalIncomingTransfersAsync(bankId);
        var deliverymanIn = await _bankRepository.GetTotalDeliverymanBankTransferInAsync(bankId);

        var net = bankPaymentsIn - expenseOut - outgoing + incoming + deliverymanIn;

        return new BankBalanceBreakdownDto
        {
            BankPaymentsIn = bankPaymentsIn,
            ExpenseBankPaymentsOut = expenseOut,
            OutgoingTransfers = outgoing,
            IncomingTransfers = incoming,
            DeliverymanBankTransferIn = deliverymanIn,
            NetBalance = net
        };
    }
}
