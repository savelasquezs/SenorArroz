using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliveryRouting.DTOs;
using SenorArroz.Application.Features.DeliveryRouting.Services;
using SenorArroz.Application.Options;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class SelfAssignOrdersHandler : IRequestHandler<SelfAssignOrdersCommand, List<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderNotificationService _notificationService;
    private readonly IDeliveryRouteWorkflowService _deliveryRouteWorkflow;
    private readonly IPrintQueueService _printQueue;
    private readonly ILogger<SelfAssignOrdersHandler> _logger;
    private readonly IApplicationDbContext? _db;
    private readonly IDeliveryRoutingPlanService? _routingPlans;
    private readonly IClock? _clock;
    private readonly DeliveryRoutingOptions _routingOptions;

    public SelfAssignOrdersHandler(
        IOrderRepository orderRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IOrderNotificationService notificationService,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow,
        IPrintQueueService printQueue,
        ILogger<SelfAssignOrdersHandler> logger,
        IApplicationDbContext? db = null,
        IDeliveryRoutingPlanService? routingPlans = null, IClock? clock = null,
        IOptions<DeliveryRoutingOptions>? routingOptions = null)
    {
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _notificationService = notificationService;
        _deliveryRouteWorkflow = deliveryRouteWorkflow;
        _printQueue = printQueue;
        _logger = logger;
        _db = db;
        _routingPlans = routingPlans;
        _clock = clock;
        _routingOptions = routingOptions?.Value ?? new DeliveryRoutingOptions();
    }

    public async Task<List<OrderDto>> Handle(SelfAssignOrdersCommand request, CancellationToken cancellationToken)
    {
        if (!Roles.IsDeliveryman(_currentUser.Role))
            throw new BusinessException("Solo los domiciliarios pueden autoasignarse pedidos");
        if (!_currentUser.IsAuthenticated)
            throw new BusinessException("Usuario no autenticado");
        if (request.OrderIds.Count == 0)
            throw new BusinessException("Selecciona al menos un pedido");
        if (request.OrderIds.Count != request.OrderIds.Distinct().Count())
            throw new BusinessException("La seleccion contiene pedidos duplicados");
        if (request.ModifiedProposal && (!request.ProposalId.HasValue || !request.ExpectedPlanVersion.HasValue
            || string.IsNullOrWhiteSpace(request.SelectionFingerprint)))
            throw new RoutingPlanStaleException("Actualiza la vista previa antes de aceptar la selección.");

        IDbContextTransaction? transaction = null;
        if (_db?.Database.IsRelational() == true)
            transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        DeliveryRoutingPlan? activePlan = null;
        DeliveryRouteProposal? proposal = null;
        try
        {
            if (request.ProposalId.HasValue || request.ExpectedPlanVersion.HasValue)
            {
                if (_db is null || !request.ExpectedPlanVersion.HasValue)
                    throw new RoutingPlanStaleException();
                activePlan = await _db.DeliveryRoutingPlans
                    .Include(x => x.Proposals)
                    .SingleOrDefaultAsync(
                        x => x.BranchId == _currentUser.BranchId
                             && x.Status == DeliveryRoutingPlanStatus.Active,
                        cancellationToken);
                if (activePlan is null || activePlan.GenerationNumber != request.ExpectedPlanVersion.Value)
                    throw new RoutingPlanStaleException();
                var now = _clock?.UtcNow ?? DateTime.UtcNow;
                if (activePlan.GeneratedAtUtc > now || now - activePlan.GeneratedAtUtc
                    > TimeSpan.FromSeconds(Math.Clamp(_routingOptions.PlanMaxAgeSeconds, 30, 300)))
                    throw new RoutingPlanStaleException();
                if (request.ProposalId.HasValue)
                {
                    proposal = activePlan.Proposals.SingleOrDefault(x => x.Id == request.ProposalId.Value);
                    if (proposal is null || proposal.Status != DeliveryRouteProposalStatus.Available)
                        throw new RoutingPlanStaleException();
                }

                if (proposal is not null && !request.ModifiedProposal)
                {
                    var claimableIds = await _db.DeliveryRouteProposalStops
                        .Where(x => x.DeliveryRouteProposalId == proposal.Id && x.Order.Status == OrderStatus.Ready)
                        .OrderBy(x => x.StopSequence)
                        .Select(x => x.OrderId)
                        .ToListAsync(cancellationToken);
                    if (!claimableIds.SequenceEqual(request.OrderIds))
                        throw new RoutingPlanStaleException("La propuesta cambio o contiene pedidos que aun no estan listos.");
                }
                else if (proposal is null)
                {
                    var plannedCount = await _db.DeliveryRouteProposalStops
                        .CountAsync(
                            x => x.DeliveryRoutingPlanId == activePlan.Id && request.OrderIds.Contains(x.OrderId),
                            cancellationToken);
                    if (plannedCount != request.OrderIds.Count)
                        throw new RoutingPlanStaleException("Uno o mas pedidos ya no pertenecen al plan activo.");
                }
            }

            var selectedOrders = new List<Order>(request.OrderIds.Count);
            var loaded = _db is not null ? await _db.Orders.Include(o => o.Address)
                .Where(o => request.OrderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken) : null;
            foreach (var orderId in request.OrderIds)
            {
                var order = loaded is not null ? loaded.GetValueOrDefault(orderId)
                    : await _orderRepository.GetByIdAsync(orderId, cancellationToken);
                order = order
                            ?? throw new BusinessException($"Pedido {orderId} no encontrado");
                ValidateOrder(order);
                selectedOrders.Add(order);
            }
            if (request.ModifiedProposal && DeliveryRoutingPolicy.SelectionFingerprint(selectedOrders) != request.SelectionFingerprint)
                throw new RoutingPlanStaleException("La selección cambió después de calcularla. Actualiza la vista previa.");

            if (await _deliveryRouteWorkflow.DeliverymanHasPendingOrdersOnActiveRouteAsync(
                    _currentUser.Id,
                    _currentUser.BranchId,
                    cancellationToken,
                    request.OrderIds))
            {
                throw new BusinessException(
                    "Termina o entrega los pedidos de tu ruta actual antes de tomar mas. Si necesitas otro pedido urgente, pide a caja que te lo asigne.");
            }

            var assignedOrders = new List<OrderDto>(selectedOrders.Count);
            foreach (var order in selectedOrders)
            {
                await _orderRepository.AssignDeliveryManAsync(order.Id, _currentUser.Id, cancellationToken);
                await _orderRepository.ChangeStatusAsync(order.Id, OrderStatus.OnTheWay, null, cancellationToken);
                var fullOrder = await _orderRepository.GetByIdAsync(order.Id, cancellationToken);
                if (fullOrder is not null)
                    await _deliveryRouteWorkflow.OnOrderAssignedToDeliverymanAsync(fullOrder, cancellationToken);
                // The response doubles as the live route snapshot; include payments and product lines.
                var persisted = await _orderRepository.GetByIdWithFullDetailsAsync(order.Id, cancellationToken) ?? fullOrder ?? order;
                assignedOrders.Add(_mapper.Map<OrderDto>(persisted));
            }

            if (_db is not null)
            {
                activePlan ??= await _db.DeliveryRoutingPlans
                    .Include(x => x.Proposals)
                    .Where(x => x.BranchId == _currentUser.BranchId && x.Status == DeliveryRoutingPlanStatus.Active)
                    .OrderByDescending(x => x.GenerationNumber)
                    .FirstOrDefaultAsync(cancellationToken);
                if (activePlan is not null)
                {
                    proposal ??= request.ProposalId.HasValue
                        ? activePlan.Proposals.SingleOrDefault(x => x.Id == request.ProposalId.Value)
                        : null;
                    if (proposal is not null)
                    {
                        proposal.Status = DeliveryRouteProposalStatus.Claimed;
                        proposal.ClaimedByDeliverymanId = _currentUser.Id;
                        proposal.ClaimedAtUtc = DateTime.UtcNow;
                    }
                    activePlan.Status = DeliveryRoutingPlanStatus.Superseded;
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            if (_db is not null && proposal is not null)
            {
                var stops = await _db.DeliveryRouteStops.Where(s => request.OrderIds.Contains(s.OrderId))
                    .ToListAsync(cancellationToken);
                foreach (var stop in stops) stop.StopSequence = request.OrderIds.IndexOf(stop.OrderId) + 1;
                var routeId = stops.Select(s => (int?)s.DeliveryRouteId).FirstOrDefault();
                if (routeId.HasValue)
                    _db.EntityAuditLogs.Add(new EntityAuditLog
                    {
                        BranchId = _currentUser.BranchId, EntityType = "delivery_route", EntityId = routeId.Value,
                        OperationType = "proposal_accepted", ChangedAt = _clock?.UtcNow ?? DateTime.UtcNow,
                        BusinessDate = (_clock?.UtcNow ?? DateTime.UtcNow).AddHours(-5).Date,
                        ChangedByUserId = _currentUser.Id, SummaryText = $"Propuesta #{proposal.Id} aceptada",
                        MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                        { proposalId = proposal.Id, request.ExpectedPlanVersion, request.ModifiedProposal, orderIds = request.OrderIds })
                    });
                await _db.SaveChangesAsync(cancellationToken);
            }
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            if (selectedOrders.Count > 0)
            {
                var accepted = selectedOrders[0];
                var observedAt = _clock?.UtcNow ?? DateTime.UtcNow;
                RoutingMeasurement.Observe(_logger, _routingOptions.Measurement, observedAt, accepted.TenantId,
                    accepted.BranchId, "accepted", accepted.DeliveryRouteId, proposalId: proposal?.Id,
                    orderIds: request.OrderIds.ToArray(), modified: request.ModifiedProposal);
                foreach (var selected in selectedOrders)
                    if (selected.GetStatusTimes().TryGetValue("ready", out var readyAt))
                        RoutingMeasurement.Observe(_logger, _routingOptions.Measurement, observedAt, selected.TenantId,
                            selected.BranchId, "ready", selected.DeliveryRouteId, selected.Id, occurredAtUtc: readyAt);
            }

            // The assignment is already committed: notification failures must not
            // suggest to the mobile app that it failed (and invite a retry).
            foreach (var order in assignedOrders)
            {
                try
                {
                    await _notificationService.NotifyOrderAssignedToDelivery(order);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Pedido {OrderId} asignado, pero fallo su notificacion.", order.Id);
                }
            }
            if (proposal is not null)
            {
                try
                {
                    await _notificationService.NotifyRouteProposalClaimed(_currentUser.BranchId, proposal.Id, _currentUser.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Ruta {ProposalId} tomada, pero fallo su notificacion.", proposal.Id);
                }
            }

            try
            {
                await _printQueue.EnqueueAsync(
                    _currentUser.BranchId,
                    PrintJobKind.Delivery,
                    request.OrderIds,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "No se encolo ticket de domicilio para pedidos {OrderIds}. La asignacion se completo.",
                    string.Join(',', request.OrderIds));
            }

            if (_routingPlans is not null)
            {
                try
                {
                    await _routingPlans.RecalculateAsync(_currentUser.BranchId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "La asignacion se completo, pero no fue posible recalcular el plan de sucursal {BranchId}.",
                        _currentUser.BranchId);
                }
            }

            return assignedOrders;
        }
        catch (Exception ex) when (HasSqlState(ex, "40001") || HasSqlState(ex, "23505") || HasSqlState(ex, "40P01")
            || ex is DbUpdateConcurrencyException)
        {
            throw new RoutingPlanStaleException("Otro domiciliario tomo la propuesta primero. Actualiza el plan.");
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private static bool HasSqlState(Exception exception, string expected)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().GetProperty("SqlState")?.GetValue(current) as string == expected)
                return true;
        }
        return false;
    }

    private void ValidateOrder(Order order)
    {
        if (order.Type != OrderType.Delivery && !(order.Type == OrderType.Reservation && order.AddressId.HasValue))
            throw new BusinessException($"El pedido {order.Id} no es un domicilio interno elegible");
        if (!string.IsNullOrWhiteSpace(order.ExternalFulfillmentProvider))
            throw new BusinessException($"El pedido {order.Id} es entregado por {order.ExternalFulfillmentProvider}");
        if (order.BranchId != _currentUser.BranchId)
            throw new BusinessException($"No tienes permisos para asignarte pedidos de la sucursal {order.BranchId}");
        if (order.Status != OrderStatus.Ready)
            throw new RoutingPlanStaleException($"El pedido {order.Id} ya no esta Ready para salir");
        if (order.DeliveryManId.HasValue || order.DeliveryRouteId.HasValue)
            throw new RoutingPlanStaleException($"El pedido {order.Id} ya fue tomado");
    }
}
