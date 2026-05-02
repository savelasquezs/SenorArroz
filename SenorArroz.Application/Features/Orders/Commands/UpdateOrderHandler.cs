using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Kitchen;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class UpdateOrderHandler : IRequestHandler<UpdateOrderCommand, OrderDto>
{
    private const string ModSchedule = "schedule";
    private const string ModContent = "content";
    private const string ModMultiple = "multiple";

    private readonly IOrderRepository _orderRepository;
    private readonly IAddressRepository _addressRepository;
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IReservationDepositRepository _reservationDepositRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderBusinessRulesService _businessRules;
    private readonly IOrderNotificationService _notificationService;
    private readonly IClock _clock;

    public UpdateOrderHandler(
        IOrderRepository orderRepository,
        IAddressRepository addressRepository,
        IBankPaymentRepository bankPaymentRepository,
        IReservationDepositRepository reservationDepositRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IOrderBusinessRulesService businessRules,
        IOrderNotificationService notificationService,
        IClock clock)
    {
        _orderRepository = orderRepository;
        _addressRepository = addressRepository;
        _bankPaymentRepository = bankPaymentRepository;
        _reservationDepositRepository = reservationDepositRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _businessRules = businessRules;
        _notificationService = notificationService;
        _clock = clock;
    }

    public async Task<OrderDto> Handle(UpdateOrderCommand request, CancellationToken cancellationToken)
    {
        var existingOrder = await _orderRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken);
        if (existingOrder == null)
            throw new BusinessException("Pedido no encontrado");

        // Validate branch access
        if (!Roles.IsSuperadmin(_currentUser.Role) && existingOrder.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        // Validar si puede actualizar el pedido
        if (!_businessRules.CanUpdateOrder(existingOrder, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar este pedido en su estado actual");

        // Validar si puede modificar productos
        if (request.Order.OrderDetails != null && !_businessRules.CanUpdateOrderProducts(existingOrder, _currentUser.Role))
            throw new BusinessException("No tienes permisos para modificar los productos de este pedido");

        if (request.Order.DeleteReservationAssociatedPayments && existingOrder.Type == OrderType.Reservation)
            await DeleteReservationAssociatedPaymentsAsync(existingOrder.Id, cancellationToken);

        var beforeReservedFor = existingOrder.ReservedFor;
        var beforePrepareAt = existingOrder.PrepareAt;
        var beforeNotes = existingOrder.Notes;

        IReadOnlyList<DetailSnap>? lineSnapshotBefore = null;
        if (request.Order.OrderDetails != null)
            lineSnapshotBefore = KitchenOrderModificationDiff.SnapshotFromOrder(existingOrder);

        // Validar prepare_at <= reserved_for cuando ambos tienen valor
        if (request.Order.PrepareAt.HasValue && request.Order.ReservedFor.HasValue
            && request.Order.PrepareAt.Value > request.Order.ReservedFor.Value)
        {
            throw new BusinessException("La hora de preparación no puede ser posterior a la hora de entrega");
        }

        // Validar distancia mínima de 40 min entre preparación y entrega
        if (request.Order.PrepareAt.HasValue && request.Order.ReservedFor.HasValue)
        {
            var diff = request.Order.ReservedFor.Value - request.Order.PrepareAt.Value;
            if (diff.TotalMinutes < 40)
            {
                throw new BusinessException("Debe haber al menos 40 minutos entre la hora de preparación y la hora de entrega");
            }
        }

        // Cambio automático a Reservation: si se pone reserved_for con valor futuro
        if (request.Order.ReservedFor.HasValue && request.Order.ReservedFor.Value > _clock.UtcNow)
        {
            existingOrder.Type = OrderType.Reservation;
        }

        // Apply scalar field mapping first (details are handled explicitly below)
        _mapper.Map(request.Order, existingOrder);

        if (request.Order.OrderDetails != null)
        {
            if (!request.Order.OrderDetails.Any())
            {
                throw new BusinessException("El pedido debe tener al menos un producto. Si es el último, cancela el pedido.");
            }

            var incomingById = request.Order.OrderDetails
                .Where(d => d.Id > 0)
                .ToDictionary(d => d.Id);

            var detailsToRemove = existingOrder.OrderDetails
                .Where(d => d.Id > 0 && !incomingById.ContainsKey(d.Id))
                .ToList();

            foreach (var detailToRemove in detailsToRemove)
            {
                existingOrder.OrderDetails.Remove(detailToRemove);
            }

            foreach (var incoming in request.Order.OrderDetails)
            {
                var lineSubtotal = incoming.Quantity * incoming.UnitPrice - incoming.Discount;
                if (incoming.Id > 0)
                {
                    var existingDetail = existingOrder.OrderDetails.FirstOrDefault(d => d.Id == incoming.Id);
                    if (existingDetail == null)
                    {
                        continue;
                    }

                    existingDetail.ProductId = incoming.ProductId;
                    existingDetail.Quantity = incoming.Quantity;
                    existingDetail.UnitPrice = incoming.UnitPrice;
                    existingDetail.Discount = incoming.Discount;
                    existingDetail.Notes = incoming.Notes;
                    existingDetail.Subtotal = lineSubtotal;
                }
                else
                {
                    existingOrder.OrderDetails.Add(new Domain.Entities.OrderDetail
                    {
                        ProductId = incoming.ProductId,
                        Quantity = incoming.Quantity,
                        UnitPrice = incoming.UnitPrice,
                        Discount = incoming.Discount,
                        Notes = incoming.Notes,
                        Subtotal = lineSubtotal
                    });
                }
            }
        }

        // Recalcular prepare_at solo si en esta petición se envió reserved_for y no prepare_at (evita pisar horarios al cambiar solo el tipo)
        if (request.Order.ReservedFor is { } rfFromRequest && !request.Order.PrepareAt.HasValue)
        {
            existingOrder.PrepareAt = rfFromRequest.AddHours(-1);
        }

        // Handle order type changes - clear delivery fields when changing to Onsite
        if (request.Order.Type.HasValue)
        {
            if (request.Order.Type == OrderType.Onsite)
            {
                existingOrder.AddressId = null;
                existingOrder.DeliveryFee = null;
                existingOrder.DeliveryManId = null;
            }
        }

        // Handle address changes - update delivery fee from address if not provided
        if (request.Order.AddressId.HasValue && !request.Order.DeliveryFee.HasValue)
        {
            var address = await _addressRepository.GetByIdAsync(request.Order.AddressId.Value, cancellationToken);
            if (address != null)
            {
                existingOrder.DeliveryFee = address.DeliveryFee;
            }
        }

        var scheduleChanged = !NullableUtcInstantEquals(beforeReservedFor, existingOrder.ReservedFor)
            || !NullableUtcInstantEquals(beforePrepareAt, existingOrder.PrepareAt);

        if (scheduleChanged && existingOrder.Status == OrderStatus.Taken)
        {
            if (existingOrder.PrepareAt.HasValue && existingOrder.PrepareAt.Value <= _clock.UtcNow)
                existingOrder.PreparedNotifiedAt = _clock.UtcNow;
            else
                existingOrder.PreparedNotifiedAt = null;
        }

        OrderTotalsHelper.RecalculateFromOrderDetails(existingOrder);

        await _orderRepository.UpdateAsync(existingOrder, cancellationToken);
        var persisted = await _orderRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken)
            ?? throw new InvalidOperationException("No se pudo recargar el pedido tras actualizar.");
        var result = _mapper.Map<OrderDto>(persisted);

        var notesChanged = request.Order.Notes != null
            && !string.Equals(beforeNotes ?? "", persisted.Notes ?? "", StringComparison.Ordinal);
        var productsChanged = request.Order.OrderDetails != null;

        string? modificationKind = (scheduleChanged, notesChanged, productsChanged) switch
        {
            (true, true, _) or (true, _, true) or (_, true, true) => ModMultiple,
            (true, false, false) => ModSchedule,
            _ when notesChanged || productsChanged => ModContent,
            _ => null,
        };

        if (modificationKind != null)
        {
            KitchenOrderModificationSummary kitchenChanges;
            if (lineSnapshotBefore != null)
            {
                var afterSnap = KitchenOrderModificationDiff.SnapshotFromOrder(persisted);
                kitchenChanges = KitchenOrderModificationDiff.Build(lineSnapshotBefore, afterSnap);
            }
            else
            {
                kitchenChanges = new KitchenOrderModificationSummary();
            }

            kitchenChanges.ScheduleChanged = scheduleChanged;
            kitchenChanges.NotesChanged = notesChanged;

            var utc = _clock.UtcNow;
            if (KitchenOrderNotificationEligibility.IsVisibleToActiveKitchen(persisted, utc)
                && persisted.Status is OrderStatus.Taken or OrderStatus.InPreparation)
            {
                await _notificationService.NotifyOrderModifiedToKitchen(result, modificationKind, kitchenChanges);
            }

            if (persisted.Status == OrderStatus.OnTheWay)
                await _notificationService.NotifyOrderModifiedToDelivery(result, modificationKind, kitchenChanges);
        }

        return result;
    }

    private static bool NullableUtcInstantEquals(DateTime? a, DateTime? b)
    {
        if (a.HasValue != b.HasValue)
            return false;
        if (!a.HasValue)
            return true;
        var av = a.GetValueOrDefault();
        var bv = b.GetValueOrDefault();
        return DateTime.Equals(NormalizeToUtc(av), NormalizeToUtc(bv));
    }

    private static DateTime NormalizeToUtc(DateTime dt) =>
        dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        };

    private async Task DeleteReservationAssociatedPaymentsAsync(int orderId, CancellationToken cancellationToken)
    {
        var bankPayments = await _bankPaymentRepository.GetByOrderIdAsync(orderId, cancellationToken);
        foreach (var bankPayment in bankPayments)
            await _bankPaymentRepository.DeleteAsync(bankPayment.Id, cancellationToken);

        await _reservationDepositRepository.DeleteByOrderIdAsync(orderId, cancellationToken);
    }
}
