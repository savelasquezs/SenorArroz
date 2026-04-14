using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Application.Common.Services;

public sealed class SystemUtcClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
