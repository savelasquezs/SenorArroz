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

public static class BranchDeliveryAutoCompletionSettingsValidator
{
    public static void EnsureValid(int arrivalRadiusMeters, int departureRadiusMeters, int minPresenceSeconds)
    {
        if (arrivalRadiusMeters is < 10 or > 150)
            throw new BusinessException("El radio de llegada debe estar entre 10 y 150 metros.");
        if (departureRadiusMeters is < 20 or > 500)
            throw new BusinessException("El radio de salida debe estar entre 20 y 500 metros.");
        if (departureRadiusMeters <= arrivalRadiusMeters)
            throw new BusinessException("El radio de salida debe ser mayor que el radio de llegada.");
        if (minPresenceSeconds is < 5 or > 300)
            throw new BusinessException("La permanencia minima debe estar entre 5 y 300 segundos.");
    }
}
