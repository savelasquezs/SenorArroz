// SenorArroz.Application/Features/Banks/Queries/GetBanksHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBanksHandler : IRequestHandler<GetBanksQuery, PagedResult<BankDto>>
{
    private readonly IBankRepository _bankRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public GetBanksHandler(
        IBankRepository bankRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _bankRepository = bankRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<PagedResult<BankDto>> Handle(GetBanksQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        var branchFilter = _branchContext.ResolveOptional(request.BranchId);

        // Cashier cannot see hidden banks (CashVault, RealVault)
        var excludeHidden = !Roles.IsAdminOrSuperadmin(_currentUser.Role);

        var pagedBanks = await _bankRepository.GetPagedAsync(
            branchFilter,
            request.Name,
            request.Active,
            excludeHiddenBanks: excludeHidden,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var bankDtos = new List<BankDto>();

        foreach (var bank in pagedBanks.Items)
        {
            var bankDto = _mapper.Map<BankDto>(bank);

            // Add additional data
            bankDto.TotalApps = await _bankRepository.GetTotalAppsAsync(bank.Id, cancellationToken);
            bankDto.ActiveApps = await _bankRepository.GetActiveAppsAsync(bank.Id, cancellationToken);
            bankDto.CurrentBalance = await _bankRepository.GetCurrentBalanceAsync(bank.Id, cancellationToken);

            bankDtos.Add(bankDto);
        }

        return new PagedResult<BankDto>
        {
            Items = bankDtos,
            TotalCount = pagedBanks.TotalCount,
            Page = pagedBanks.Page,
            PageSize = pagedBanks.PageSize,
            TotalPages = pagedBanks.TotalPages
        };
    }
}
