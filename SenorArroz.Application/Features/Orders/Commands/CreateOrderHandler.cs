using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateOrderHandler(
        IOrderRepository orderRepository, 
        IBankPaymentRepository bankPaymentRepository,
        IAppPaymentRepository appPaymentRepository,
        IMapper mapper, 
        ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _bankPaymentRepository = bankPaymentRepository;
        _appPaymentRepository = appPaymentRepository;
        _mapper = mapper;
        _currentUser = currentUser;
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

        // Validate order type specific requirements
        if (request.Order.Type == Domain.Enums.OrderType.Delivery)
        {
            if (!request.Order.CustomerId.HasValue)
                throw new BusinessException("Los pedidos de domicilio requieren un cliente");
            
            if (!request.Order.AddressId.HasValue)
                throw new BusinessException("Los pedidos de domicilio requieren una dirección");
        }

        if (request.Order.Type == Domain.Enums.OrderType.Reservation)
        {
            if (!request.Order.ReservedFor.HasValue)
                throw new BusinessException("Los pedidos de reserva requieren fecha y hora de entrega");
        }

        var order = _mapper.Map<Order>(request.Order);
        order.BranchId = branchId;
        
        // Configurar estado inicial y timestamps
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

        var createdOrder = await _orderRepository.CreateAsync(order);

        // Crear BankPayments
        if (request.Order.BankPayments != null && request.Order.BankPayments.Any())
        {
            foreach (var bankPaymentDto in request.Order.BankPayments)
            {
                var bankPayment = new Domain.Entities.BankPayment
                {
                    OrderId = createdOrder.Id,
                    BankId = bankPaymentDto.BankId,
                    Amount = bankPaymentDto.Amount
                };
                await _bankPaymentRepository.CreateAsync(bankPayment);
            }
        }

        // Crear AppPayments
        if (request.Order.AppPayments != null && request.Order.AppPayments.Any())
        {
            foreach (var appPaymentDto in request.Order.AppPayments)
            {
                var appPayment = new Domain.Entities.AppPayment
                {
                    OrderId = createdOrder.Id,
                    AppId = appPaymentDto.AppId,
                    Amount = appPaymentDto.Amount,
                    IsSetted = false // Los pagos de app empiezan como no liquidados
                };
                await _appPaymentRepository.CreateAsync(appPayment);
            }
        }

        return _mapper.Map<OrderDto>(createdOrder);
    }
}
