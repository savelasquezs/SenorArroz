using SenorArroz.Domain.Entities.Common;
namespace SenorArroz.Domain.Entities;
public class BranchBusinessHour:BaseEntity{public int BranchId{get;set;}public DayOfWeek DayOfWeek{get;set;}public TimeOnly? OpenTime{get;set;}public TimeOnly? CloseTime{get;set;}public bool IsClosed{get;set;}public int DisplayOrder{get;set;}public Branch Branch{get;set;}=null!;}
