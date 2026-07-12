namespace SenorArroz.Application.Common.Interfaces;
public record BranchBusinessHourDto(int? Id,int BranchId,DayOfWeek DayOfWeek,TimeOnly? OpenTime,TimeOnly? CloseTime,bool IsClosed,int DisplayOrder);
public interface IBranchBusinessHoursService{Task<IReadOnlyList<BranchBusinessHourDto>> GetBusinessHours(int branchId,CancellationToken ct=default);Task<string> GetBusinessHoursAsText(int branchId,CancellationToken ct=default);}
public interface IWhatsAppSystemPromptBuilder{Task<string> Build(int branchId,CancellationToken ct=default);}
