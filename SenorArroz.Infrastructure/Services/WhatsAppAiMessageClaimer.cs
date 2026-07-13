using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
namespace SenorArroz.Infrastructure.Services;
public class WhatsAppAiMessageClaimer(ApplicationDbContext db,IOptions<WhatsAppAiOrchestratorOptions> options):IWhatsAppAiMessageClaimer
{public async Task<bool> TryClaimAsync(int conversationId,int messageId,CancellationToken ct)=>await db.WhatsAppMessages.Where(x=>x.Id==messageId&&x.ConversationId==conversationId&&x.Direction==WhatsAppMessageDirection.Inbound&&x.AiProcessingStatus==WhatsAppAiProcessingStatus.Pending&&x.AiProcessingAttempts<options.Value.MaxPersistentAttempts&&(x.AiNextRetryAt==null||x.AiNextRetryAt<=DateTime.UtcNow)).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AiProcessingStatus,WhatsAppAiProcessingStatus.Processing).SetProperty(x=>x.AiProcessingStartedAt,DateTime.UtcNow).SetProperty(x=>x.AiNextRetryAt,(DateTime?)null).SetProperty(x=>x.AiProcessingAttempts,x=>x.AiProcessingAttempts+1),ct)==1;}
