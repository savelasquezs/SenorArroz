using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Tests;

/// <summary>Reloj de prueba con instante UTC configurable.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTime utcNow) => UtcNow = utcNow;

    public DateTime UtcNow { get; set; }
}
