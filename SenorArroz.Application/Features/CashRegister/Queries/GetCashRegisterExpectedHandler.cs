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
    private readonly IClock _clock;

    public GetCashRegisterExpectedHandler(
        ICashRegisterClosureRepository closureRepository,
        IBankRepository bankRepository,
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IClock clock)
    {
        _closureRepository = closureRepository;
        _bankRepository = bankRepository;
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<CashRegisterExpectedDto> Handle(GetCashRegisterExpectedQuery request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;

        var lastClosure = await _closureRepository.GetLastByBranchAsync(branchId, cancellationToken);

        DateTime since;
        DateTime now;

        if (lastClosure is null)
        {
            since = ColombiaTimeHelper.GetTodayStartInUtcFromUtc(_clock.UtcNow);
            now = ColombiaTimeHelper.GetTodayEndInUtcFromUtc(_clock.UtcNow);
        }
        else
        {
            since = lastClosure.ClosedAt;
            now = _clock.UtcNow;
        }

        decimal openingCash = lastClosure?.ClosingCash ?? 0;

        decimal openingBanksActual = 0;
        if (lastClosure != null)
        {
            foreach (var r in lastClosure.BankReconciliations)
                openingBanksActual += r.ActualBalance;
        }

        // Snapshot de préstamos en el último cierre; si no hay filas (cuadres previos a esta lógica), no sumar préstamo en apertura global.
        decimal openingInformalFromLastClosure = 0;
        if (lastClosure?.InformalLoans is { Count: > 0 })
            openingInformalFromLastClosure = lastClosure.InformalLoans.Sum(x => x.Amount);

        var openingUnsettledAppsTotal = CashRegisterUnsettledAppsHelper.SumSnapshot(lastClosure?.PendingAppPaymentsSnapshot);

        var openingGlobalTotal = openingCash + openingBanksActual + openingInformalFromLastClosure + openingUnsettledAppsTotal;

        var informalLoansActiveTotal = await _context.BranchInformalLoans
            .Where(l => l.BranchId == branchId && l.DeactivatedAt == null)
            .SumAsync(l => l.Amount, cancellationToken);

        var expensesInPeriodTotal = await _context.ExpenseHeaders
            .Where(eh => eh.BranchId == branchId && eh.CreatedAt > since && eh.CreatedAt <= now)
            .SumAsync(eh => (decimal)(eh.Total ?? 0), cancellationToken);

        // Candidatos: entregados que podrían caer en el período por instante contable (PrepareAt/CreatedAt) o actividad reciente.
        var lookback = since.AddDays(-400);
        var deliveredCandidates = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId && o.Status == OrderStatus.Delivered)
            .Where(o =>
                o.UpdatedAt > lookback
                || o.CreatedAt > lookback
                || (o.PrepareAt != null && o.PrepareAt > lookback))
            .Select(o => new
            {
                o.Id,
                o.Total,
                o.PrepareAt,
                o.CreatedAt,
                o.Status,
            })
            .ToListAsync(cancellationToken);

        decimal salesInPeriodTotal = 0;
        foreach (var row in deliveredCandidates)
        {
            if (CashRegisterPeriodHelper.IsDeliveredSaleInCashRegisterPeriod(row.Status, row.PrepareAt, row.CreatedAt, since, now))
                salesInPeriodTotal += (decimal)row.Total;
        }

        var reservationDepositsInPeriod = await _context.ReservationDeposits
            .AsNoTracking()
            .Where(d => d.BranchId == branchId && d.ReceivedAt > since && d.ReceivedAt <= now)
            .Select(d => new
            {
                d.Amount,
                PrepareAt = d.Order.PrepareAt,
                CreatedAt = d.Order.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var reservationDepositsOrderDeliveryNotColombiaTodayTotal = reservationDepositsInPeriod
            .Where(d =>
            {
                var deliveryInstant = d.PrepareAt ?? d.CreatedAt;
                var utc = ColombiaTimeHelper.EnsureUtc(deliveryInstant);
                return !ColombiaTimeHelper.IsColombiaTodayFromUtc(utc, _clock.UtcNow);
            })
            .Sum(d => d.Amount);

        // Total esperado = apertura global (C0+B0+L0+apps snapshot) + ventas − gastos + abonos de reserva del período cuyo pedido tiene fecha de entrega/programación (PrepareAt o CreatedAt) distinta a hoy (CO). L1 activo no se suma aparte en el esperado.
        var expectedGlobalTotal = openingGlobalTotal + salesInPeriodTotal - expensesInPeriodTotal + reservationDepositsOrderDeliveryNotColombiaTodayTotal;

        var exemptOrderIds = await CashRegisterExemptOrderIds.ActiveExemptOrderIdsAsync(_context, branchId, cancellationToken);

        var todayCol = ColombiaTimeHelper.GetPrepareAtSqlTruncDayUtc(_clock.UtcNow);

        var undeliveredOrdersCount = await _context.Orders
            .Where(o => o.BranchId == branchId
                && o.Status != OrderStatus.Delivered
                && o.Status != OrderStatus.Cancelled
                && !exemptOrderIds.Contains(o.Id)
                && !(o.Type == OrderType.Reservation
                     && o.PrepareAt.HasValue
                     && o.PrepareAt.Value.ToUniversalTime().AddHours(-5).Date != todayCol))
            .CountAsync(cancellationToken);

        bool isAdmin = Roles.IsAdminOrSuperadmin(_currentUser.Role);
        var banks = await _bankRepository.GetByBranchIdAsync(branchId, excludeHiddenBanks: !isAdmin, cancellationToken);

        var bankExpected = new List<BankExpectedBalanceDto>();
        foreach (var bank in banks)
        {
            decimal openingBalance = 0;
            if (lastClosure != null)
            {
                var prevRecon = lastClosure.BankReconciliations.FirstOrDefault(r => r.BankId == bank.Id);
                openingBalance = prevRecon?.ActualBalance ?? 0;
            }

            var promotedReservationDepositIds = _context.BankPayments
                .Where(bp => bp.BankId == bank.Id && bp.SourceReservationDepositId.HasValue)
                .Select(bp => bp.SourceReservationDepositId!.Value);

            var bankPaymentsIn = await _context.BankPayments
                .Where(bp => bp.BankId == bank.Id
                    && bp.Order.BranchId == branchId
                    && bp.CreatedAt > since && bp.CreatedAt <= now)
                .SumAsync(bp => bp.Amount, cancellationToken);

            var expensePaymentsOut = await _context.ExpenseBankPayments
                .Where(ebp => ebp.BankId == bank.Id
                    && ebp.ExpenseHeader.BranchId == branchId
                    && ebp.CreatedAt > since && ebp.CreatedAt <= now)
                .SumAsync(ebp => ebp.Amount, cancellationToken);

            var incomingTransfers = await _context.BankTransfers
                .Where(bt => bt.ToBankId == bank.Id && bt.CreatedAt > since && bt.CreatedAt <= now)
                .SumAsync(bt => bt.Amount, cancellationToken);

            var outgoingTransfers = await _context.BankTransfers
                .Where(bt => bt.FromBankId == bank.Id && bt.CreatedAt > since && bt.CreatedAt <= now)
                .SumAsync(bt => bt.Amount, cancellationToken);

            var bankDepositPaymentsIn = await _context.ReservationDeposits
                .Where(d => d.BankId == bank.Id
                    && !promotedReservationDepositIds.Contains(d.Id)
                    && d.ReceivedAt > since && d.ReceivedAt <= now)
                .SumAsync(d => d.Amount, cancellationToken);

            var bankDepositsAlreadyCounted = await _context.ReservationDeposits
                .Where(d => d.BankId == bank.Id
                    && !promotedReservationDepositIds.Contains(d.Id)
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

        var (unsettledAppLines, unsettledAppsTotal) =
            await CashRegisterUnsettledAppsHelper.LoadUnsettledForBranchAsync(_context, branchId, cancellationToken);

        return new CashRegisterExpectedDto
        {
            OpeningCash = openingCash,
            OpeningGlobalTotal = openingGlobalTotal,
            OpeningUnsettledAppsTotal = openingUnsettledAppsTotal,
            SalesInPeriodTotal = salesInPeriodTotal,
            ExpensesInPeriodTotal = expensesInPeriodTotal,
            ExpectedGlobalTotal = expectedGlobalTotal,
            ReservationDepositsAddedToGlobalTotal = reservationDepositsOrderDeliveryNotColombiaTodayTotal,
            InformalLoansActiveTotal = informalLoansActiveTotal,
            UndeliveredOrdersCount = undeliveredOrdersCount,
            AsOf = now,
            LastClosureAt = lastClosure?.ClosedAt,
            Banks = bankExpected,
            UnsettledAppLines = unsettledAppLines,
            UnsettledAppsTotal = unsettledAppsTotal
        };
    }
}
