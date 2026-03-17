using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Deliverymen.DTOs;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetDeliverymanDaySummaryHandler : IRequestHandler<GetDeliverymanDaySummaryQuery, DeliverymanDaySummaryDto>
{
    private const decimal DefaultBaseAmount = 55000m;

    private readonly IUserRepository _userRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IDeliverymanAdvanceRepository _advanceRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public GetDeliverymanDaySummaryHandler(
        IUserRepository userRepository,
        IOrderRepository orderRepository,
        IDeliverymanAdvanceRepository advanceRepository,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _userRepository = userRepository;
        _orderRepository = orderRepository;
        _advanceRepository = advanceRepository;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<DeliverymanDaySummaryDto> Handle(GetDeliverymanDaySummaryQuery request, CancellationToken cancellationToken)
    {
        var (fromDate, toDate) = ResolveDateRange(request);

        // 1. Validar que el domiciliario existe y es de la sucursal
        var deliveryman = await _userRepository.GetByIdAsync(request.DeliverymanId, cancellationToken);
        if (deliveryman == null)
            throw new BusinessException("El domiciliario no existe");
        if (deliveryman.Role != UserRole.Deliveryman)
            throw new BusinessException("El usuario no es un domiciliario");
        if (!deliveryman.Active)
            throw new BusinessException("El domiciliario no está activo");

        var branchId = _currentUser.Role == "superadmin"
            ? deliveryman.BranchId
            : _currentUser.BranchId;
        if (_currentUser.Role != "superadmin" && deliveryman.BranchId != branchId)
            throw new BusinessException("No tienes permisos para ver los datos de este domiciliario");

        // 2. Obtener pedidos del domiciliario en el rango
        var ordersResult = await _orderRepository.SearchOrdersAsync(
            searchTerm: null,
            branchId: branchId,
            customerId: null,
            deliveryManId: request.DeliverymanId,
            status: OrderStatus.Delivered,
            type: OrderType.Delivery,
            fromDate: fromDate,
            toDate: toDate,
            minAmount: null,
            maxAmount: null,
            page: 1,
            pageSize: 500,
            sortBy: "CreatedAt",
            sortOrder: "desc");

        var orders = ordersResult.Items.ToList();

        // 3. Obtener total de abonos del período
        var totalAdvances = await _advanceRepository.GetTotalAdvancesByDeliverymanAsync(
            request.DeliverymanId,
            fromDate,
            toDate);

        // 4. Calcular stats
        var totalCash = CalculateTotalCash(orders);
        var totalDeliveryFee = orders.Sum(o => o.DeliveryFee ?? 0);
        var avgTime = CalculateAverageDeliveryTimeMinutes(orders);
        var cashToDeliver = totalCash + DefaultBaseAmount - totalAdvances;
        var currentBalance = cashToDeliver;

        var stats = new DeliverymanDayStatsDto
        {
            DeliverymanId = deliveryman.Id,
            DeliverymanName = deliveryman.Name,
            OrdersCount = orders.Count,
            TotalCollected = totalCash,
            TotalAdvances = totalAdvances,
            TotalDeliveryFee = totalDeliveryFee,
            CashToDeliver = cashToDeliver,
            BaseAmount = DefaultBaseAmount,
            CurrentBalance = currentBalance,
            AverageDeliveryTimeMinutes = avgTime
        };

        // 5. Mapear pedidos a DTOs
        var orderDtos = orders.Select(o => _mapper.Map<OrderDto>(o)).ToList();

        return new DeliverymanDaySummaryDto
        {
            Stats = stats,
            Orders = orderDtos
        };
    }

    private static (DateTime from, DateTime to) ResolveDateRange(GetDeliverymanDaySummaryQuery request)
    {
        DateTime fromUtc;
        DateTime toUtc;
        if (request.FromDate.HasValue && request.ToDate.HasValue)
        {
            fromUtc = ColombiaTimeHelper.ConvertColombiaToUtc(request.FromDate.Value);
            var toDate = request.ToDate.Value;
            if (toDate.TimeOfDay == TimeSpan.Zero)
                toDate = toDate.Date.AddDays(1).AddTicks(-1);
            toUtc = ColombiaTimeHelper.ConvertColombiaToUtc(toDate);
        }
        else
        {
            var date = request.Date?.Date ?? ColombiaTimeHelper.GetNowInColombia().Date;
            fromUtc = ColombiaTimeHelper.ConvertColombiaToUtc(date);
            toUtc = ColombiaTimeHelper.ConvertColombiaToUtc(date.AddDays(1).AddTicks(-1));
        }
        return (fromUtc, toUtc);
    }

    private static decimal CalculateTotalCash(List<Order> orders)
    {
        decimal total = 0;
        foreach (var order in orders)
        {
            var bankTotal = order.BankPayments?.Sum(bp => bp.Amount) ?? 0;
            var appTotal = order.AppPayments?.Sum(ap => ap.Amount) ?? 0;
            total += order.Total - bankTotal - appTotal;
        }
        return total;
    }

    private static int CalculateAverageDeliveryTimeMinutes(List<Order> orders)
    {
        if (orders.Count == 0) return 0;
        var totalMs = 0.0;
        var count = 0;
        foreach (var order in orders)
        {
            var statusTimes = order.GetStatusTimes();
            if (statusTimes.TryGetValue("ready", out var readyTime) &&
                statusTimes.TryGetValue("delivered", out var deliveredTime))
            {
                totalMs += (deliveredTime - readyTime).TotalMilliseconds;
                count++;
            }
        }
        if (count == 0) return 0;
        return (int)Math.Round(totalMs / count / 1000 / 60);
    }
}
