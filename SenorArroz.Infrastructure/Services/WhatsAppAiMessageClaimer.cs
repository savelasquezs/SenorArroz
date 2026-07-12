using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
namespace SenorArroz.Infrastructure.Services;
public class WhatsAppAiMessageClaimer(ApplicationDbContext db):IWhatsAppAiMessageClaimer
{public async Task<bool> TryClaimAsync(int conversationId,int messageId,CancellationToken ct)=>await db.WhatsAppMessages.Where(x=>x.Id==messageId&&x.ConversationId==conversationId&&x.Direction==WhatsAppMessageDirection.Inbound&&x.AiProcessingStatus==WhatsAppAiProcessingStatus.Pending).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.AiProcessingStatus,WhatsAppAiProcessingStatus.Processing).SetProperty(x=>x.AiProcessingAttempts,x=>x.AiProcessingAttempts+1),ct)==1;}
