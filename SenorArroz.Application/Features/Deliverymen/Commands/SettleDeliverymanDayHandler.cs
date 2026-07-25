using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliverymanAdvances.DTOs;
using SenorArroz.Application.Features.Deliverymen.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Deliverymen.Commands;

public class SettleDeliverymanDayHandler : IRequestHandler<SettleDeliverymanDayCommand, SettleDeliverymanDayResultDto>
{
    private const decimal DefaultBaseAmount = 55000m;
    private const decimal Tolerance = 0.02m;

    private readonly IApplicationDbContext _context;
    private readonly IUserRepository _userRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IFcmPushService _fcm;
    private readonly ILogger<SettleDeliverymanDayHandler> _logger;

    public SettleDeliverymanDayHandler(
        IApplicationDbContext context,
        IUserRepository userRepository,
        IOrderRepository orderRepository,
        IDeliverymanAdvanceRepository advanceRepository,
        IBankRepository bankRepository,
        IExpenseHeaderRepository expenseHeaderRepository,
        ICurrentUser currentUser,
        IBranchContext branchContext,
        IMapper mapper,
        IClock clock,
        IRefreshTokenRepository refreshTokenRepository,
        IFcmPushService fcm,
        ILogger<SettleDeliverymanDayHandler> logger)
    {
        _context = context;
        _userRepository = userRepository;
        _orderRepository = orderRepository;
        _advanceRepository = advanceRepository;
        _bankRepository = bankRepository;
        _expenseHeaderRepository = expenseHeaderRepository;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _mapper = mapper;
        _clock = clock;
        _refreshTokenRepository = refreshTokenRepository;
        _fcm = fcm;
        _logger = logger;
    }

    public async Task<SettleDeliverymanDayResultDto> Handle(SettleDeliverymanDayCommand request, CancellationToken cancellationToken)
    {
        var s = request.Settlement;
        if (string.IsNullOrWhiteSpace(s.Date) || !DateOnly.TryParse(s.Date, out var settlementDate))
            throw new BusinessException("Fecha inválida (use YYYY-MM-DD)");

        if (s.BaseAmount < 0)
            throw new BusinessException("La base no puede ser negativa");

        if (s.Mode != DeliverymanDayLiquidationMode.FullLiquidation
            && s.Mode != DeliverymanDayLiquidationMode.LiquidateAndReturnBase)
            throw new BusinessException("Modo de liquidación inválido");

        var deliveryman = await _userRepository.GetByIdAsync(request.DeliverymanId, cancellationToken);
        if (deliveryman == null)
            throw new BusinessException("El domiciliario no existe");
        _branchContext.EnsureAccess(deliveryman.BranchId);
        if (deliveryman.Role != UserRole.Deliveryman)
            throw new BusinessException("El usuario no es un domiciliario");
        if (!deliveryman.Active)
            throw new BusinessException("El domiciliario no está activo");

        var branchId = Roles.IsSuperadmin(_currentUser.Role)
            ? deliveryman.BranchId
            : _currentUser.BranchId;
        if (!Roles.IsSuperadmin(_currentUser.Role) && deliveryman.BranchId != branchId)
            throw new BusinessException("No tienes permisos para liquidar este domiciliario");

        var hasActiveAssignedOrders = await _context.Orders.AsNoTracking()
            .AnyAsync(x => x.BranchId == branchId
                           && x.DeliveryManId == request.DeliverymanId
                           && x.Status != OrderStatus.Delivered
                           && x.Status != OrderStatus.Cancelled,
                cancellationToken);
        if (hasActiveAssignedOrders)
            throw new BusinessException(
                "No puedes liquidar mientras el domiciliario tenga pedidos activos sin entregar o reasignar.");

        var startColombia = settlementDate.ToDateTime(TimeOnly.MinValue);
        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(startColombia, startColombia);
        var closesCurrentWorkSession = s.Mode == DeliverymanDayLiquidationMode.FullLiquidation
                                       && settlementDate == ColombiaTimeHelper.GetTodayDateOnlyColombiaFromUtc(_clock.UtcNow);

        var orders = (await DeliverymanDeliveredOrdersQuery.LoadAllDeliveredInRangeAsync(
                _orderRepository,
                branchId,
                request.DeliverymanId,
                fromUtc,
                toUtc,
                cancellationToken))
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        var priorState = await _context.DeliverymanDayStates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.BranchId == branchId
                     && x.DeliverymanId == request.DeliverymanId
                     && x.Date == settlementDate,
                cancellationToken);
        var lastLiquidationAtUtc = priorState?.LastLiquidationAtUtc;

