using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Application.Features.CashRegister.Helpers;
using SenorArroz.Application.Features.CashRegister.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CloseCashRegisterHandler : IRequestHandler<CloseCashRegisterCommand, CashClosureDto>
{
    private readonly ICashRegisterClosureRepository _closureRepository;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public CloseCashRegisterHandler(
        ICashRegisterClosureRepository closureRepository,
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IMediator mediator,
        IClock clock)
    {
        _closureRepository = closureRepository;
        _context = context;
        _currentUser = currentUser;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<CashClosureDto> Handle(CloseCashRegisterCommand request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;
        var dto = request.Dto;

        var exemptIds = await CashRegisterExemptOrderIds.ActiveExemptOrderIdsAsync(_context, branchId, cancellationToken);

        var today = ColombiaTimeHelper.GetNowInColombiaFromUtc(_clock.UtcNow).Date;

        var undelivered = await _context.Orders
            .Where(o => o.BranchId == branchId
                && o.Status != OrderStatus.Delivered
                && o.Status != OrderStatus.Cancelled
                && !exemptIds.Contains(o.Id)
                && !(o.Type == OrderType.Reservation
                     && o.PrepareAt.HasValue
                     && o.PrepareAt.Value.ToUniversalTime().AddHours(-5).Date != today))
            .CountAsync(cancellationToken);
        if (undelivered > 0)
        {
            throw new InvalidOperationException(
                $"No se puede cerrar caja: hay {undelivered} pedido(s) sin entregar. Entrega o cancela esos pedidos antes de cuadrar.");
        }

        foreach (var recon in dto.BankReconciliations)
        {
            var diff = recon.ActualBalance - recon.ExpectedBalance;
            if (diff != 0)
                throw new InvalidOperationException(
                    $"El banco ID {recon.BankId} tiene una diferencia de {diff}. Todos los bancos deben cuadrar a 0.");
        }

        var activeLoans = await _context.BranchInformalLoans
            .Where(l => l.BranchId == branchId && l.DeactivatedAt == null)
            .ToListAsync(cancellationToken);
        var informalActiveSum = activeLoans.Sum(l => l.Amount);

        var countedGlobalTotal = dto.ClosingCash + dto.BankReconciliations.Sum(r => r.ActualBalance) + informalActiveSum;

        var expectedSnapshot = await _mediator.Send(new GetCashRegisterExpectedQuery { BranchId = branchId }, cancellationToken);
        if (countedGlobalTotal != expectedSnapshot.ExpectedGlobalTotal)
        {
            throw new InvalidOperationException(
                $"El total global contado ({countedGlobalTotal:N0}) no coincide con el esperado ({expectedSnapshot.ExpectedGlobalTotal:N0}). " +
                "Revisa efectivo, saldos reales por banco y préstamos informales activos.");
        }

        var lastClosure = await _closureRepository.GetLastByBranchAsync(branchId, cancellationToken);
        decimal openingCash = lastClosure?.ClosingCash ?? 0;

        var closure = new CashRegisterClosure
        {
            BranchId = branchId,
            ClosedAt = DateTime.SpecifyKind(dto.ClosedAt, DateTimeKind.Utc),
            CreatedById = _currentUser.Id,
            OpeningCash = openingCash,
            ClosingCash = dto.ClosingCash,
            DenominationCounts = dto.DenominationCounts,
            BankReconciliations = dto.BankReconciliations.Select(r => new CashClosureBankReconciliation
            {
                BankId = r.BankId,
                ExpectedBalance = r.ExpectedBalance,
                ActualBalance = r.ActualBalance,
                Adjustments = r.Adjustments,
                Difference = r.ActualBalance - r.ExpectedBalance
            }).ToList(),
            InformalLoans = activeLoans
                .Select(l => new CashClosureInformalLoan { Concept = l.Concept, Amount = l.Amount })
                .ToList()
        };

        var saved = await _closureRepository.CreateAsync(closure, cancellationToken);

        return new CashClosureDto
        {
            Id = saved.Id,
            BranchId = saved.BranchId,
            BranchName = saved.Branch?.Name ?? "",
            ClosedAt = saved.ClosedAt,
            CreatedById = saved.CreatedById,
            CreatedByName = saved.CreatedBy?.Name ?? "",
            OpeningCash = saved.OpeningCash,
            ClosingCash = saved.ClosingCash,
            DenominationCounts = saved.DenominationCounts,
            CreatedAt = saved.CreatedAt,
            BankReconciliations = saved.BankReconciliations.Select(br => new CashClosureBankReconciliationDto
            {
                Id = br.Id,
                BankId = br.BankId,
                BankName = br.Bank?.Name ?? "",
                ExpectedBalance = br.ExpectedBalance,
                ActualBalance = br.ActualBalance,
                Adjustments = br.Adjustments,
                Difference = br.Difference
            }).ToList(),
            InformalLoans = saved.InformalLoans.Select(il => new CashClosureInformalLoanDto
            {
                Id = il.Id,
                Concept = il.Concept,
                Amount = il.Amount
            }).ToList()
        };
    }
}
