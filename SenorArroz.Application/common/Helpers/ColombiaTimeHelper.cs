namespace SenorArroz.Application.Common.Helpers;

public static class ColombiaTimeHelper
{
    private static readonly TimeZoneInfo ColombiaTimeZone = 
        TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); // UTC-5

    /// <summary>
    /// Convierte una fecha de hora de Colombia a UTC
    /// </summary>
    public static DateTime ConvertColombiaToUtc(DateTime colombiaDateTime)
    {
        // Asegurar que la fecha no tenga Kind.Utc para poder convertirla
        var unspecified = DateTime.SpecifyKind(colombiaDateTime, DateTimeKind.Unspecified);
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(unspecified, ColombiaTimeZone);
        // Asegurar que el resultado tenga Kind.Utc explícitamente
        return DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
    }

    /// <summary>
    /// Obtiene la fecha/hora actual en Colombia
    /// </summary>
    public static DateTime GetNowInColombia()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ColombiaTimeZone);
    }

    /// <summary>
    /// Obtiene el inicio del día actual en Colombia, convertido a UTC
    /// </summary>
    public static DateTime GetTodayStartInUtc()
    {
        var colombiaToday = GetNowInColombia().Date; // 00:00:00 en Colombia
        var utcStart = ConvertColombiaToUtc(colombiaToday);
        return DateTime.SpecifyKind(utcStart, DateTimeKind.Utc);
    }

    /// <summary>
    /// Obtiene el fin del día actual en Colombia, convertido a UTC
    /// </summary>
    public static DateTime GetTodayEndInUtc()
    {
        var colombiaTodayEnd = GetNowInColombia().Date.AddDays(1).AddTicks(-1); // 23:59:59.999 en Colombia
        var utcEnd = ConvertColombiaToUtc(colombiaTodayEnd);
        return DateTime.SpecifyKind(utcEnd, DateTimeKind.Utc);
    }
}

