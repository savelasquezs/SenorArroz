using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
namespace SenorArroz.Application.Common.Services;
public class BranchBusinessHoursService(IApplicationDbContext db):IBranchBusinessHoursService
{
 private static readonly string[] Names=["Domingo","Lunes","Martes","Miércoles","Jueves","Viernes","Sábado"];
 public async Task<IReadOnlyList<BranchBusinessHourDto>> GetBusinessHours(int branchId,CancellationToken ct=default){var rows=await db.BranchBusinessHours.AsNoTracking().Where(x=>x.BranchId==branchId).OrderBy(x=>x.DisplayOrder).Select(x=>new BranchBusinessHourDto(x.Id,x.BranchId,x.DayOfWeek,x.OpenTime,x.CloseTime,x.IsClosed,x.DisplayOrder)).ToListAsync(ct);return rows.Count>0?rows:Enumerable.Range(0,7).Select(i=>{var day=(DayOfWeek)((i+1)%7);return new BranchBusinessHourDto(null,branchId,day,null,null,true,i);}).ToList();}
 public async Task<string> GetBusinessHoursAsText(int branchId,CancellationToken ct=default){var rows=await GetBusinessHours(branchId,ct);return string.Join(Environment.NewLine,rows.OrderBy(x=>x.DisplayOrder).Select(x=>$"{Names[(int)x.DayOfWeek]}: {(x.IsClosed?"Cerrado":$"{Format(x.OpenTime)} - {Format(x.CloseTime)}")}"));}
 private static string Format(TimeOnly? t)=>t?.ToString("h:mm tt",CultureInfo.InvariantCulture)??"Sin definir";
}
