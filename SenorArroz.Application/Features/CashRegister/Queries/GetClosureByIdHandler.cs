using MediatR;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetClosureByIdHandler : IRequestHandler<GetClosureByIdQuery, CashClosureDto?>
{
    private readonly ICashRegisterClosureRepository _closureRepository;

    public GetClosureByIdHandler(ICashRegisterClosureRepository closureRepository)
    {
        _closureRepository = closureRepository;
    }

    public async Task<CashClosureDto?> Handle(GetClosureByIdQuery request, CancellationToken cancellationToken)
    {
        var c = await _closureRepository.GetByIdAsync(request.Id, cancellationToken);
        if (c == null) return null;

        return new CashClosureDto
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
            PendingAppPaymentsSnapshot = c.PendingAppPaymentsSnapshot,
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
        };
    }
}
