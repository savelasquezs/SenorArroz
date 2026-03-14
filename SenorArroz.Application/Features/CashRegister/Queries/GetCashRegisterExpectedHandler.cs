using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.CashRegister.Queries;

public class GetCashRegisterExpectedHandler : IRequestHandler<GetCashRegisterExpectedQuery, CashRegisterExpectedDto>
{
    private readonly ICashRegisterClosureRepository _closureRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetCashRegisterExpectedHandler(
        ICashRegisterClosureRepository closureRepository,
        IBankRepository bankRepository,
        IApplicationDbContext context,
        ICurrentUser currentUser)
    {
        _closureRepository = closureRepository;
        _bankRepository = bankRepository;
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<CashRegisterExpectedDto> Handle(GetCashRegisterExpectedQuery request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;
        var now = DateTime.UtcNow;

        var lastClosure = await _closureRepository.GetLastByBranchAsync(branchId);
        var since = lastClosure?.ClosedAt ?? DateTime.MinValue;

        // --- EFECTIVO ---
        decimal openingCash = lastClosure?.ClosingCash ?? 0;

        // Ingresos en efectivo = Total orden - pagos por banco - pagos por app (solo órdenes entregadas)
        var cashFromOrders = await _context.Orders
            .Where(o => o.BranchId == branchId
                && o.Status == OrderStatus.Delivered
                && o.CreatedAt > since && o.CreatedAt <= now)
            .SumAsync(o =>
                (decimal)o.Total
                - o.BankPayments.Sum(bp => bp.Amount)
                - o.AppPayments.Sum(ap => ap.Amount),
                cancellationToken);

        // Gastos en efectivo = gasto total - lo que se pagó por banco
        var cashExpenses = await _context.ExpenseHeaders
            .Where(eh => eh.BranchId == branchId && eh.CreatedAt > since && eh.CreatedAt <= now)
            .SumAsync(eh =>
                (decimal)(eh.Total ?? 0) - eh.ExpenseBankPayments.Sum(ebp => ebp.Amount),
                cancellationToken);

        // Adelantos a domiciliarios
        var advances = await _context.DeliverymanAdvances
            .Where(a => a.BranchId == branchId && a.CreatedAt > since && a.CreatedAt <= now)
            .SumAsync(a => a.Amount, cancellationToken);

        var expectedCash = openingCash + cashFromOrders - cashExpenses - advances;

        // --- BANCOS ---
        bool isAdmin = _currentUser.Role == "superadmin" || _currentUser.Role == "admin";
        var banks = await _bankRepository.GetByBranchIdAsync(branchId, excludeHiddenBanks: !isAdmin);

        var bankExpected = new List<BankExpectedBalanceDto>();
        foreach (var bank in banks)
        {
            // Balance de apertura: lo que había en el último cuadre para este banco
            decimal openingBalance = 0;
            if (lastClosure != null)
            {
                var prevRecon = lastClosure.BankReconciliations.FirstOrDefault(r => r.BankId == bank.Id);
                openingBalance = prevRecon?.ActualBalance ?? 0;
            }

            // Ingresos desde el último cuadre: pagos de pedidos por este banco
            var bankPaymentsIn = await _context.BankPayments
                .Where(bp => bp.BankId == bank.Id
                    && bp.Order.BranchId == branchId
                    && bp.CreatedAt > since && bp.CreatedAt <= now)
                .SumAsync(bp => bp.Amount, cancellationToken);

            // Pagos de gastos por este banco (salidas)
            var expensePaymentsOut = await _context.ExpenseBankPayments
                .Where(ebp => ebp.BankId == bank.Id
                    && ebp.ExpenseHeader.BranchId == branchId
                    && ebp.CreatedAt > since && ebp.CreatedAt <= now)
                .SumAsync(ebp => ebp.Amount, cancellationToken);

            // Transferencias entrantes y salientes
            var incomingTransfers = await _context.BankTransfers
                .Where(bt => bt.ToBankId == bank.Id && bt.CreatedAt > since && bt.CreatedAt <= now)
                .SumAsync(bt => bt.Amount, cancellationToken);

            var outgoingTransfers = await _context.BankTransfers
                .Where(bt => bt.FromBankId == bank.Id && bt.CreatedAt > since && bt.CreatedAt <= now)
                .SumAsync(bt => bt.Amount, cancellationToken);

            var expectedBalance = openingBalance + bankPaymentsIn - expensePaymentsOut + incomingTransfers - outgoingTransfers;

            bankExpected.Add(new BankExpectedBalanceDto
            {
                BankId = bank.Id,
                BankName = bank.Name,
                OpeningBalance = openingBalance,
                ExpectedBalance = expectedBalance
            });
        }

        return new CashRegisterExpectedDto
        {
            OpeningCash = openingCash,
            ExpectedCash = expectedCash,
            CashFromOrders = cashFromOrders,
            CashExpenses = cashExpenses,
            Advances = advances,
            AsOf = now,
            LastClosureAt = lastClosure?.ClosedAt,
            Banks = bankExpected
        };
    }
}