        var cycleOrders = DeliverymanSettlementCycleHelper.FilterOrdersForCycle(
            orders, fromUtc, toUtc, lastLiquidationAtUtc, useSettlementCycle: true);
        var totalCash = DeliverymanSettlementCycleHelper.SumCashFromOrders(cycleOrders);
        var totalAdvancesBefore = await _advanceRepository.GetTotalAdvancesForSettlementCycleAsync(
            request.DeliverymanId, fromUtc, toUtc, lastLiquidationAtUtc, useSettlementCycle: true);

        var baseAmount = s.BaseAmount > 0 ? s.BaseAmount : DefaultBaseAmount;
        if (s.Mode == DeliverymanDayLiquidationMode.LiquidateAndReturnBase && s.CashAmount < baseAmount)
            throw new BusinessException(
                "Para liquidar y devolver base, el efectivo contado debe ser al menos la base inicial.");

        var surplus = totalCash + baseAmount - totalAdvancesBefore;
        if (surplus <= 0)
            throw new BusinessException("No hay excedente para liquidar con los datos actuales");

        var bankSum = s.BankTransfers.Sum(x => x.Amount);
        var expenseSum = s.ExpenseOffsets.Sum(x => x.Amount);
        var submitted = s.CashAmount + bankSum + expenseSum;

        if (Math.Abs(submitted - surplus) > Tolerance)
            throw new BusinessException(
                $"El total ingresado ({submitted:N0}) no coincide con el excedente a liquidar ({surplus:N0})");

        foreach (var line in s.BankTransfers)
        {
            if (line.Amount <= 0)
                throw new BusinessException("Los montos bancarios deben ser mayores a cero");
            var bank = await _bankRepository.GetByIdAsync(line.BankId, cancellationToken);
            if (bank == null || bank.BranchId != branchId)
                throw new BusinessException($"Banco inválido: {line.BankId}");
        }

