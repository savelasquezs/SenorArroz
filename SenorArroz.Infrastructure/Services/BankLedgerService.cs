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
        var reservationDepositsIn = await _bankRepository.GetTotalReservationDepositsAsync(bankId, cancellationToken: cancellationToken);
        var expenseOut = await _bankRepository.GetTotalExpenseBankPaymentsAsync(bankId);
        var outgoing = await _bankRepository.GetTotalOutgoingTransfersAsync(bankId);
        var incoming = await _bankRepository.GetTotalIncomingTransfersAsync(bankId);
        var deliverymanIn = await _bankRepository.GetTotalDeliverymanBankTransferInAsync(bankId);

        var net = bankPaymentsIn + reservationDepositsIn - expenseOut - outgoing + incoming + deliverymanIn;

        return new BankBalanceBreakdownDto
        {
            BankPaymentsIn = bankPaymentsIn,
            ReservationDepositsIn = reservationDepositsIn,
            ExpenseBankPaymentsOut = expenseOut,
            OutgoingTransfers = outgoing,
            IncomingTransfers = incoming,
            DeliverymanBankTransferIn = deliverymanIn,
            NetBalance = net
        };
    }

    public async Task<BankBalanceBreakdownDto> GetPeriodBalanceBreakdownAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var bankPaymentsIn = await _bankRepository.GetTotalBankPaymentsInPeriodAsync(bankId, fromUtc, toUtc);
        var reservationDepositsIn = await _bankRepository.GetTotalReservationDepositsInPeriodAsync(bankId, fromUtc, toUtc);
        var expenseOut = await _bankRepository.GetTotalExpenseBankPaymentsInPeriodAsync(bankId, fromUtc, toUtc);
        var outgoing = await _bankRepository.GetTotalOutgoingTransfersInPeriodAsync(bankId, fromUtc, toUtc);
        var incoming = await _bankRepository.GetTotalIncomingTransfersInPeriodAsync(bankId, fromUtc, toUtc);
        var deliverymanIn = await _bankRepository.GetTotalDeliverymanBankTransferInPeriodAsync(bankId, fromUtc, toUtc);
        var net = bankPaymentsIn + reservationDepositsIn - expenseOut - outgoing + incoming + deliverymanIn;

        return new BankBalanceBreakdownDto
        {
            BankPaymentsIn = bankPaymentsIn,
            ReservationDepositsIn = reservationDepositsIn,
            ExpenseBankPaymentsOut = expenseOut,
            OutgoingTransfers = outgoing,
            IncomingTransfers = incoming,
            DeliverymanBankTransferIn = deliverymanIn,
            NetBalance = net
        };
    }
}
