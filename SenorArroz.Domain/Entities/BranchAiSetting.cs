using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class BranchAiSetting : BaseEntity
{
    public int BranchId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public double? Temperature { get; set; }
    public int MaxContextMessages { get; set; } = 20;
    public string ContextStrategy { get; set; } = "legacy";
    public DateTime? LastTestedAt { get; set; }
    public bool IsVerified { get; set; }
    public string AssistantName { get; set; } = string.Empty;
    public string? PromptObjective { get; set; }
    public string? PromptPersonality { get; set; }
    public string? PromptRequiredRules { get; set; }
    public string? PromptFixedBranchInfo { get; set; }
    public string? PromptAdditionalInstructions { get; set; }
    public string TransferMessage { get; set; } = "Un asesor continuará con tu atención.";

    public virtual Branch Branch { get; set; } = null!;
}
