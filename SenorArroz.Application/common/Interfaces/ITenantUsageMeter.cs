namespace SenorArroz.Application.Common.Interfaces;

public interface ITenantUsageMeter
{
    Task AddStorageBytesAsync(long bytes, CancellationToken cancellationToken = default);
}
