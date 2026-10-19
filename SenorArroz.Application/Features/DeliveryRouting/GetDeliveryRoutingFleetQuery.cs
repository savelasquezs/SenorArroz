using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliveryRouting.DTOs;
using SenorArroz.Application.Features.DeliveryRouting.Services;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.DeliveryRouting;

public sealed record GetDeliveryRoutingFleetQuery(int? BranchId) : IRequest<DeliveryRoutingFleetDto>;

public sealed class GetDeliveryRoutingFleetHandler(IBranchContext branchContext,
    IApplicationDbContext db, IDeliverymanAvailabilityService availability, IClock clock)
    : IRequestHandler<GetDeliveryRoutingFleetQuery, DeliveryRoutingFleetDto>
{
    public async Task<DeliveryRoutingFleetDto> Handle(GetDeliveryRoutingFleetQuery request, CancellationToken ct)
    {
        var branchId = branchContext.RequireBranch(request.BranchId);
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == branchId, ct)
            ?? throw new NotFoundException("Sucursal no encontrada.");
        if (!DeliveryRoutingPolicy.Coordinates(branch.Latitude, branch.Longitude))
            throw new BusinessException("Configura las coordenadas de la sucursal para consultar disponibilidad.");
        var now = clock.UtcNow;
        var result = await availability.GetCapacityAsync(branchId,
            (double)branch.Latitude!.Value, (double)branch.Longitude!.Value, now, ct);
        return new DeliveryRoutingFleetDto(now, branchId,
            new DeliveryRoutingCapacityDto(result.AvailableNow, result.AvailableSoon)
            { Couriers = result.Couriers }, result.Warnings);
    }
}
