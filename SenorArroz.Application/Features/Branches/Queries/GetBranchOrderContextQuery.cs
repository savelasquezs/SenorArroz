using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Branches.DTOs;

namespace SenorArroz.Application.Features.Branches.Queries;

/// <summary>
/// Campos estrictamente necesarios para cotizar/crear pedidos entre sucursales.
/// El DTO completo incluye usuarios y configuraciones administrativas que no deben
/// cruzar sucursales. EF mantiene filtros globales de tenant y PostgreSQL RLS.
/// </summary>
public sealed record GetBranchOrderContextQuery(int Id) : IRequest<BranchDto?>;

public sealed class GetBranchOrderContextHandler(IApplicationDbContext db)
    : IRequestHandler<GetBranchOrderContextQuery, BranchDto?>
{
    public Task<BranchDto?> Handle(GetBranchOrderContextQuery request, CancellationToken cancellationToken) =>
        db.Branches.AsNoTracking()
            .Where(branch => branch.Id == request.Id)
            .Select(branch => new BranchDto
            {
                Id = branch.Id,
                Name = branch.Name,
                Address = branch.Address,
                Latitude = branch.Latitude,
                Longitude = branch.Longitude,
                IsActive = branch.IsActive,
                MaxFreeDeliveryDiscount = branch.MaxFreeDeliveryDiscount,
                PosCopyEtaMinMinutes = branch.PosCopyEtaMinMinutes,
                PosCopyEtaRangeMinutes = branch.PosCopyEtaRangeMinutes
            })
            .SingleOrDefaultAsync(cancellationToken);
}