        foreach (var line in s.ExpenseOffsets)
        {
            if (line.Amount <= 0)
                throw new BusinessException("Los montos de gasto deben ser mayores a cero");
            var expense = await _expenseHeaderRepository.GetByIdWithDetailsAsync(line.ExpenseHeaderId, cancellationToken);
            if (expense == null || expense.BranchId != branchId)
                throw new BusinessException($"Gasto no encontrado: {line.ExpenseHeaderId}");
            if (expense.DeliverymanId != request.DeliverymanId)
                throw new BusinessException("El gasto no está asociado a este domiciliario");
            var expenseTotal = expense.Total ?? 0;
            if (Math.Abs(expenseTotal - line.Amount) > Tolerance)
                throw new BusinessException($"El monto del gasto #{line.ExpenseHeaderId} no coincide con el total registrado");

            var existingOffset = await _context.DeliverymanAdvances.AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.DeliverymanId == request.DeliverymanId
                         && a.ExpenseHeaderId == line.ExpenseHeaderId
                         && a.PaymentMethod == DeliverymanAdvancePaymentMethod.ExpenseOffset,
                    cancellationToken);
            if (existingOffset != null
                && Math.Abs(existingOffset.Amount - line.Amount) > Tolerance)
                throw new BusinessException(
                    $"El abono existente del gasto #{line.ExpenseHeaderId} no coincide con el monto indicado en la liquidación.");
        }

        if (s.CashAmount < 0)
            throw new BusinessException("El efectivo contado no puede ser negativo");

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        var newAdvances = new List<DeliverymanAdvance>();
        var workSessionTokens = new List<string>();

        decimal cashAdvanceAmount = s.Mode == DeliverymanDayLiquidationMode.FullLiquidation
            ? s.CashAmount
            : (s.CashAmount > baseAmount ? s.CashAmount - baseAmount : 0m);

        if (cashAdvanceAmount > 0)
        {
            newAdvances.Add(new DeliverymanAdvance
            {
                DeliverymanId = request.DeliverymanId,
                Amount = cashAdvanceAmount,
                PaymentMethod = DeliverymanAdvancePaymentMethod.Cash,
                BankId = null,
                ExpenseHeaderId = null,
                Notes = s.Mode == DeliverymanDayLiquidationMode.LiquidateAndReturnBase
                    ? "Liquidación — efectivo (excedente sobre base)"
                    : "Liquidación — efectivo",
                CreatedBy = _currentUser.Id,
                BranchId = branchId
            });
        }

        foreach (var line in s.BankTransfers)
        {
            newAdvances.Add(new DeliverymanAdvance
            {
                DeliverymanId = request.DeliverymanId,
                Amount = line.Amount,
                PaymentMethod = DeliverymanAdvancePaymentMethod.BankTransfer,
                BankId = line.BankId,
                ExpenseHeaderId = null,
                Notes = "Liquidación — transferencia",
                CreatedBy = _currentUser.Id,
                BranchId = branchId
            });
        }

        foreach (var line in s.ExpenseOffsets)
        {
            var alreadyRecorded = await _advanceRepository.ExistsExpenseOffsetForExpenseHeaderAsync(
                request.DeliverymanId,
                line.ExpenseHeaderId,
                cancellationToken);
            if (alreadyRecorded)
                continue;

            newAdvances.Add(new DeliverymanAdvance
            {
                DeliverymanId = request.DeliverymanId,
                Amount = line.Amount,
                PaymentMethod = DeliverymanAdvancePaymentMethod.ExpenseOffset,
                BankId = null,
                ExpenseHeaderId = line.ExpenseHeaderId,
                Notes = $"Liquidación — gasto #{line.ExpenseHeaderId}",
                CreatedBy = _currentUser.Id,
                BranchId = branchId
            });
        }

        _context.DeliverymanAdvances.AddRange(newAdvances);
        await _context.SaveChangesAsync(cancellationToken);
        var createdIds = newAdvances.Select(a => a.Id).ToList();

        var state = await _context.DeliverymanDayStates
            .FirstOrDefaultAsync(
                x => x.BranchId == branchId
                     && x.DeliverymanId == request.DeliverymanId
                     && x.Date == settlementDate,
                cancellationToken);

        if (state == null)
        {
            state = new DeliverymanDayState
            {
                BranchId = branchId,
                DeliverymanId = request.DeliverymanId,
                Date = settlementDate,
                LiquidationMode = s.Mode,
                Blocked = s.Mode == DeliverymanDayLiquidationMode.FullLiquidation
            };
            _context.DeliverymanDayStates.Add(state);
        }
        else
        {
            state.LiquidationMode = s.Mode;
            state.Blocked = s.Mode == DeliverymanDayLiquidationMode.FullLiquidation;
            if (state.Blocked)
            {
                state.UnlockedAt = null;
                state.UnlockedById = null;
            }
        }

        var liquidationMarkerUtc = newAdvances.Count > 0
            ? await _context.DeliverymanAdvances
                .Where(a => createdIds.Contains(a.Id))
                .MaxAsync(a => a.CreatedAt, cancellationToken)
            : _clock.UtcNow;
        state.LastLiquidationAtUtc = DateTime.SpecifyKind(liquidationMarkerUtc, DateTimeKind.Utc).AddTicks(10);

        if (closesCurrentWorkSession)
        {
            var nowUtc = ColombiaTimeHelper.EnsureUtc(_clock.UtcNow);
            var activeSessions = await _context.DeliveryWorkSessions
                .Where(x => x.DeliverymanId == request.DeliverymanId
                            && x.Status == DeliveryWorkSessionStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var session in activeSessions)
            {
                session.Close(nowUtc, DeliveryWorkSessionEndReason.TotalSettlement);
                _context.DeliveryDeviceEvents.Add(DeliveryDeviceEvent.ForClosure(
                    session,
                    nowUtc,
                    DeliveryWorkSessionEndReason.TotalSettlement));
            }

            workSessionTokens = await _context.UserDeviceTokens.AsNoTracking()
                .Where(x => x.UserId == request.DeliverymanId)
                .Select(x => x.Token)
                .ToListAsync(cancellationToken);
            await _refreshTokenRepository.RevokeAllByUserIdAsync(
                request.DeliverymanId,
                "total-settlement",
                cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (closesCurrentWorkSession)
            await NotifyWorkSessionClosedAsync(request.DeliverymanId, workSessionTokens, cancellationToken);

        var dtos = new List<DeliverymanAdvanceDto>();
        foreach (var id in createdIds)
        {
            var entity = await _advanceRepository.GetByIdAsync(id, cancellationToken);
            if (entity != null)
                dtos.Add(_mapper.Map<DeliverymanAdvanceDto>(entity));
        }

        return new SettleDeliverymanDayResultDto
        {
            Advances = dtos,
            SurplusApplied = surplus
        };
    }

    private async Task NotifyWorkSessionClosedAsync(
        int deliverymanId,
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken)
    {
        try
        {
            await _fcm.SendToTokensAsync(
                tokens,
                "Jornada finalizada",
                "Tu jornada fue cerrada por liquidación total.",
                new Dictionary<string, string>
                {
                    ["type"] = "work_session_closed",
                    ["reason"] = "total_settlement",
                },
                cancellationToken,
                $"total_settlement:{deliverymanId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "La liquidación se completó, pero no se pudo notificar al domiciliario {DeliverymanId}.",
                deliverymanId);
        }
    }
}
