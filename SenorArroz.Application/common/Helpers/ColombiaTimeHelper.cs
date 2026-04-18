using System.Globalization;

namespace SenorArroz.Application.Common.Helpers;

public static class ColombiaTimeHelper
{
    private static readonly Lazy<TimeZoneInfo> ColombiaTimeZoneLazy = new(ResolveColombiaTimeZone);

    private static TimeZoneInfo ColombiaTimeZone => ColombiaTimeZoneLazy.Value;

    private static TimeZoneInfo ResolveColombiaTimeZone()
    {
        foreach (var id in new[] { "America/Bogota", "SA Pacific Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "Colombia-UTC-5",
            TimeSpan.FromHours(-5),
            "Colombia (UTC-5)",
            "Colombia (UTC-5)");
    }

    public static DateTime EnsureUtc(DateTime dt)
    {
        return dt.Kind == DateTimeKind.Utc
            ? dt
            : DateTime.SpecifyKind(dt.ToUniversalTime(), DateTimeKind.Utc);
    }

    /// <summary>
    /// Convierte una fecha de hora de Colombia a UTC
    /// </summary>
    public static DateTime ConvertColombiaToUtc(DateTime colombiaDateTime)
    {
        var unspecified = DateTime.SpecifyKind(colombiaDateTime, DateTimeKind.Unspecified);
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(unspecified, ColombiaTimeZone);
        return DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
    }

    /// <summary>
    /// Convierte un instante UTC a hora local Colombia (útil con <see cref="SenorArroz.Application.Common.Interfaces.IClock"/>).
    /// </summary>
    public static DateTime GetNowInColombiaFromUtc(DateTime utcNow)
    {
        var utc = EnsureUtc(utcNow);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, ColombiaTimeZone);
    }

    /// <summary>
    /// Obtiene la fecha/hora actual en Colombia
    /// </summary>
    public static DateTime GetNowInColombia() => GetNowInColombiaFromUtc(DateTime.UtcNow);

    /// <summary>
    /// Medianoche UTC del día UTC de <c>utcNow - 5 horas</c>, alineada con
    /// <c>date_trunc('day', prepare_at::timestamptz + INTERVAL '-5 hours', 'UTC')</c> en PostgreSQL/EF.
    /// El resultado tiene <see cref="DateTimeKind.Utc"/> (Npgsql no admite <c>Unspecified</c> en <c>timestamptz</c>).
    /// </summary>
    public static DateTime GetPrepareAtSqlTruncDayUtc(DateTime utcNow) =>
        EnsureUtc(utcNow).AddHours(-5).Date;

    /// <summary>Fecha calendario de operación en Colombia (día actual local).</summary>
    public static DateOnly GetTodayDateOnlyColombiaFromUtc(DateTime utcNow) =>
        DateOnly.FromDateTime(GetNowInColombiaFromUtc(utcNow).Date);

    /// <summary>Fecha calendario de operación en Colombia (día actual local).</summary>
    public static DateOnly GetTodayDateOnlyColombia() => GetTodayDateOnlyColombiaFromUtc(DateTime.UtcNow);

    /// <summary>
    /// Obtiene el inicio del día actual en Colombia, convertido a UTC
    /// </summary>
    public static DateTime GetTodayStartInUtcFromUtc(DateTime utcNow)
    {
        var colombiaToday = GetNowInColombiaFromUtc(utcNow).Date;
        var utcStart = ConvertColombiaToUtc(colombiaToday);
        return DateTime.SpecifyKind(utcStart, DateTimeKind.Utc);
    }

    public static DateTime GetTodayStartInUtc() => GetTodayStartInUtcFromUtc(DateTime.UtcNow);

    /// <summary>
    /// Obtiene el fin del día actual en Colombia, convertido a UTC
    /// </summary>
    public static DateTime GetTodayEndInUtcFromUtc(DateTime utcNow)
    {
        var colombiaTodayEnd = GetNowInColombiaFromUtc(utcNow).Date.AddDays(1).AddTicks(-1);
        var utcEnd = ConvertColombiaToUtc(colombiaTodayEnd);
        return DateTime.SpecifyKind(utcEnd, DateTimeKind.Utc);
    }

    public static DateTime GetTodayEndInUtc() => GetTodayEndInUtcFromUtc(DateTime.UtcNow);

    /// <summary>
    /// Inicio del día calendario siguiente en Colombia, en UTC (p. ej. para excluir reservas “futuras” respecto a hoy en Bogotá).
    /// </summary>
    public static DateTime GetColombiaStartOfTomorrowUtcFromUtc(DateTime utcNow)
    {
        var nextColombiaDay = GetNowInColombiaFromUtc(utcNow).Date.AddDays(1);
        var unspecified = DateTime.SpecifyKind(nextColombiaDay, DateTimeKind.Unspecified);
        var utc = ConvertColombiaToUtc(unspecified);
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }

