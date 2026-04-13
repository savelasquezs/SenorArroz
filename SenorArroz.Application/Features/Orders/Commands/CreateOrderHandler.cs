using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IApplicationDbContext _db;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderHandler(
        IOrderRepository orderRepository,
        IApplicationDbContext db,
        IMapper mapper,
        ICurrentUser currentUser,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _db = db;
        _mapper = mapper;
        _currentUser = currentUser;
        _notificationService = notificationService;
    }

    public async Task<OrderDto> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // Determine branch based on user role
        int branchId;

        if (_currentUser.Role == "superadmin")
        {
            // Superadmin can specify branch or needs to provide it
            if (request.Order.BranchId <= 0)
            {
                throw new BusinessException("Superadmin debe especificar la sucursal");
            }
            branchId = request.Order.BranchId;
        }
        else if (_currentUser.Role == "admin" || _currentUser.Role == "cashier")
        {
            // Admin and Cashier use their branch
            branchId = _currentUser.BranchId;
        }
        else
        {
            throw new BusinessException("No tienes permisos para crear pedidos");
        }

        // Validar que el pedido tenga al menos un producto
        if (request.Order.OrderDetails == null || !request.Order.OrderDetails.Any())
            throw new BusinessException("El pedido debe tener al menos un producto");

        // Validate order type specific requirements
        if (request.Order.Type == Domain.Enums.OrderType.Delivery)
        {
            if (!request.Order.CustomerId.HasValue)
                throw new BusinessException("Los pedidos de domicilio requieren un cliente");
            
            if (!request.Order.AddressId.HasValue)
                throw new BusinessException("Los pedidos de domicilio requieren una dirección");
            
            if (string.IsNullOrWhiteSpace(request.Order.GuestName))
                throw new BusinessException("Los pedidos de domicilio requieren el nombre del invitado");
        }

        if (request.Order.Type == Domain.Enums.OrderType.Reservation)
        {
            if (!request.Order.ReservedFor.HasValue)
                throw new BusinessException("Los pedidos de reserva requieren fecha y hora de entrega");
            
            if (string.IsNullOrWhiteSpace(request.Order.GuestName))
                throw new BusinessException("Los pedidos de reserva requieren el nombre del invitado");
        }

        // Validar prepare_at <= reserved_for cuando ambos tienen valor
        if (request.Order.PrepareAt.HasValue && request.Order.ReservedFor.HasValue
            && request.Order.PrepareAt.Value > request.Order.ReservedFor.Value)
        {
            throw new BusinessException("La hora de preparación no puede ser posterior a la hora de entrega");
        }

        var order = _mapper.Map<Order>(request.Order);

        // prepare_at por defecto: reserved_for - 1h si null y hay reserved_for
        if (order.ReservedFor.HasValue && !order.PrepareAt.HasValue)
        {
            order.PrepareAt = order.ReservedFor.Value.AddHours(-1);
        }
        
        // Configurar valores obligatorios inmediatamente después del mapeo
        order.BranchId = branchId;
        order.Status = Domain.Enums.OrderStatus.Taken;
        order.AddStatusTime(Domain.Enums.OrderStatus.Taken, DateTime.UtcNow);

        // Mapear y agregar OrderDetails
        if (request.Order.OrderDetails != null && request.Order.OrderDetails.Any())
        {
            var orderDetails = _mapper.Map<List<Domain.Entities.OrderDetail>>(request.Order.OrderDetails);
            foreach (var detail in orderDetails)
            {
               
                order.OrderDetails.Add(detail);
            }
        }

        var createdOrder = await _orderRepository.CreateAsync(order, cancellationToken);

        // Batch insert de pagos: un único SaveChangesAsync en lugar de N roundtrips individuales
        var bankPayments = request.Order.BankPayments?
            .Select(bp => new Domain.Entities.BankPayment
            {
                OrderId = createdOrder.Id,
                BankId = bp.BankId,
                Amount = bp.Amount,
                IsVerified = false
            }).ToList() ?? [];

        var appPayments = request.Order.AppPayments?
            .Select(ap => new Domain.Entities.AppPayment
            {
                OrderId = createdOrder.Id,
                AppId = ap.AppId,
                Amount = ap.Amount,
                IsSetted = false
            }).ToList() ?? [];

        if (bankPayments.Count > 0)
            _db.BankPayments.AddRange(bankPayments);

        if (appPayments.Count > 0)
            _db.AppPayments.AddRange(appPayments);

        if (bankPayments.Count > 0 || appPayments.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        if (request.Order.PaidInStoreCash)
        {
            var role = _currentUser.Role.ToLowerInvariant();
            if (role is not ("cashier" or "admin" or "superadmin"))
                throw new BusinessException("No tienes permisos para marcar cobro en tienda en la creación del pedido");

            var tracked = await _db.Orders
                .Include(o => o.BankPayments)
                .Include(o => o.AppPayments)
                .FirstOrDefaultAsync(o => o.Id == createdOrder.Id, cancellationToken);
            if (tracked == null)
                throw new BusinessException("Pedido no encontrado tras crear");

            OrderPaidInStoreCashHelper.Apply(tracked, true, DateTime.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var fullOrder = await _orderRepository.GetByIdWithFullDetailsAsync(createdOrder.Id, cancellationToken);
        if (fullOrder == null)
            throw new BusinessException("Pedido no encontrado");

        var result = _mapper.Map<OrderDto>(fullOrder);

        // Notificar a cocina: pedido inmediato (sin reserved_for) o prepare_at ya pasó
        var now = DateTime.UtcNow;
        var shouldNotifyNow = !fullOrder.ReservedFor.HasValue
            || (fullOrder.PrepareAt.HasValue && fullOrder.PrepareAt.Value <= now);
        if (shouldNotifyNow)
        {
            await _notificationService.NotifyNewOrderToKitchen(result);
        }

        return result;
    }
}
