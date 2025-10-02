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

    public GetBanksHandler(IBankRepository bankRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _bankRepository = bankRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<BankDto>> Handle(GetBanksQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        int? branchFilter = null;
        if (_currentUser.Role != "superadmin")
        {
            branchFilter = _currentUser.BranchId;
        }
        else if (request.BranchId > 0)
        {
            // Superadmin can optionally filter by specific branch
            branchFilter = request.BranchId;
        }
        // If branchFilter is null, superadmin gets all banks from all branches

        var pagedBanks = await _bankRepository.GetPagedAsync(
            branchFilter,
            request.Name,
            request.Active,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var bankDtos = new List<BankDto>();

        foreach (var bank in pagedBanks.Items)
        {
            var bankDto = _mapper.Map<BankDto>(bank);

            // Add additional data
            bankDto.TotalApps = await _bankRepository.GetTotalAppsAsync(bank.Id);
            bankDto.ActiveApps = await _bankRepository.GetActiveAppsAsync(bank.Id);
            bankDto.CurrentBalance = await _bankRepository.GetCurrentBalanceAsync(bank.Id);

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