    public static DateTime GetColombiaStartOfTomorrowUtc() => GetColombiaStartOfTomorrowUtcFromUtc(DateTime.UtcNow);

    /// <summary>
    /// Fecha de calendario en Colombia (medianoche local interpretada como fecha-only, Kind Unspecified).
    /// </summary>
    public static DateTime ConvertUtcToColombiaCalendarDate(DateTime utcInstant)
    {
        var utc = EnsureUtc(utcInstant);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, ColombiaTimeZone);
        return DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
    }

    /// <summary>True si ambos instantes UTC caen en el mismo día calendario en Colombia.</summary>
    public static bool IsSameColombiaCalendarDay(DateTime utcA, DateTime utcB) =>
        DateOnly.FromDateTime(ConvertUtcToColombiaCalendarDate(utcA)) ==
        DateOnly.FromDateTime(ConvertUtcToColombiaCalendarDate(utcB));

    /// <summary>
    /// True si <paramref name="utcInstant"/> es el día calendario actual en Colombia respecto a <paramref name="utcNow"/>.
    /// </summary>
    public static bool IsColombiaTodayFromUtc(DateTime utcInstant, DateTime utcNow) =>
        DateOnly.FromDateTime(ConvertUtcToColombiaCalendarDate(utcInstant)) ==
        GetTodayDateOnlyColombiaFromUtc(utcNow);

    /// <summary>Día operativo del pedido (ReservedFor o CreatedAt) en calendario Colombia.</summary>
    public static DateTime OrderOperationalColombiaCalendarDate(DateTime createdAtUtc, DateTime? reservedForUtc)
    {
        var instant = reservedForUtc ?? createdAtUtc;
        return ConvertUtcToColombiaCalendarDate(instant);
    }

    public static (int Year, int Month) OrderOperationalColombiaYearMonth(DateTime createdAtUtc, DateTime? reservedForUtc)
    {
        var instant = reservedForUtc ?? createdAtUtc;
        var utc = EnsureUtc(instant);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, ColombiaTimeZone);
        return (local.Year, local.Month);
    }

    public static int OrderOperationalColombiaYear(DateTime createdAtUtc, DateTime? reservedForUtc)
    {
        var instant = reservedForUtc ?? createdAtUtc;
        var utc = EnsureUtc(instant);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, ColombiaTimeZone);
        return local.Year;
    }

    public static int OrderOperationalColombiaHour(DateTime createdAtUtc, DateTime? reservedForUtc)
    {
        var instant = reservedForUtc ?? createdAtUtc;
        var utc = EnsureUtc(instant);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, ColombiaTimeZone);
        return local.Hour;
    }

    public static DateTime ExpenseColombiaCalendarDate(DateTime headerCreatedAtUtc)
        => ConvertUtcToColombiaCalendarDate(headerCreatedAtUtc);

    public static (int Year, int Month) ExpenseColombiaYearMonth(DateTime headerCreatedAtUtc)
    {
        var utc = EnsureUtc(headerCreatedAtUtc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, ColombiaTimeZone);
        return (local.Year, local.Month);
    }

    public static int ExpenseColombiaYear(DateTime headerCreatedAtUtc)
    {
        var utc = EnsureUtc(headerCreatedAtUtc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, ColombiaTimeZone);
        return local.Year;
    }

    /// <summary>Inicio UTC del día calendario Colombia.</summary>
    public static DateTime ColombiaCalendarDayStartUtc(DateTime colombiaDateOnly)
    {
        var day = colombiaDateOnly.Date;
        var (from, _) = GetColombiaCalendarDateRangeUtc(day, day);
        return from;
    }

    /// <summary>
    /// Rango UTC para filtrar instantes guardados en UTC (p. ej. <c>CreatedAt</c>) por días calendario en Colombia.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) GetColombiaCalendarDateRangeUtc(DateTime fromDate, DateTime toDate)
    {
        var from = fromDate.Date;
        var to = toDate.Date;
        if (to < from)
            (from, to) = (to, from);

        var startColombia = DateTime.SpecifyKind(from, DateTimeKind.Unspecified);
        var endColombia = DateTime.SpecifyKind(to.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified);

        var fromUtc = DateTime.SpecifyKind(ConvertColombiaToUtc(startColombia), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(ConvertColombiaToUtc(endColombia), DateTimeKind.Utc);
        return (fromUtc, toUtc);
    }

    /// <summary>
    /// Normaliza FromUtc/ToUtc del dashboard al rango UTC de los días calendario Colombia tocados por esos instantes,
    /// con tope de días inclusivos.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) NormalizeDashboardRangeUtc(
        DateTime fromUtc,
        DateTime toUtc,
        int maxColombiaInclusiveDays = 400)
    {
        var from = EnsureUtc(fromUtc);
        var to = EnsureUtc(toUtc);
        if (to < from)
            (from, to) = (to, from);

        var d0 = ConvertUtcToColombiaCalendarDate(from);
        var d1 = ConvertUtcToColombiaCalendarDate(to);
        var span = (int)(d1 - d0).TotalDays + 1;
        if (span > maxColombiaInclusiveDays)
            d1 = d0.AddDays(maxColombiaInclusiveDays - 1);

        return GetColombiaCalendarDateRangeUtc(d0, d1);
    }

    /// <summary>Último día calendario Colombia dentro del rango normalizado (para buckets por hora).</summary>
    public static (DateTime FromUtc, DateTime ToUtc) GetLastColombiaDayBoundsInRangeUtc(DateTime rangeFromUtc, DateTime rangeToUtc)
    {
        var to = EnsureUtc(rangeToUtc);
        var lastDay = ConvertUtcToColombiaCalendarDate(to);
        return GetColombiaCalendarDateRangeUtc(lastDay, lastDay);
    }

    public static (List<DateTime> Days, List<string> Labels) EnumerateColombiaDashboardDays(
        DateTime fromUtc,
        DateTime toUtc,
        int maxBuckets,
        CultureInfo culture)
    {
        var d0 = ConvertUtcToColombiaCalendarDate(EnsureUtc(fromUtc));
        var d1 = ConvertUtcToColombiaCalendarDate(EnsureUtc(toUtc));
        if (d1 < d0)
            (d0, d1) = (d1, d0);

        var days = new List<DateTime>();
        for (var d = d0; d <= d1 && days.Count < maxBuckets; d = d.AddDays(1))
            days.Add(d);

        var labels = days.Select(d => d.ToString("ddd d MMM", culture)).ToList();
        return (days, labels);
    }

    public static (List<(int Year, int Month)> Keys, List<string> Labels) EnumerateColombiaDashboardMonths(
        DateTime fromUtc,
        DateTime toUtc,
        int maxBuckets,
        CultureInfo culture)
    {
        var d0 = ConvertUtcToColombiaCalendarDate(EnsureUtc(fromUtc));
        var d1 = ConvertUtcToColombiaCalendarDate(EnsureUtc(toUtc));
        if (d1 < d0)
            (d0, d1) = (d1, d0);

        var s = new DateTime(d0.Year, d0.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var e = new DateTime(d1.Year, d1.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var keys = new List<(int Year, int Month)>();
        for (var cur = s; cur <= e && keys.Count < maxBuckets; cur = cur.AddMonths(1))
            keys.Add((cur.Year, cur.Month));

        var labels = keys.Select(k => new DateTime(k.Year, k.Month, 1).ToString("MMM yyyy", culture)).ToList();
        return (keys, labels);
    }

    public static (List<int> Years, List<string> Labels) EnumerateColombiaDashboardYears(
        DateTime fromUtc,
        DateTime toUtc,
        int maxBuckets)
    {
        var d0 = ConvertUtcToColombiaCalendarDate(EnsureUtc(fromUtc));
        var d1 = ConvertUtcToColombiaCalendarDate(EnsureUtc(toUtc));
        if (d1 < d0)
            (d0, d1) = (d1, d0);

        var y0 = d0.Year;
        var y1 = d1.Year;
        var years = new List<int>();
        for (var y = y0; y <= y1 && years.Count < maxBuckets; y++)
            years.Add(y);

        var labels = years.Select(y => y.ToString()).ToList();
        return (years, labels);
    }

    /// <summary>
    /// Convierte filtros FromDate/ToDate de la API (query string, Kind suele ser Unspecified) en límites UTC
    /// por calendario Colombia, para usar en EF/Npgsql con columnas <c>timestamptz</c>.
    /// </summary>
    public static (DateTime? FromUtc, DateTime? ToUtc) NormalizeApiDateFiltersToUtc(DateTime? fromDate, DateTime? toDate)
    {
        if (!fromDate.HasValue && !toDate.HasValue)
            return (null, null);

        if (fromDate.HasValue && toDate.HasValue)
        {
            var (f, t) = GetColombiaCalendarDateRangeUtc(fromDate.Value, toDate.Value);
            return (f, t);
        }

        if (fromDate.HasValue)
        {
            var (f, _) = GetColombiaCalendarDateRangeUtc(fromDate.Value, fromDate.Value);
            return (f, null);
        }

        var (_, tOnly) = GetColombiaCalendarDateRangeUtc(toDate!.Value, toDate.Value);
        return (null, tOnly);
    }
}
