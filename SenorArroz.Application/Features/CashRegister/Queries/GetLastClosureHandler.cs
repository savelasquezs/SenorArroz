using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetLastClosureHandler : IRequestHandler<GetLastClosureQuery, CashClosureDto?>
{
    private readonly ICashRegisterClosureRepository _closureRepository;
    private readonly ICurrentUser _currentUser;

    public GetLastClosureHandler(ICashRegisterClosureRepository closureRepository, ICurrentUser currentUser)
    {
        _closureRepository = closureRepository;
        _currentUser = currentUser;
    }

    public async Task<CashClosureDto?> Handle(GetLastClosureQuery request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;
        var closure = await _closureRepository.GetLastByBranchAsync(branchId, cancellationToken);
        if (closure == null) return null;

        return new CashClosureDto
        {
            Id = closure.Id,
            BranchId = closure.BranchId,
            BranchName = closure.Branch?.Name ?? "",
            ClosedAt = closure.ClosedAt,
            CreatedById = closure.CreatedById,
            CreatedByName = closure.CreatedBy?.Name ?? "",
            OpeningCash = closure.OpeningCash,
            ClosingCash = closure.ClosingCash,
            DenominationCounts = closure.DenominationCounts,
            PendingAppPaymentsSnapshot = closure.PendingAppPaymentsSnapshot,
            CreatedAt = closure.CreatedAt,
            BankReconciliations = closure.BankReconciliations.Select(br => new CashClosureBankReconciliationDto
            {
                Id = br.Id,
                BankId = br.BankId,
                BankName = br.Bank?.Name ?? "",
                ExpectedBalance = br.ExpectedBalance,
                ActualBalance = br.ActualBalance,
                Adjustments = br.Adjustments,
                Difference = br.Difference
            }).ToList(),
            InformalLoans = closure.InformalLoans.Select(il => new CashClosureInformalLoanDto
            {
                Id = il.Id,
                Concept = il.Concept,
                Amount = il.Amount
            }).ToList()
        };
    }
}
