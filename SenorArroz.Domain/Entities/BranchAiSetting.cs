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
    public DateTime? LastTestedAt { get; set; }
    public bool IsVerified { get; set; }

    public virtual Branch Branch { get; set; } = null!;
}
