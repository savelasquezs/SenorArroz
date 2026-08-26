using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
namespace SenorArroz.Application.Common.Services;
public class BranchBusinessHoursService(IApplicationDbContext db):IBranchBusinessHoursService
{
 private static readonly string[] Names=["Domingo","Lunes","Martes","Miércoles","Jueves","Viernes","Sábado"];
 public async Task<IReadOnlyList<BranchBusinessHourDto>> GetBusinessHours(int branchId,CancellationToken ct=default)=>(await GetBusinessHoursMany([branchId],ct))[branchId];
 public async Task<IReadOnlyDictionary<int,IReadOnlyList<BranchBusinessHourDto>>> GetBusinessHoursMany(IEnumerable<int> branchIds,CancellationToken ct=default)
 {
  var ids=branchIds.Distinct().ToList();
  var rows=await db.BranchBusinessHours.AsNoTracking().Where(x=>ids.Contains(x.BranchId)).OrderBy(x=>x.DisplayOrder).Select(x=>new BranchBusinessHourDto(x.Id,x.BranchId,x.DayOfWeek,x.OpenTime,x.CloseTime,x.IsClosed,x.DisplayOrder)).ToListAsync(ct);
  return ids.ToDictionary(id=>id,id=>WithFallback(id,rows.Where(x=>x.BranchId==id).ToList()));
 }
 public async Task<string> GetBusinessHoursAsText(int branchId,CancellationToken ct=default){var rows=await GetBusinessHours(branchId,ct);return string.Join(Environment.NewLine,rows.OrderBy(x=>x.DisplayOrder).Select(x=>$"{Names[(int)x.DayOfWeek]}: {(x.IsClosed?"Cerrado":$"{Format(x.OpenTime)} - {Format(x.CloseTime)}")}"));}
 public async Task<BranchBusinessHoursEvaluation> Evaluate(int branchId,DateTime nowUtc,CancellationToken ct=default)
 {
  var evaluations=await EvaluateMany([branchId],nowUtc,ct);
  return evaluations[branchId];
 }
 public async Task<IReadOnlyDictionary<int,BranchBusinessHoursEvaluation>> EvaluateMany(IEnumerable<int> branchIds,DateTime nowUtc,CancellationToken ct=default)
 {
  var ids=branchIds.Distinct().ToList();
  var schedules=await GetBusinessHoursMany(ids,ct);
  return ids.ToDictionary(id=>id,id=>EvaluateRows(schedules[id],nowUtc));
 }
 private static IReadOnlyList<BranchBusinessHourDto> WithFallback(int branchId,IReadOnlyList<BranchBusinessHourDto> rows)=>rows.Count>0?rows:Enumerable.Range(0,7).Select(i=>new BranchBusinessHourDto(null,branchId,(DayOfWeek)((i+1)%7),null,null,true,i)).ToList();
 private static BranchBusinessHoursEvaluation EvaluateRows(IReadOnlyCollection<BranchBusinessHourDto> rows,DateTime nowUtc)
 {
  if(!IsValidConfiguredSchedule(rows))return new(false,false,null,null);
  var schedule=rows.ToDictionary(x=>x.DayOfWeek);
  var nowLocal=ColombiaTimeHelper.GetNowInColombiaFromUtc(nowUtc);
  var today=schedule[nowLocal.DayOfWeek];
  var localTime=TimeOnly.FromDateTime(nowLocal);
  if(!today.IsClosed&&localTime>=today.OpenTime!.Value&&localTime<today.CloseTime!.Value)
   return new(true,true,null,null);
  DateTime? closedAtLocal=null;
  for(var offset=0;offset<=7;offset++)
  {
   var date=nowLocal.Date.AddDays(-offset);
   var row=schedule[date.DayOfWeek];
   if(row.IsClosed)continue;
   var candidate=date.Add(row.CloseTime!.Value.ToTimeSpan());
   if(candidate<=nowLocal&&(closedAtLocal is null||candidate>closedAtLocal))closedAtLocal=candidate;
  }
  DateTime? nextOpeningLocal=null;
  for(var offset=0;offset<=7;offset++)
  {
   var date=nowLocal.Date.AddDays(offset);
   var row=schedule[date.DayOfWeek];
   if(row.IsClosed)continue;
   var candidate=date.Add(row.OpenTime!.Value.ToTimeSpan());
   if(candidate>nowLocal&&(nextOpeningLocal is null||candidate<nextOpeningLocal))nextOpeningLocal=candidate;
  }
  return new(true,false,closedAtLocal is null?null:ColombiaTimeHelper.ConvertColombiaToUtc(closedAtLocal.Value),nextOpeningLocal is null?null:ColombiaTimeHelper.ConvertColombiaToUtc(nextOpeningLocal.Value));
 }
 private static bool IsValidConfiguredSchedule(IReadOnlyCollection<BranchBusinessHourDto> rows)
 {
  if(rows.Count!=7||rows.Select(x=>x.DayOfWeek).Distinct().Count()!=7||rows.All(x=>x.IsClosed))return false;
  return rows.All(x=>x.IsClosed?x.OpenTime is null&&x.CloseTime is null:x.OpenTime is not null&&x.CloseTime is not null&&x.CloseTime>x.OpenTime);
 }
 private static string Format(TimeOnly? t)=>t?.ToString("h:mm tt",CultureInfo.InvariantCulture)??"Sin definir";
}
