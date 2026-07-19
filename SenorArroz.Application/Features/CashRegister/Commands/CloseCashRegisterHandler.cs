using System.Text.Json;
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
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Domain.Models;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CloseCashRegisterHandler : IRequestHandler<CloseCashRegisterCommand, CashClosureDto>
{
    private readonly ICashRegisterClosureRepository _closureRepository;
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;

    public CloseCashRegisterHandler(
        ICashRegisterClosureRepository closureRepository,
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IMediator mediator,
        IClock clock,
        IUserRepository userRepository,
        IEmailService emailService)
    {
        _closureRepository = closureRepository;
        _context = context;
        _currentUser = currentUser;
        _mediator = mediator;
        _clock = clock;
        _userRepository = userRepository;
        _emailService = emailService;
    }

    public async Task<CashClosureDto> Handle(CloseCashRegisterCommand request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;
        var dto = request.Dto;

        var exemptIds = await CashRegisterExemptOrderIds.ActiveExemptOrderIdsAsync(_context, branchId, cancellationToken);
        var today = ColombiaTimeHelper.GetPrepareAtSqlTruncDayUtc(_clock.UtcNow);

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

        var expectedSnapshot = await _mediator.Send(new GetCashRegisterExpectedQuery { BranchId = branchId }, cancellationToken);
        var allowedBankIds = expectedSnapshot.Banks.Select(b => b.BankId).ToHashSet();
        var unexpectedBank = dto.BankReconciliations.FirstOrDefault(r => !allowedBankIds.Contains(r.BankId));
        if (unexpectedBank != null)
        {
            throw new InvalidOperationException($"El banco ID {unexpectedBank.BankId} no está disponible para este cuadre.");
        }

        foreach (var recon in dto.BankReconciliations)
        {
            var diff = CashRegisterMoney.DifferenceInWholePesos(recon.ActualBalance, recon.ExpectedBalance);
            if (diff != 0)
            {
                throw new InvalidOperationException(
                    $"El banco ID {recon.BankId} tiene una diferencia de {diff}. Todos los bancos deben cuadrar a 0.");
            }
        }

        var activeLoans = await _context.BranchInformalLoans
            .Where(l => l.BranchId == branchId && l.DeactivatedAt == null)
            .ToListAsync(cancellationToken);
        var informalActiveSum = activeLoans.Sum(l => l.Amount);

        var (unsettledAppLines, unsettledAppsTotal) =
            await CashRegisterUnsettledAppsHelper.LoadUnsettledForBranchAsync(_context, branchId, cancellationToken);

        var countedGlobalTotal = dto.ClosingCash
            + dto.BankReconciliations.Sum(r => r.ActualBalance)
            + informalActiveSum
            + unsettledAppsTotal;

        if (!CashRegisterMoney.EqualInWholePesos(countedGlobalTotal, expectedSnapshot.ExpectedGlobalTotal))
        {
            throw new InvalidOperationException(
                $"El total global contado ({CashRegisterMoney.ToWholePeso(countedGlobalTotal):N0}) no coincide con el esperado ({CashRegisterMoney.ToWholePeso(expectedSnapshot.ExpectedGlobalTotal):N0}). " +
                "Revisa efectivo, saldos reales por banco, préstamos informales activos y pendiente por liquidar en apps.");
        }

        var lastClosure = await _closureRepository.GetLastByBranchAsync(branchId, cancellationToken);
        decimal openingCash = lastClosure?.ClosingCash ?? 0;
        var submittedBankIds = dto.BankReconciliations.Select(r => r.BankId).ToHashSet();
        var carriedHiddenBankReconciliations = expectedSnapshot.HiddenBanksForClosureCarry
            .Where(b => !submittedBankIds.Contains(b.BankId))
            .Select(b => new CashClosureBankReconciliation
            {
                BankId = b.BankId,
                ExpectedBalance = b.ExpectedBalance,
                ActualBalance = b.ExpectedBalance,
                Adjustments = "[]",
                Difference = 0
            });

        var closure = new CashRegisterClosure
        {
            BranchId = branchId,
            ClosedAt = DateTime.SpecifyKind(dto.ClosedAt, DateTimeKind.Utc),
            CreatedById = _currentUser.Id,
            OpeningCash = openingCash,
            ClosingCash = dto.ClosingCash,
            DenominationCounts = dto.DenominationCounts,
            PendingAppPaymentsSnapshot = CashRegisterUnsettledAppsHelper.SerializeSnapshot(unsettledAppLines),
            BankReconciliations = dto.BankReconciliations.Select(r => new CashClosureBankReconciliation
            {
                BankId = r.BankId,
                ExpectedBalance = r.ExpectedBalance,
                ActualBalance = r.ActualBalance,
                Adjustments = r.Adjustments,
                Difference = CashRegisterMoney.DifferenceInWholePesos(r.ActualBalance, r.ExpectedBalance)
            }).Concat(carriedHiddenBankReconciliations).ToList(),
            InformalLoans = activeLoans
                .Select(l => new CashClosureInformalLoan { Concept = l.Concept, Amount = l.Amount })
                .ToList()
        };

        var saved = await _closureRepository.CreateAsync(closure, cancellationToken);
        var auditBusinessDate = ColombiaTimeHelper.ConvertUtcToColombiaCalendarDate(saved.ClosedAt);
        var existingDispatch = await _context.DailyAuditDispatches
            .FirstOrDefaultAsync(x => x.BranchId == branchId && x.BusinessDate == auditBusinessDate.Date, cancellationToken);

        string auditStatus;
        string? auditError = null;
        DateTime? auditDispatchedAt = null;

        if (existingDispatch != null)
        {
            auditStatus = "already_sent";
            auditError = existingDispatch.DispatchError;
            auditDispatchedAt = existingDispatch.DispatchedAt;
        }
        else
        {
            var previousClosure = await _context.CashRegisterClosures
                .AsNoTracking()
                .Where(x => x.BranchId == branchId && x.Id != saved.Id && x.ClosedAt < saved.ClosedAt)
                .OrderByDescending(x => x.ClosedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var periodStartUtc = previousClosure?.ClosedAt ?? ColombiaTimeHelper.ColombiaCalendarDayStartUtc(auditBusinessDate);
            var logs = await _context.EntityAuditLogs
                .AsNoTracking()
                .Where(x => x.BranchId == branchId && x.ChangedAt > periodStartUtc && x.ChangedAt <= saved.ClosedAt)
                .OrderByDescending(x => x.ChangedAt)
                .ToListAsync(cancellationToken);

            var relevantLogs = logs
                .Where(CashClosureAuditMapper.ShouldIncludeInDailyEmail)
                .ToList();
            var referencedProductIds = relevantLogs
                .SelectMany(x => CashClosureAuditMapper.ParseDelta(x.MoneyDeltaJson).ProductIds)
                .Distinct()
                .ToList();
            var productNames = await _context.Products
                .AsNoTracking()
                .Where(x => referencedProductIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
            var cancelledOrderIds = relevantLogs
                .Where(x => x.OperationType == "cancelled")
                .Select(x => x.EntityId)
                .Distinct()
                .ToList();
            var cancelledOrderProductRows = await _context.OrderDetails
                .AsNoTracking()
                .Where(x => cancelledOrderIds.Contains(x.OrderId))
                .Select(x => new { x.OrderId, x.Product.Name })
                .ToListAsync(cancellationToken);
            var cancelledOrderProductNames = cancelledOrderProductRows
                .GroupBy(x => x.OrderId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(x => x.Name).Distinct().ToList());

            var groups = relevantLogs
                .GroupBy(CashClosureAuditMapper.GroupKey)
                .Select(g => new CashClosureAuditGroupDto
                {
                    Key = g.Key,
                    Title = CashClosureAuditMapper.GroupTitle(g.Key),
                    EventCount = g.Count(),
                    NetDifference = g.Sum(x => CashClosureAuditMapper.ParseDelta(x.MoneyDeltaJson).Difference ?? 0),
                    Details = g.OrderByDescending(x => x.ChangedAt)
                        .Select(x => CashClosureAuditMapper.FormatDailyEmailDetail(x, productNames, cancelledOrderProductNames))
                        .ToList()
                })
                .OrderBy(x => x.Title)
                .ToList();

            var branchUsers = await _userRepository.GetAllAsync(branchId, cancellationToken);
            var allUsers = await _userRepository.GetAllAsync(null, cancellationToken);
            var recipients = branchUsers
                .Where(x => x.Active && x.BranchId == branchId && x.Role == UserRole.Admin)
                .Concat(allUsers.Where(x => x.Active && x.Role == UserRole.Superadmin))
                .Select(x => x.Email?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();

            var payload = new DailyMonetaryAuditEmailPayload
            {
                BranchName = saved.Branch?.Name ?? string.Empty,
                BusinessDate = auditBusinessDate,
                PeriodStartUtc = periodStartUtc,
                PeriodEndUtc = saved.ClosedAt,
                RecipientEmails = recipients,
                Groups = groups.Select(x => new DailyMonetaryAuditEmailGroup
                {
                    Title = x.Title,
                    EventCount = x.EventCount,
                    NetDifference = x.NetDifference,
                    Lines = x.Details
                }).ToList()
            };

            var dispatch = new DailyAuditDispatch
            {
                BranchId = branchId,
                BusinessDate = auditBusinessDate.Date,
                CashRegisterClosureId = saved.Id,
                DispatchedAt = null,
                DispatchedByUserId = _currentUser.IsAuthenticated ? _currentUser.Id : null,
                DispatchStatus = "queued",
                DispatchError = null,
                RecipientEmailsJson = JsonSerializer.Serialize(recipients),
                SummaryJson = JsonSerializer.Serialize(new
                {
                    closureId = saved.Id,
                    businessDate = auditBusinessDate.ToString("yyyy-MM-dd"),
                    periodStartUtc,
                    periodEndUtc = saved.ClosedAt,
                    groups
                })
            };

            _context.DailyAuditDispatches.Add(dispatch);
            await _context.SaveChangesAsync(cancellationToken);

            var emailResult = await _emailService.SendDailyMonetaryAuditEmailAsync(
                recipients,
                payload,
                relatedEntityType: "daily_audit_dispatch",
                relatedEntityId: dispatch.Id);

            auditStatus = emailResult.Success ? "queued" : "failed";
            auditError = emailResult.Success
                ? null
                : $"No se pudo encolar el correo de auditoría monetaria. Provider: {emailResult.Provider}. Error: {emailResult.ErrorMessage}";
            auditDispatchedAt = null;

            dispatch.DispatchStatus = auditStatus;
            dispatch.DispatchError = auditError;

            await _context.SaveChangesAsync(cancellationToken);
        }

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
            PendingAppPaymentsSnapshot = saved.PendingAppPaymentsSnapshot,
            AuditBusinessDate = auditBusinessDate.ToString("yyyy-MM-dd"),
            AuditDispatchStatus = auditStatus,
            AuditDispatchError = auditError,
            AuditDispatchedAt = auditDispatchedAt,
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
