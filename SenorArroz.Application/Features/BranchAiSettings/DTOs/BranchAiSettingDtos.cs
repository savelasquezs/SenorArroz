namespace SenorArroz.Application.Features.BranchAiSettings.DTOs;

public class BranchAiSettingDto
{
    public int? Id { get; set; }
    public int BranchId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool ApiKeyConfigured { get; set; }
    public string? ApiKeyMasked { get; set; }
    public bool IsActive { get; set; }
    public double? Temperature { get; set; }
    public int MaxContextMessages { get; set; } = 20;
    public DateTime? LastTestedAt { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = "not_configured";
    public string AssistantName { get; set; } = string.Empty;
    public string PromptObjective { get; set; } = string.Empty;
    public string PromptPersonality { get; set; } = string.Empty;
    public string PromptRequiredRules { get; set; } = string.Empty;
    public string PromptFixedBranchInfo { get; set; } = string.Empty;
    public string PromptAdditionalInstructions { get; set; } = string.Empty;
    public string TransferMessage { get; set; } = string.Empty;
}

public class UpsertBranchAiSettingDto
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public bool IsActive { get; set; }
    public double? Temperature { get; set; }
    public int MaxContextMessages { get; set; } = 20;
    public string AssistantName { get; set; } = string.Empty;
    public string PromptObjective { get; set; } = string.Empty;
    public string PromptPersonality { get; set; } = string.Empty;
    public string PromptRequiredRules { get; set; } = string.Empty;
    public string PromptFixedBranchInfo { get; set; } = string.Empty;
    public string PromptAdditionalInstructions { get; set; } = string.Empty;
    public string TransferMessage { get; set; } = string.Empty;
}

public record PromptPreviewDto(string Prompt);

public class AiTestConnectionResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public BranchAiSettingDto? Setting { get; set; }
}

public class AiModelLookupDto
{
    public string Provider { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
}

public class AiProviderModelDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class AiProviderModelsResultDto
{
    public string Provider { get; set; } = string.Empty;
    public List<AiProviderModelDto> Models { get; set; } = [];
}
