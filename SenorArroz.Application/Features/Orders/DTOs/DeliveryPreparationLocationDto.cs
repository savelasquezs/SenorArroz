using System.ComponentModel.DataAnnotations;

namespace SenorArroz.Application.Features.Orders.DTOs;

public sealed class DeliveryPreparationLocationDto
{
    [Range(-90, 90)]
    public decimal Latitude { get; set; }

    [Range(-180, 180)]
    public decimal Longitude { get; set; }
}
