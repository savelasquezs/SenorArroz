using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliveryRouting.DTOs;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Orders.Commands;

public class SelfAssignOrdersHandler : IRequestHandler<SelfAssignOrdersCommand, List<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUserRepository _userRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordService _passwordService;
    private readonly IOrderNotificationService _notificationService;
    private readonly IDeliveryRouteWorkflowService _deliveryRouteWorkflow;
    private readonly IPrintQueueService _printQueue;
    private readonly ILogger<SelfAssignOrdersHandler> _logger;
    private readonly IApplicationDbContext? _db;
    private readonly IDeliveryRoutingPlanService? _routingPlans;

    public SelfAssignOrdersHandler(
        IOrderRepository orderRepository,
        IUserRepository userRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IPasswordService passwordService,
        IOrderNotificationService notificationService,
        IDeliveryRouteWorkflowService deliveryRouteWorkflow,
        IPrintQueueService printQueue,
        ILogger<SelfAssignOrdersHandler> logger,
        IApplicationDbContext? db = null,
        IDeliveryRoutingPlanService? routingPlans = null)
    {
        _orderRepository = orderRepository;
        _userRepository = userRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _passwordService = passwordService;
        _notificationService = notificationService;
        _deliveryRouteWorkflow = deliveryRouteWorkflow;
        _printQueue = printQueue;
        _logger = logger;
        _db = db;
        _routingPlans = routingPlans;
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

        var user = await _userRepository.GetByIdAsync(_currentUser.Id, cancellationToken)
                   ?? throw new BusinessException("Usuario no encontrado");
        if (!_passwordService.VerifyPassword(request.Password, user.PasswordHash))
            throw new BusinessException("Contraseña incorrecta");

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
                if (request.ProposalId.HasValue)
                {
                    proposal = activePlan.Proposals.SingleOrDefault(x => x.Id == request.ProposalId.Value);
                    if (proposal is null || proposal.Status != DeliveryRouteProposalStatus.Available)
                        throw new RoutingPlanStaleException();
                }

                if (proposal is not null)
                {
                    var claimableIds = await _db.DeliveryRouteProposalStops
                        .Where(x => x.DeliveryRouteProposalId == proposal.Id && x.Order.Status == OrderStatus.Ready)
                        .OrderBy(x => x.StopSequence)
                        .Select(x => x.OrderId)
                        .ToListAsync(cancellationToken);
                    if (!claimableIds.SequenceEqual(request.OrderIds))
                        throw new RoutingPlanStaleException("La propuesta cambio o contiene pedidos que aun no estan listos.");
                }
                else
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
            foreach (var orderId in request.OrderIds)
            {
                var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
                            ?? throw new BusinessException($"Pedido {orderId} no encontrado");
                ValidateOrder(order);
                selectedOrders.Add(order);
            }

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
                var persisted = await _orderRepository.GetByIdAsync(order.Id, cancellationToken) ?? fullOrder ?? order;
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

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            foreach (var order in assignedOrders)
                await _notificationService.NotifyOrderAssignedToDelivery(order);
            if (proposal is not null)
                await _notificationService.NotifyRouteProposalClaimed(_currentUser.BranchId, proposal.Id, _currentUser.Id);

            try
            {
                await _printQueue.EnqueueAsync(
                    _currentUser.BranchId,
                    PrintJobKind.Delivery,
                    request.OrderIds,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
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
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "La asignacion se completo, pero no fue posible recalcular el plan de sucursal {BranchId}.",
                        _currentUser.BranchId);
                }
            }

            return assignedOrders;
        }
        catch (Exception ex) when (HasSqlState(ex, "40001") || HasSqlState(ex, "23505"))
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
