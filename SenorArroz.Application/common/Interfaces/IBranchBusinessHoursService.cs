namespace SenorArroz.Application.Common.Interfaces;
public record BranchBusinessHourDto(int? Id,int BranchId,DayOfWeek DayOfWeek,TimeOnly? OpenTime,TimeOnly? CloseTime,bool IsClosed,int DisplayOrder);
public interface IBranchBusinessHoursService{Task<IReadOnlyList<BranchBusinessHourDto>> GetBusinessHours(int branchId,CancellationToken ct=default);Task<string> GetBusinessHoursAsText(int branchId,CancellationToken ct=default);}
public record WhatsAppPromptConfiguration(string? AssistantName,string? Objective,string? Personality,string? RequiredRules,string? FixedBranchInfo,string? AdditionalInstructions);
public interface IWhatsAppSystemPromptBuilder{Task<string> Build(int branchId,CancellationToken ct=default);Task<string> Build(int branchId,WhatsAppPromptConfiguration configuration,CancellationToken ct=default);}
