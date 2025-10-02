// SenorArroz.Application/Features/Banks/Queries/GetBankByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
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
        var bank = await _bankRepository.GetByIdAsync(request.Id);
        
        if (bank == null)
            return null;

        // Check if user has access to this bank's branch
        if (_currentUser.Role != "superadmin" && bank.BranchId != _currentUser.BranchId)
            return null;

        var bankDto = _mapper.Map<BankDto>(bank);

        // Add additional data
        bankDto.TotalApps = await _bankRepository.GetTotalAppsAsync(bank.Id);
        bankDto.ActiveApps = await _bankRepository.GetActiveAppsAsync(bank.Id);
        bankDto.CurrentBalance = await _bankRepository.GetCurrentBalanceAsync(bank.Id);

        return bankDto;
    }
}
