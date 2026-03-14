using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetClosuresHandler : IRequestHandler<GetClosuresQuery, PagedResult<CashClosureDto>>
{
    private readonly ICashRegisterClosureRepository _closureRepository;
    private readonly ICurrentUser _currentUser;

    public GetClosuresHandler(ICashRegisterClosureRepository closureRepository, ICurrentUser currentUser)
    {
        _closureRepository = closureRepository;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<CashClosureDto>> Handle(GetClosuresQuery request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;
        var result = await _closureRepository.GetPagedAsync(branchId, fromDate: null, toDate: null, page: request.Page, pageSize: request.PageSize);

        var dtos = result.Items.Select(c => new CashClosureDto
        {
            Id = c.Id,
            BranchId = c.BranchId,
            BranchName = c.Branch?.Name ?? "",
            ClosedAt = c.ClosedAt,
            CreatedById = c.CreatedById,
            CreatedByName = c.CreatedBy?.Name ?? "",
            OpeningCash = c.OpeningCash,
            ClosingCash = c.ClosingCash,
            DenominationCounts = c.DenominationCounts,
            CreatedAt = c.CreatedAt,
            BankReconciliations = c.BankReconciliations.Select(br => new CashClosureBankReconciliationDto
            {
                Id = br.Id,
                BankId = br.BankId,
                BankName = br.Bank?.Name ?? "",
                ExpectedBalance = br.ExpectedBalance,
                ActualBalance = br.ActualBalance,
                Adjustments = br.Adjustments,
                Difference = br.Difference
            }).ToList(),
            InformalLoans = c.InformalLoans.Select(il => new CashClosureInformalLoanDto
            {
                Id = il.Id,
                Concept = il.Concept,
                Amount = il.Amount
            }).ToList()
        }).ToList();

        return new PagedResult<CashClosureDto>
        {
            Items = dtos,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
    }
}
