using System.Text.Json;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
namespace SenorArroz.Tests;
public class WhatsAppAiHardeningTests
{
 [Fact] public void FullQueue_TryEnqueueReturnsImmediatelyAndFalse(){var q=new WhatsAppAiWorkQueue();for(var i=0;i<500;i++)Assert.True(q.TryEnqueue(1,i));var started=DateTime.UtcNow;Assert.False(q.TryEnqueue(1,501));Assert.True(DateTime.UtcNow-started<TimeSpan.FromSeconds(1));}
 [Fact] public async Task UnknownTool_ReturnsControlledFailure(){var executor=new AgentToolExecutor([],Mock.Of<IApplicationDbContext>());using var doc=JsonDocument.Parse("{}");var result=await executor.ExecuteAsync("unknown",new(1,1),doc.RootElement,CancellationToken.None);Assert.False(result.Success);Assert.Contains("no permitida",result.Error,StringComparison.OrdinalIgnoreCase);}
}
