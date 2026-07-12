using System.Threading.Channels;
using SenorArroz.Application.Common.Interfaces;
namespace SenorArroz.API.Services;
public record WhatsAppAiWorkItem(int ConversationId,int MessageId);
public class WhatsAppAiWorkQueue : IWhatsAppAiWorkQueue { private readonly Channel<WhatsAppAiWorkItem> _channel=Channel.CreateBounded<WhatsAppAiWorkItem>(new BoundedChannelOptions(500){FullMode=BoundedChannelFullMode.Wait}); public ValueTask EnqueueAsync(int c,int m,CancellationToken ct=default)=>_channel.Writer.WriteAsync(new(c,m),ct); public IAsyncEnumerable<WhatsAppAiWorkItem> ReadAllAsync(CancellationToken ct)=>_channel.Reader.ReadAllAsync(ct); }
public class WhatsAppAiBackgroundService(WhatsAppAiWorkQueue queue,IServiceScopeFactory scopes,ILogger<WhatsAppAiBackgroundService> logger):BackgroundService
{protected override async Task ExecuteAsync(CancellationToken stoppingToken){await foreach(var item in queue.ReadAllAsync(stoppingToken)){try{using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<IWhatsAppAiOrchestrator>().ProcessIncomingMessageAsync(item.ConversationId,item.MessageId,stoppingToken);}catch(Exception ex){logger.LogError(ex,"WhatsApp AI background item failed ConversationId={ConversationId} MessageId={MessageId}",item.ConversationId,item.MessageId);}}}}
