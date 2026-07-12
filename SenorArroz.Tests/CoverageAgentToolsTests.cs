using System.Text.Json;using Microsoft.EntityFrameworkCore;using SenorArroz.Application.Common.Models;using SenorArroz.Domain.Entities;using SenorArroz.Infrastructure.Data;using SenorArroz.Infrastructure.Services;
namespace SenorArroz.Tests;
public class CoverageAgentToolsTests
{
 [Fact]public async Task ExactAccentInsensitiveMatch_ReturnsBackendFeeAndBranch(){await using var db=Db();Seed(db);await db.SaveChangesAsync();var r=await new RegisteredNeighborhoodResolver(db).Resolve("barrio Sántander",1,default);Assert.True(r.Matched);Assert.Equal(4000,r.Match!.DeliveryFee);Assert.Equal("Centro",r.Match.BranchName);}
 [Fact]public async Task ReasonableTypo_IsMatched(){await using var db=Db();Seed(db);await db.SaveChangesAsync();var r=await new RegisteredNeighborhoodResolver(db).Resolve("Santnder",1,default);Assert.True(r.Matched);Assert.Equal("Santander",r.Match!.Name);}
 [Fact]public async Task AmbiguousPartialMatch_RequiresConfirmation(){await using var db=Db();Seed(db);db.Neighborhoods.AddRange(new Neighborhood{Id=2,BranchId=1,Name="Manrique Central",DeliveryFee=5000},new Neighborhood{Id=3,BranchId=1,Name="Manrique Oriental",DeliveryFee=5500});await db.SaveChangesAsync();var r=await new RegisteredNeighborhoodResolver(db).Resolve("Manrique",1,default);Assert.True(r.RequiresConfirmation);Assert.Equal(2,r.Options.Count);}
 [Fact]public void ToolSchema_DoesNotExposeBranchId(){using var db=Db();var tool=new FindRegisteredNeighborhoodAgentTool(new RegisteredNeighborhoodResolver(db));Assert.DoesNotContain("branchId",tool.ParametersSchema.GetRawText(),StringComparison.OrdinalIgnoreCase);}
 private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
 private static void Seed(ApplicationDbContext db){db.Branches.Add(new Branch{Id=1,Name="Centro"});db.Neighborhoods.Add(new Neighborhood{Id=1,BranchId=1,Name="Santander",DeliveryFee=4000});}
}
