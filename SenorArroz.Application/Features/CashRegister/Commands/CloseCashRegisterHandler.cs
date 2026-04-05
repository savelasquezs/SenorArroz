using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CloseCashRegisterHandler : IRequestHandler<CloseCashRegisterCommand, CashClosureDto>
{
    private readonly ICashRegisterClosureRepository _closureRepository;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public CloseCashRegisterHandler(
        ICashRegisterClosureRepository closureRepository,
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _closureRepository = closureRepository;
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<CashClosureDto> Handle(CloseCashRegisterCommand request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;
        var dto = request.Dto;

        var undelivered = await _context.Orders
            .Where(o => o.BranchId == branchId
                && o.Status != OrderStatus.Delivered
                && o.Status != OrderStatus.Cancelled)
            .CountAsync(cancellationToken);
        if (undelivered > 0)
        {
            throw new InvalidOperationException(
                $"No se puede cerrar caja: hay {undelivered} pedido(s) sin entregar. Entrega o cancela esos pedidos antes de cuadrar.");
        }

        // Validar que la diferencia de todos los bancos es 0
        foreach (var recon in dto.BankReconciliations)
        {
            var diff = recon.ActualBalance - recon.ExpectedBalance;
            if (diff != 0)
                throw new InvalidOperationException(
                    $"El banco ID {recon.BankId} tiene una diferencia de {diff}. Todos los bancos deben cuadrar a 0.");
        }

        var lastClosure = await _closureRepository.GetLastByBranchAsync(branchId);
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
            }).ToList()
        };

        var saved = await _closureRepository.CreateAsync(closure);

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
