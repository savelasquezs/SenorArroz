using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Application.Features.Deliverymen.Queries;

public class GetDeliverymanLastLocationQuery : IRequest<DeliverymanLastLocationDto?>
{
    public int DeliverymanId { get; set; }
}

public class DeliverymanLastLocationDto
{
    public int DeliverymanId { get; set; }
    public int? DeliveryRouteId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime RecordedAt { get; set; }
}

public class GetDeliverymanLastLocationHandler
    : IRequestHandler<GetDeliverymanLastLocationQuery, DeliverymanLastLocationDto?>
{
    private readonly IApplicationDbContext _db;

    public GetDeliverymanLastLocationHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DeliverymanLastLocationDto?> Handle(
        GetDeliverymanLastLocationQuery request,
        CancellationToken cancellationToken)
    {
        var loc = await _db.DeliverymanLocations
            .Where(l => l.DeliverymanId == request.DeliverymanId)
            .OrderByDescending(l => l.RecordedAt)
            .Select(l => new DeliverymanLastLocationDto
            {
                DeliverymanId = l.DeliverymanId,
                DeliveryRouteId = l.DeliveryRouteId,
                Latitude = (double)l.Latitude,
                Longitude = (double)l.Longitude,
                RecordedAt = l.RecordedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return loc;
    }
}
