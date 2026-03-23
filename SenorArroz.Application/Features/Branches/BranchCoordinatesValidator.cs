using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Branches;

public static class BranchCoordinatesValidator
{
    public static void EnsureValid(decimal? latitude, decimal? longitude)
    {
        if (latitude is null || longitude is null)
            throw new BusinessException("Debes seleccionar la ubicación de la sucursal en el mapa.");

        if (latitude < -90 || latitude > 90)
            throw new BusinessException("La latitud debe estar entre -90 y 90.");

        if (longitude < -180 || longitude > 180)
            throw new BusinessException("La longitud debe estar entre -180 y 180.");
    }
}
