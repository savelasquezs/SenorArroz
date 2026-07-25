// SenorArroz.Application/Features/Banks/Queries/GetBankDetailHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankDetailHandler : IRequestHandler<GetBankDetailQuery, BankDetailDto?>
{
    private readonly IBankRepository _bankRepository;
    private readonly IBankLedgerService _bankLedgerService;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public GetBankDetailHandler(
        IBankRepository bankRepository,
        IBankLedgerService bankLedgerService,
        IMapper mapper,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _bankRepository = bankRepository;
        _bankLedgerService = bankLedgerService;
        _mapper = mapper;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<BankDetailDto?> Handle(GetBankDetailQuery request, CancellationToken cancellationToken)
    {
        var bank = await _bankRepository.GetByIdWithAppsAsync(request.Id, cancellationToken);
        
        if (bank == null)
            return null;
        _branchContext.EnsureAccess(bank.BranchId);

        // Check if user has access to this bank's branch
        if (!Roles.IsSuperadmin(_currentUser.Role) && bank.BranchId != _currentUser.BranchId)
            return null;

        // Cashier cannot access hidden banks (CashVault, RealVault)
        if (Roles.IsCashier(_currentUser.Role) && (bank.Type == BankType.CashVault || bank.Type == BankType.RealVault))
            return null;

        var bankDetailDto = _mapper.Map<BankDetailDto>(bank);

        // Add detailed statistics
        bankDetailDto.TotalApps = await _bankRepository.GetTotalAppsAsync(bank.Id, cancellationToken);
        bankDetailDto.ActiveApps = await _bankRepository.GetActiveAppsAsync(bank.Id, cancellationToken);
        bankDetailDto.TotalBankPayments = await _bankRepository.GetTotalBankPaymentsAsync(bank.Id, cancellationToken);
        bankDetailDto.TotalExpenseBankPayments = await _bankRepository.GetTotalExpenseBankPaymentsAsync(bank.Id, cancellationToken);
        bankDetailDto.BalanceBreakdown = await _bankLedgerService.GetRunningBalanceBreakdownAsync(bank.Id, cancellationToken);
        bankDetailDto.CurrentBalance = bankDetailDto.BalanceBreakdown.NetBalance;

        return bankDetailDto;
    }
}
