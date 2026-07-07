namespace SenorArroz.Application.Common.Interfaces;

public interface IAiModelProviderClient
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
