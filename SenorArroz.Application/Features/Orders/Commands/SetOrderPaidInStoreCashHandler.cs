using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Orders.Commands;

public class SetOrderPaidInStoreCashHandler : IRequestHandler<SetOrderPaidInStoreCashCommand, OrderDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IOrderRepository _orderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public SetOrderPaidInStoreCashHandler(
        IApplicationDbContext context,
        IOrderRepository orderRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _context = context;
        _orderRepository = orderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<OrderDto> Handle(SetOrderPaidInStoreCashCommand request, CancellationToken cancellationToken)
    {
        var role = _currentUser.Role.ToLowerInvariant();
        if (!Roles.IsSuperadminOrAdminOrCashier(role))
            throw new BusinessException("No tienes permisos para marcar cobro en tienda");

        var order = await _context.Orders
            .Include(o => o.BankPayments)
            .Include(o => o.AppPayments)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order == null)
            throw new BusinessException("Pedido no encontrado");

        if (order.Status == OrderStatus.Cancelled)
            throw new BusinessException("No se puede modificar un pedido cancelado");

        if (!Roles.IsSuperadmin(role) && order.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para modificar pedidos de esta sucursal");

        OrderPaidInStoreCashHelper.Apply(order, request.PaidInStoreCash, DateTime.UtcNow);

        await _context.SaveChangesAsync(cancellationToken);

        var full = await _orderRepository.GetByIdWithFullDetailsAsync(request.OrderId, cancellationToken);
        if (full == null)
            throw new BusinessException("Pedido no encontrado");

        return _mapper.Map<OrderDto>(full);
    }
}
