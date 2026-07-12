using System.Threading.Channels;
using SenorArroz.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
namespace SenorArroz.API.Services;
public record WhatsAppAiWorkItem(int ConversationId,int MessageId);
public class WhatsAppAiWorkQueue : IWhatsAppAiWorkQueue { private readonly Channel<WhatsAppAiWorkItem> _channel=Channel.CreateBounded<WhatsAppAiWorkItem>(new BoundedChannelOptions(500){FullMode=BoundedChannelFullMode.Wait}); public bool TryEnqueue(int c,int m)=>_channel.Writer.TryWrite(new(c,m)); public IAsyncEnumerable<WhatsAppAiWorkItem> ReadAllAsync(CancellationToken ct)=>_channel.Reader.ReadAllAsync(ct); }
public class WhatsAppAiBackgroundService(WhatsAppAiWorkQueue queue,IServiceScopeFactory scopes,ILogger<WhatsAppAiBackgroundService> logger):BackgroundService
{protected override async Task ExecuteAsync(CancellationToken stoppingToken){await foreach(var item in queue.ReadAllAsync(stoppingToken)){try{using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<IWhatsAppAiOrchestrator>().ProcessIncomingMessageAsync(item.ConversationId,item.MessageId,stoppingToken);}catch(Exception ex){logger.LogError(ex,"WhatsApp AI background item failed ConversationId={ConversationId} MessageId={MessageId}",item.ConversationId,item.MessageId);}}}}

public class WhatsAppAiRecoveryService(WhatsAppAiWorkQueue queue,IServiceScopeFactory scopes,IOptions<WhatsAppAiOrchestratorOptions> options,ILogger<WhatsAppAiRecoveryService> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct){var o=options.Value;while(!ct.IsCancellationRequested){try{using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var now=DateTime.UtcNow;var stale=now.AddSeconds(-o.ProcessingStaleAfterSeconds);await db.WhatsAppMessages.Where(x=>x.AiProcessingStatus==WhatsAppAiProcessingStatus.Processing&&x.AiProcessingStartedAt<stale).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AiProcessingStatus,WhatsAppAiProcessingStatus.Pending).SetProperty(x=>x.AiProcessingStartedAt,(DateTime?)null).SetProperty(x=>x.AiNextRetryAt,now),ct);var pending=await db.WhatsAppMessages.AsNoTracking().Where(x=>x.AiProcessingStatus==WhatsAppAiProcessingStatus.Pending&&(x.AiNextRetryAt==null||x.AiNextRetryAt<=now)).OrderBy(x=>x.Id).Take(o.RecoveryBatchSize).Select(x=>new{x.ConversationId,x.Id}).ToListAsync(ct);foreach(var x in pending)if(!queue.TryEnqueue(x.ConversationId,x.Id))break;}catch(Exception ex){logger.LogError(ex,"WhatsApp AI recovery cycle failed.");}await Task.Delay(TimeSpan.FromSeconds(Math.Max(1,o.RecoveryIntervalSeconds)),ct);}}
}
