using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController, Authorize(Roles="Superadmin, Admin"), Route("api/branches/{branchId:int}/business-hours")]
public class BranchBusinessHoursController(IApplicationDbContext db, ICurrentUser currentUser, IBranchBusinessHoursService service):ControllerBase
{
 [HttpGet] public async Task<ActionResult<ApiResponse<IReadOnlyList<BranchBusinessHourDto>>>> Get(int branchId,CancellationToken ct){if(!Access(branchId))return Forbid();if(!await db.Branches.AnyAsync(x=>x.Id==branchId,ct))return NotFound();return Ok(ApiResponse<IReadOnlyList<BranchBusinessHourDto>>.SuccessResponse(await service.GetBusinessHours(branchId,ct)));}
 [HttpPut] public async Task<ActionResult<ApiResponse<IReadOnlyList<BranchBusinessHourDto>>>> Put(int branchId,[FromBody]List<BranchBusinessHourDto> values,CancellationToken ct){if(!Access(branchId))return Forbid();if(!await db.Branches.AnyAsync(x=>x.Id==branchId,ct))return NotFound();if(values.GroupBy(x=>x.DayOfWeek).Any(x=>x.Count()>1)||values.Any(x=>!x.IsClosed&&(x.OpenTime is null||x.CloseTime is null||x.CloseTime<=x.OpenTime)))return BadRequest(ApiResponse<IReadOnlyList<BranchBusinessHourDto>>.ErrorResponse("Cada día debe ser único y los horarios abiertos deben tener una hora de cierre posterior a la apertura."));var existing=await db.BranchBusinessHours.Where(x=>x.BranchId==branchId).ToListAsync(ct);foreach(var value in values){var row=existing.FirstOrDefault(x=>x.DayOfWeek==value.DayOfWeek);if(row==null){row=new BranchBusinessHour{BranchId=branchId,DayOfWeek=value.DayOfWeek};db.BranchBusinessHours.Add(row);}row.IsClosed=value.IsClosed;row.OpenTime=value.IsClosed?null:value.OpenTime;row.CloseTime=value.IsClosed?null:value.CloseTime;row.DisplayOrder=value.DisplayOrder;}await db.SaveChangesAsync(ct);return Ok(ApiResponse<IReadOnlyList<BranchBusinessHourDto>>.SuccessResponse(await service.GetBusinessHours(branchId,ct),"Horarios guardados."));}
 private bool Access(int id)=>Roles.IsSuperadmin(currentUser.Role)||(Roles.IsAdmin(currentUser.Role)&&currentUser.BranchId==id);
}
