using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Orders.Queries;

public sealed class GetPreparationOrdersNearBranchHandler
    : IRequestHandler<GetPreparationOrdersNearBranchQuery, List<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetPreparationOrdersNearBranchHandler(
        IApplicationDbContext context,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _context = context;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<List<OrderDto>> Handle(
        GetPreparationOrdersNearBranchQuery request,
        CancellationToken cancellationToken)
    {
        if (!Roles.IsDeliveryman(_currentUser.Role))
            throw new BusinessException("Solo los domiciliarios pueden consultar estos pedidos");

        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            throw new BusinessException("La ubicación enviada no es válida");

        var branch = await _context.Branches
            .AsNoTracking()
            .Where(x => x.Id == _currentUser.BranchId)
            .Select(x => new
            {
                x.Latitude,
                x.Longitude,
                x.DeliveryTrackingAllowedDistanceMeters,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (branch is null)
            throw new BusinessException("No se encontró la sucursal del domiciliario");

        if (branch.Latitude is null || branch.Longitude is null)
            throw new BusinessException("La sucursal no tiene una ubicación configurada");

        var allowedDistanceMeters = Math.Max(1, branch.DeliveryTrackingAllowedDistanceMeters);
        var isInside = DeliveryPreparationLocationGate.IsInside(
            request.Latitude,
            request.Longitude,
            branch.Latitude.Value,
            branch.Longitude.Value,
            allowedDistanceMeters,
            out var distanceMeters);

        if (!isInside)
        {
            throw new BusinessException(
                $"Debes estar en la sucursal para ver estos pedidos. Distancia actual: {Math.Round(distanceMeters)} m");
        }

        var orders = await _context.Orders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Branch)
            .Include(x => x.TakenBy)
            .Include(x => x.Customer)
            .Include(x => x.Address)
                .ThenInclude(x => x!.Neighborhood)
            .Include(x => x.LoyaltyCycleStep)
            .Include(x => x.DeliveryMan)
            .Include(x => x.OrderDetails)
                .ThenInclude(x => x.Product)
            .Where(x => x.BranchId == _currentUser.BranchId
                && x.Type == OrderType.Delivery
                && (x.Status == OrderStatus.Taken || x.Status == OrderStatus.InPreparation))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return _mapper.Map<List<OrderDto>>(orders);
    }
}
