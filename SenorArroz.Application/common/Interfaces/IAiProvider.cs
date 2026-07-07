namespace SenorArroz.Application.Common.Interfaces;

public interface IAiProvider
{
    string Provider { get; }

    Task<AiModelProviderResult> ListModelsAsync(
        string apiKey,
        CancellationToken cancellationToken = default);
}

public interface IAiProviderResolver
{
    Task<AiModelProviderResult> ListModelsAsync(
        string provider,
        string apiKey,
        CancellationToken cancellationToken = default);
}

public record AiModelProviderResult(
    bool Success,
    IReadOnlyList<AiProviderModel> Models,
    string? ErrorMessage);

public record AiProviderModel(
    string Id,
    string DisplayName);
