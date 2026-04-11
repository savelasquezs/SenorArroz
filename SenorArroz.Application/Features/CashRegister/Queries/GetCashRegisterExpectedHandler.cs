using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Application.Features.CashRegister.Helpers;
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

        var lastClosure = await _closureRepository.GetLastByBranchAsync(branchId);

        DateTime since;
        DateTime now;

        if (lastClosure is null)
        {
            // Primer cuadre: tomar solo el día actual en hora Colombia
            since = ColombiaTimeHelper.GetTodayStartInUtc();
            now = ColombiaTimeHelper.GetTodayEndInUtc();
        }
        else
        {
            // Cuadres posteriores: desde el último cierre hasta ahora
            since = lastClosure.ClosedAt;
            now = DateTime.UtcNow;
        }

        // --- EFECTIVO ---
        decimal openingCash = lastClosure?.ClosingCash ?? 0;

        // Pedidos entregados en el período (mismo filtro para ventas / efectivo / banco / app)
        var deliveredOrdersQuery = _context.Orders
            .Where(o => o.BranchId == branchId
                && o.Status == OrderStatus.Delivered
                && o.CreatedAt > since && o.CreatedAt <= now);

        var deliveredOrdersSalesTotal = await deliveredOrdersQuery
            .SumAsync(o => (decimal)o.Total, cancellationToken);

        var bankPaymentsFromOrdersTotal = await deliveredOrdersQuery
            .SumAsync(o => o.BankPayments.Sum(bp => bp.Amount), cancellationToken);

        var appPaymentsFromOrdersTotal = await deliveredOrdersQuery
            .SumAsync(o => o.AppPayments.Sum(ap => ap.Amount), cancellationToken);

        // Ingresos en efectivo = venta - banco - app
        var cashFromOrders = deliveredOrdersSalesTotal - bankPaymentsFromOrdersTotal - appPaymentsFromOrdersTotal;

        // Gastos en efectivo = gasto total - lo que se pagó por banco
        var cashExpenses = await _context.ExpenseHeaders
            .Where(eh => eh.BranchId == branchId && eh.CreatedAt > since && eh.CreatedAt <= now)
            .SumAsync(eh =>
                (decimal)(eh.Total ?? 0) - eh.ExpenseBankPayments.Sum(ebp => ebp.Amount),
                cancellationToken);

        // Abonos en efectivo de reservas recibidos en este período
        var cashDeposits = await _context.ReservationDeposits
            .Where(d => d.BranchId == branchId
                && d.IsEffective
                && d.ReceivedAt > since && d.ReceivedAt <= now)
            .SumAsync(d => d.Amount, cancellationToken);

        // Al entregar una reserva, su total ya se cuenta en cashFromOrders,
        // pero sus abonos anteriores ya entraron en cuadres previos → restarlos para no duplicar
        var depositsAlreadyCounted = await _context.ReservationDeposits
            .Where(d => d.BranchId == branchId
                && d.IsEffective
                && d.ReceivedAt <= since   // abonados ANTES del período actual
                && d.Order.Status == OrderStatus.Delivered
                && d.Order.UpdatedAt > since && d.Order.UpdatedAt <= now)
            .SumAsync(d => d.Amount, cancellationToken);

        // Abonos a domiciliario por transferencia: ya suman en el cuadre bancario (deliverymanBankIn);
        // no deben esperarse también en efectivo físico (balance total caja + banco).
        var advancesBankTransfer = await _context.DeliverymanAdvances
            .Where(a => a.BranchId == branchId
                && a.PaymentMethod == DeliverymanAdvancePaymentMethod.BankTransfer
                && a.CreatedAt > since && a.CreatedAt <= now)
            .SumAsync(a => a.Amount, cancellationToken);

        var informalLoansActiveTotal = await _context.BranchInformalLoans
            .Where(l => l.BranchId == branchId && l.DeactivatedAt == null)
            .SumAsync(l => l.Amount, cancellationToken);

        var cashVaultAbonos = await _context.CashVaultMovements
            .Where(m => m.BranchId == branchId
                && m.CreatedAt > since && m.CreatedAt <= now
                && m.Kind == CashVaultMovementKind.AbonoToVault)
            .SumAsync(m => m.Amount, cancellationToken);

        var cashVaultDescargas = await _context.CashVaultMovements
            .Where(m => m.BranchId == branchId
                && m.CreatedAt > since && m.CreatedAt <= now
                && m.Kind == CashVaultMovementKind.WithdrawFromVault)
            .SumAsync(m => m.Amount, cancellationToken);

        var cashVaultNetToVault = cashVaultAbonos - cashVaultDescargas;

        // Efectivo → banco: sale de caja física hacia cuenta (mismo período que el cuadre)
        var cashOutToBanks = await _context.BankTransfers
            .Where(bt => bt.FromBankId == null
                && bt.ToBank != null && bt.ToBank.BranchId == branchId
                && bt.CreatedAt > since && bt.CreatedAt <= now)
            .SumAsync(bt => bt.Amount, cancellationToken);

        // Banco → efectivo: entra a caja física desde cuenta
        var cashInFromBanks = await _context.BankTransfers
            .Where(bt => bt.ToBankId == null
                && bt.FromBank != null && bt.FromBank.BranchId == branchId
                && bt.CreatedAt > since && bt.CreatedAt <= now)
            .SumAsync(bt => bt.Amount, cancellationToken);

        var expectedCash = openingCash + cashFromOrders + cashDeposits - depositsAlreadyCounted - cashExpenses
            - advancesBankTransfer - informalLoansActiveTotal - cashVaultNetToVault
            + cashInFromBanks - cashOutToBanks;

        var exemptOrderIds = await CashRegisterExemptOrderIds.ActiveExemptOrderIdsAsync(_context, branchId, cancellationToken);

        var todayCol = DateTime.UtcNow.AddHours(-5).Date; // Fecha hoy en Colombia (UTC-5)

        var undeliveredOrdersCount = await _context.Orders
            .Where(o => o.BranchId == branchId
                && o.Status != OrderStatus.Delivered
                && o.Status != OrderStatus.Cancelled
                && !exemptOrderIds.Contains(o.Id)
                && !(o.Type == OrderType.Reservation
                     && o.PrepareAt.HasValue
                     && o.PrepareAt.Value.ToUniversalTime().AddHours(-5).Date != todayCol))
            .CountAsync(cancellationToken);

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

            // Abonos de reservas recibidos en este banco en el período
            var bankDepositPaymentsIn = await _context.ReservationDeposits
                .Where(d => d.BankId == bank.Id
                    && d.ReceivedAt > since && d.ReceivedAt <= now)
                .SumAsync(d => d.Amount, cancellationToken);

            // Abonos bancarios de reservas ya contabilizados en cuadres anteriores (descontar al entregar)
            var bankDepositsAlreadyCounted = await _context.ReservationDeposits
                .Where(d => d.BankId == bank.Id
                    && d.ReceivedAt <= since
                    && d.Order.Status == OrderStatus.Delivered
                    && d.Order.UpdatedAt > since && d.Order.UpdatedAt <= now)
                .SumAsync(d => d.Amount, cancellationToken);

            var deliverymanBankIn = await _context.DeliverymanAdvances
                .Where(a => a.BranchId == branchId
                    && a.BankId == bank.Id
                    && a.PaymentMethod == DeliverymanAdvancePaymentMethod.BankTransfer
                    && a.CreatedAt > since && a.CreatedAt <= now)
                .SumAsync(a => a.Amount, cancellationToken);

            var expectedBalance = openingBalance + bankPaymentsIn + bankDepositPaymentsIn - bankDepositsAlreadyCounted - expensePaymentsOut + incomingTransfers - outgoingTransfers + deliverymanBankIn;

            if (bank.Type == BankType.CashVault)
            {
                var vaultAbonosBank = await _context.CashVaultMovements
                    .Where(m => m.BranchId == branchId && m.BankId == bank.Id
                        && m.CreatedAt > since && m.CreatedAt <= now
                        && m.Kind == CashVaultMovementKind.AbonoToVault)
                    .SumAsync(m => m.Amount, cancellationToken);
                var vaultDescargasBank = await _context.CashVaultMovements
                    .Where(m => m.BranchId == branchId && m.BankId == bank.Id
                        && m.CreatedAt > since && m.CreatedAt <= now
                        && m.Kind == CashVaultMovementKind.WithdrawFromVault)
                    .SumAsync(m => m.Amount, cancellationToken);
                expectedBalance += vaultAbonosBank - vaultDescargasBank;
            }

            bankExpected.Add(new BankExpectedBalanceDto
            {
                BankId = bank.Id,
                BankName = bank.Name,
                BankType = bank.Type,
                OpeningBalance = openingBalance,
                ExpectedBalance = expectedBalance
            });
        }

        return new CashRegisterExpectedDto
        {
            OpeningCash = openingCash,
            ExpectedCash = expectedCash,
            CashFromOrders = cashFromOrders,
            DeliveredOrdersSalesTotal = deliveredOrdersSalesTotal,
            BankPaymentsFromOrdersTotal = bankPaymentsFromOrdersTotal,
            AppPaymentsFromOrdersTotal = appPaymentsFromOrdersTotal,
            CashDeposits = cashDeposits,
            CashExpenses = cashExpenses,
            AdvancesBankTransfer = advancesBankTransfer,
            InformalLoansActiveTotal = informalLoansActiveTotal,
            CashVaultAbonosTotal = cashVaultAbonos,
            CashVaultDescargasTotal = cashVaultDescargas,
            UndeliveredOrdersCount = undeliveredOrdersCount,
            AsOf = now,
            LastClosureAt = lastClosure?.ClosedAt,
            Banks = bankExpected
        };
    }
}
