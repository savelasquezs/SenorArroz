// SenorArroz.Application/Features/Banks/Queries/GetBankDetailHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankDetailHandler : IRequestHandler<GetBankDetailQuery, BankDetailDto?>
{
    private readonly IBankRepository _bankRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetBankDetailHandler(IBankRepository bankRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _bankRepository = bankRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<BankDetailDto?> Handle(GetBankDetailQuery request, CancellationToken cancellationToken)
    {
        var bank = await _bankRepository.GetByIdWithAppsAsync(request.Id);
        
        if (bank == null)
            return null;

        // Check if user has access to this bank's branch
        if (_currentUser.Role != "superadmin" && bank.BranchId != _currentUser.BranchId)
            return null;

        var bankDetailDto = _mapper.Map<BankDetailDto>(bank);

        // Add detailed statistics
        bankDetailDto.TotalApps = await _bankRepository.GetTotalAppsAsync(bank.Id);
        bankDetailDto.ActiveApps = await _bankRepository.GetActiveAppsAsync(bank.Id);
        bankDetailDto.TotalBankPayments = await _bankRepository.GetTotalBankPaymentsAsync(bank.Id);
        bankDetailDto.TotalExpenseBankPayments = await _bankRepository.GetTotalExpenseBankPaymentsAsync(bank.Id);
        bankDetailDto.CurrentBalance = await _bankRepository.GetCurrentBalanceAsync(bank.Id);

        return bankDetailDto;
    }
}
