namespace SenorArroz.API.Security;

public sealed class StorefrontQuoteConcurrencyGate
{
    private readonly SemaphoreSlim _semaphore;

    public StorefrontQuoteConcurrencyGate(IConfiguration configuration)
    {
        var limit = Math.Max(1, configuration.GetValue("RateLimiting:StorefrontQuote:ConcurrentLimit", 8));
        _semaphore = new SemaphoreSlim(limit, limit);
    }

    public async Task<IDisposable?> TryEnter(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
            return null;
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
