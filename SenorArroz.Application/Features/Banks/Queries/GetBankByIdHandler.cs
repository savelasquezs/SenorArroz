// SenorArroz.Application/Features/Banks/Queries/GetBankByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankByIdHandler : IRequestHandler<GetBankByIdQuery, BankDto?>
{
    private readonly IBankRepository _bankRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetBankByIdHandler(IBankRepository bankRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _bankRepository = bankRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<BankDto?> Handle(GetBankByIdQuery request, CancellationToken cancellationToken)
    {
        var bank = await _bankRepository.GetByIdAsync(request.Id, cancellationToken);
        
        if (bank == null)
            return null;

        // Check if user has access to this bank's branch
        if (!Roles.IsSuperadmin(_currentUser.Role) && bank.BranchId != _currentUser.BranchId)
            return null;

        // Cashier cannot access hidden banks (CashVault, RealVault)
        if (Roles.IsCashier(_currentUser.Role) && (bank.Type == BankType.CashVault || bank.Type == BankType.RealVault))
            return null;

        var bankDto = _mapper.Map<BankDto>(bank);

        // Add additional data
        bankDto.TotalApps = await _bankRepository.GetTotalAppsAsync(bank.Id, cancellationToken);
        bankDto.ActiveApps = await _bankRepository.GetActiveAppsAsync(bank.Id, cancellationToken);
        bankDto.CurrentBalance = await _bankRepository.GetCurrentBalanceAsync(bank.Id, cancellationToken);

        return bankDto;
    }
}
