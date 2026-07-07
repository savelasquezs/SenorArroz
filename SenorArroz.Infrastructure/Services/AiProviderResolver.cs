using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public class AiProviderResolver : IAiProviderResolver
{
    private readonly IReadOnlyDictionary<string, IAiProvider> _providers;

    public AiProviderResolver(IEnumerable<IAiProvider> providers)
    {
        _providers = providers.ToDictionary(x => x.Provider, StringComparer.OrdinalIgnoreCase);
    }

    public Task<AiModelProviderResult> ListModelsAsync(
        string provider,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProvider(provider);
        return _providers.TryGetValue(normalized, out var providerClient)
            ? providerClient.ListModelsAsync(apiKey, cancellationToken)
            : Task.FromResult(new AiModelProviderResult(false, [], "Proveedor de IA no soportado."));
    }

    private static string NormalizeProvider(string? provider)
    {
        var value = provider?.Trim().ToLowerInvariant() ?? string.Empty;
        return value is "google_gemini" or "google-gemini" or "google gemini" ? "gemini" : value;
    }
}
