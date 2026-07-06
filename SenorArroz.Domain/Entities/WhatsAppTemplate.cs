using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class WhatsAppTemplate : BaseEntity
{
    public int? BranchId { get; set; }
    public string? BusinessAccountId { get; set; }
    public string MetaTemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Components { get; set; } = "[]";

    public virtual Branch? Branch { get; set; }
}
