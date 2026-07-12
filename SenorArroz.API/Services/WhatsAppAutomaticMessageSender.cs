using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
namespace SenorArroz.API.Services;
public class WhatsAppAutomaticMessageSender(ApplicationDbContext db,IWhatsAppCloudClient cloud,IWhatsAppNotificationService notifications,IClock clock):IWhatsAppAutomaticMessageSender
{
 public async Task<WhatsAppAutomaticSendResult> SendTextAsync(int conversationId,int incomingMessageId,string attemptId,string text,CancellationToken ct)
 {
  var c=await db.WhatsAppConversations.Include(x=>x.Branch).Include(x=>x.Customer).FirstOrDefaultAsync(x=>x.Id==conversationId,ct);var source=await db.WhatsAppMessages.FirstOrDefaultAsync(x=>x.Id==incomingMessageId&&x.ConversationId==conversationId,ct);if(c==null||source==null||c.AttentionMode!=WhatsAppAttentionMode.Ai)return new(false,false,null,"La atención ya no está en IA.");
  if(source.AiProcessingStatus is WhatsAppAiProcessingStatus.Sent or WhatsAppAiProcessingStatus.Completed||!string.IsNullOrWhiteSpace(source.AiResponseWhatsAppMessageId))return new(true,false,source.AiResponseWhatsAppMessageId,null);
  var s=await db.WhatsAppBranchSettings.AsNoTracking().FirstOrDefaultAsync(x=>x.BranchId==c.BranchId&&x.IsActive&&x.IsVerified,ct);if(s==null)return new(false,false,null,"WhatsApp no disponible.");
  source.AiProcessingStatus=WhatsAppAiProcessingStatus.Sending;source.AiResponseAttemptId=attemptId;source.AiGeneratedResponse=text;await db.SaveChangesAsync(ct);
  // Existe una carrera inevitable entre esta última lectura y la llamada externa. Meta no ofrece
  // una clave idempotente de cliente para /messages; el estado Sending evita reintentos automáticos inciertos.
  if(!await db.WhatsAppConversations.AsNoTracking().AnyAsync(x=>x.Id==conversationId&&x.AttentionMode==WhatsAppAttentionMode.Ai,ct))return new(false,false,null,"La atención cambió.");
  var result=await cloud.SendTextMessageAsync(s.PhoneNumberId,s.AccessToken,c.PhoneNumber,text,ct);var now=clock.UtcNow;var m=new WhatsAppMessage{ConversationId=c.Id,WhatsAppMessageId=result.WhatsAppMessageId,Direction=WhatsAppMessageDirection.Outbound,Type=WhatsAppMessageType.Text,TextBody=text,Status=result.Success?WhatsAppMessageStatus.Sent:WhatsAppMessageStatus.Failed,Timestamp=now,SentByAi=true,RawPayload=JsonSerializer.Serialize(new{origin="ai",attemptId,result.Success,result.ErrorMessage})};db.WhatsAppMessages.Add(m);
  if(result.Success){source.AiProcessingStatus=WhatsAppAiProcessingStatus.Sent;source.AiResponseWhatsAppMessageId=result.WhatsAppMessageId;c.LastMessageAt=now;c.LastMessagePreview=text;}else source.AiProcessingStatus=WhatsAppAiProcessingStatus.Failed;
  await db.SaveChangesAsync(ct);var conversationDto=new WhatsAppConversationDto{Id=c.Id,BranchId=c.BranchId,BranchName=c.Branch.Name,CustomerId=c.CustomerId,CustomerName=c.Customer?.Name,PhoneNumber=c.PhoneNumber,ContactName=c.ContactName,Status="open",AttentionMode="ai",LastMessageAt=c.LastMessageAt,LastMessagePreview=c.LastMessagePreview,UnreadCount=c.UnreadCount,CreatedAt=c.CreatedAt,UpdatedAt=c.UpdatedAt,AttentionModeUpdatedAt=c.AttentionModeUpdatedAt};var messageDto=new WhatsAppMessageDto{Id=m.Id,ConversationId=c.Id,WhatsAppMessageId=m.WhatsAppMessageId,Direction="outbound",Type="text",TextBody=text,Status=result.Success?"sent":"failed",Timestamp=now,CreatedAt=m.CreatedAt};await notifications.NotifyMessageCreatedAsync(c.BranchId,conversationDto,messageDto,ct);
  return new(result.Success,!result.Success&&IsTransient(result.ErrorMessage),result.WhatsAppMessageId,result.ErrorMessage);
 }
 private static bool IsTransient(string? error)=>error?.Contains("429",StringComparison.OrdinalIgnoreCase)==true||error?.Contains("timeout",StringComparison.OrdinalIgnoreCase)==true||error?.Contains("temporal",StringComparison.OrdinalIgnoreCase)==true||error?.Contains("500",StringComparison.OrdinalIgnoreCase)==true;
}
