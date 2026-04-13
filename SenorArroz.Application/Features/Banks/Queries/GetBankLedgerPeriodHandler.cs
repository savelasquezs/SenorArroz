using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankLedgerPeriodHandler : IRequestHandler<GetBankLedgerPeriodQuery, BankBalanceBreakdownDto?>
{
    private readonly IBankRepository _bankRepository;
    private readonly IBankLedgerService _bankLedgerService;
    private readonly ICurrentUser _currentUser;

    public GetBankLedgerPeriodHandler(
        IBankRepository bankRepository,
        IBankLedgerService bankLedgerService,
        ICurrentUser currentUser)
    {
        _bankRepository = bankRepository;
        _bankLedgerService = bankLedgerService;
        _currentUser = currentUser;
    }

    public async Task<BankBalanceBreakdownDto?> Handle(GetBankLedgerPeriodQuery request, CancellationToken cancellationToken)
    {
        var bank = await _bankRepository.GetByIdAsync(request.BankId, cancellationToken);
        if (bank == null)
            return null;

        if (_currentUser.Role != "superadmin" && bank.BranchId != _currentUser.BranchId)
            return null;

        if (_currentUser.Role == "cashier" && (bank.Type == BankType.CashVault || bank.Type == BankType.RealVault))
            return null;

        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(request.FromDate, request.ToDate);
        return await _bankLedgerService.GetPeriodBalanceBreakdownAsync(bank.Id, fromUtc, toUtc, cancellationToken);
    }
}
