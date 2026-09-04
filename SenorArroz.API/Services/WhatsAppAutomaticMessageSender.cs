using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
namespace SenorArroz.API.Services;
public class WhatsAppAutomaticMessageSender(ApplicationDbContext db,IWhatsAppCloudClient cloud,IWhatsAppNotificationService notifications,IClock clock):IWhatsAppAutomaticMessageSender
{
 public async Task<WhatsAppAutomaticSendResult> SendTextAsync(int conversationId,int incomingMessageId,string attemptId,string text,CancellationToken ct)
 {
  var c=await db.WhatsAppConversations.Include(x=>x.Branch).Include(x=>x.Customer).FirstOrDefaultAsync(x=>x.Id==conversationId,ct);var source=await db.WhatsAppMessages.FirstOrDefaultAsync(x=>x.Id==incomingMessageId&&x.ConversationId==conversationId,ct);if(c==null||source==null||c.AttentionMode!=WhatsAppAttentionMode.Ai)return new(false,false,null,"La atención ya no está en IA.");var recipient=WhatsAppRecipientResolver.Resolve(c);if(recipient==null)return new(false,false,null,"La conversación no tiene un destinatario de WhatsApp válido.");
  if(source.AiProcessingStatus is WhatsAppAiProcessingStatus.Sent or WhatsAppAiProcessingStatus.Completed||!string.IsNullOrWhiteSpace(source.AiResponseWhatsAppMessageId))return new(true,false,source.AiResponseWhatsAppMessageId,null);
  if(source.AiProcessingStatus==WhatsAppAiProcessingStatus.Sending)return new(false,false,null,"El envío ya está en curso.",true);
  var s=await GetChannelAsync(c,ct);if(s==null)return new(false,false,null,"WhatsApp no disponible.");
  var eligible=(source.AiResponseAttemptId==null||source.AiResponseAttemptId==attemptId)&&source.AiProcessingStatus is WhatsAppAiProcessingStatus.ResponseGenerated or WhatsAppAiProcessingStatus.Processing;
  var acquired=db.Database.IsRelational()?await db.WhatsAppMessages.Where(x=>x.Id==source.Id&&x.ConversationId==conversationId&&(x.AiResponseAttemptId==null||x.AiResponseAttemptId==attemptId)&&(x.AiProcessingStatus==WhatsAppAiProcessingStatus.ResponseGenerated||x.AiProcessingStatus==WhatsAppAiProcessingStatus.Processing)).ExecuteUpdateAsync(update=>update.SetProperty(x=>x.AiProcessingStatus,WhatsAppAiProcessingStatus.Sending).SetProperty(x=>x.AiResponseAttemptId,attemptId).SetProperty(x=>x.AiGeneratedResponse,text),ct):eligible?1:0;
  if(acquired==0){var current=await db.WhatsAppMessages.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==source.Id,ct);if(current?.AiProcessingStatus is WhatsAppAiProcessingStatus.Sent or WhatsAppAiProcessingStatus.Completed||!string.IsNullOrWhiteSpace(current?.AiResponseWhatsAppMessageId))return new(true,false,current?.AiResponseWhatsAppMessageId,null);return new(false,false,null,"Otro proceso adquirió el envío; no se repetirá el POST a Meta.",true);}
  source.AiProcessingStatus=WhatsAppAiProcessingStatus.Sending;source.AiResponseAttemptId=attemptId;source.AiGeneratedResponse=text;
  // Existe una carrera inevitable entre esta última lectura y la llamada externa. Meta no ofrece
  // una clave idempotente de cliente para /messages; el estado Sending evita reintentos automáticos inciertos.
  if(!await db.WhatsAppConversations.AsNoTracking().AnyAsync(x=>x.Id==conversationId&&x.AttentionMode==WhatsAppAttentionMode.Ai,ct))return new(false,false,null,"La atención cambió.");
  var result=await cloud.SendTextMessageAsync(s.PhoneNumberId,s.AccessToken,recipient,text,ct);var now=clock.UtcNow;var m=new WhatsAppMessage{ConversationId=c.Id,WhatsAppMessageId=result.WhatsAppMessageId,Direction=WhatsAppMessageDirection.Outbound,Type=WhatsAppMessageType.Text,TextBody=text,Status=result.Success?WhatsAppMessageStatus.Sent:WhatsAppMessageStatus.Failed,Timestamp=now,SentByAi=true,RawPayload=JsonSerializer.Serialize(new{origin="ai",attemptId,result.Success,result.ErrorMessage})};db.WhatsAppMessages.Add(m);
  if(result.Success){source.AiProcessingStatus=WhatsAppAiProcessingStatus.Sent;source.AiResponseWhatsAppMessageId=result.WhatsAppMessageId;c.LastMessageAt=now;c.LastMessagePreview=text;}else source.AiProcessingStatus=WhatsAppAiProcessingStatus.Failed;
  await db.SaveChangesAsync(ct);var conversationDto=new WhatsAppConversationDto{Id=c.Id,BranchId=c.BranchId,BranchName=c.Branch.Name,CustomerId=c.CustomerId,CustomerName=c.Customer?.Name,PhoneNumber=c.PhoneNumber,WhatsAppUsername=c.WhatsAppUsername,HasWhatsAppIdentity=!string.IsNullOrWhiteSpace(c.WhatsAppUserId),ContactName=c.ContactName,Status="open",AttentionMode="ai",LastMessageAt=c.LastMessageAt,LastMessagePreview=c.LastMessagePreview,UnreadCount=c.UnreadCount,CreatedAt=c.CreatedAt,UpdatedAt=c.UpdatedAt,AttentionModeUpdatedAt=c.AttentionModeUpdatedAt};var messageDto=new WhatsAppMessageDto{Id=m.Id,ConversationId=c.Id,WhatsAppMessageId=m.WhatsAppMessageId,Direction="outbound",Type="text",TextBody=text,Status=result.Success?"sent":"failed",Timestamp=now,CreatedAt=m.CreatedAt};await notifications.NotifyMessageCreatedAsync(c.BranchId,conversationDto,messageDto,ct);
  return new(result.Success,!result.Success&&IsRetrySafeTransient(result.ErrorMessage),result.WhatsAppMessageId,result.ErrorMessage);
 }
 public async Task<WhatsAppAutomaticSendResult> SendAwayTextAsync(int conversationId,string dispatchKey,string text,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(dispatchKey)||dispatchKey.Length>180)return new(false,false,null,"Clave de envío inválida.");
  var existing=await db.WhatsAppMessages.AsNoTracking().FirstOrDefaultAsync(x=>x.AgentDispatchKey==dispatchKey,ct);
  if(existing!=null)return existing.Status==WhatsAppMessageStatus.Sent?new(true,false,existing.WhatsAppMessageId,null):new(false,false,existing.WhatsAppMessageId,"El aviso de ausencia ya fue intentado durante este cierre.");
  var c=await db.WhatsAppConversations.Include(x=>x.Branch).Include(x=>x.Customer).FirstOrDefaultAsync(x=>x.Id==conversationId,ct);
  if(c==null)return new(false,false,null,"Conversación no encontrada.");var recipient=WhatsAppRecipientResolver.Resolve(c);if(recipient==null)return new(false,false,null,"La conversación no tiene un destinatario de WhatsApp válido.");
  var setting=await GetChannelAsync(c,ct,true);
  if(setting==null)return new(false,false,null,"El mensaje de ausencia no está disponible.");
  var now=clock.UtcNow;
  var message=new WhatsAppMessage{ConversationId=c.Id,Direction=WhatsAppMessageDirection.Outbound,Type=WhatsAppMessageType.Text,TextBody=text,Status=WhatsAppMessageStatus.Failed,Timestamp=now,SentByAi=false,AgentDispatchKey=dispatchKey,RawPayload=JsonSerializer.Serialize(new{origin="away_message",dispatchKey})};
  db.WhatsAppMessages.Add(message);
  try
  {
   await db.SaveChangesAsync(ct);
  }
  catch(DbUpdateException)
  {
   db.Entry(message).State=EntityState.Detached;
   var duplicate=await db.WhatsAppMessages.AsNoTracking().FirstAsync(x=>x.AgentDispatchKey==dispatchKey,ct);
   return duplicate.Status==WhatsAppMessageStatus.Sent?new(true,false,duplicate.WhatsAppMessageId,null):new(false,false,duplicate.WhatsAppMessageId,"El aviso de ausencia ya está siendo procesado.");
  }
  var result=await cloud.SendTextMessageAsync(setting.PhoneNumberId,setting.AccessToken,recipient,text,ct);
  message.WhatsAppMessageId=result.WhatsAppMessageId;
  message.Status=result.Success?WhatsAppMessageStatus.Sent:WhatsAppMessageStatus.Failed;
  message.RawPayload=JsonSerializer.Serialize(new{origin="away_message",dispatchKey,result.Success,result.ErrorMessage});
  if(result.Success){c.LastMessageAt=now;c.LastMessagePreview=text;}
  await db.SaveChangesAsync(ct);
  var attentionMode=c.AttentionMode switch{WhatsAppAttentionMode.Ai=>"ai",WhatsAppAttentionMode.Human=>"human",WhatsAppAttentionMode.WaitingForHuman=>"waitingForHuman",WhatsAppAttentionMode.Paused=>"paused",WhatsAppAttentionMode.Closed=>"closed",_=>"human"};
  var cd=new WhatsAppConversationDto{Id=c.Id,BranchId=c.BranchId,BranchName=c.Branch.Name,CustomerId=c.CustomerId,CustomerName=c.Customer?.Name,PhoneNumber=c.PhoneNumber,WhatsAppUsername=c.WhatsAppUsername,HasWhatsAppIdentity=!string.IsNullOrWhiteSpace(c.WhatsAppUserId),ContactName=c.ContactName,Status=c.Status==WhatsAppConversationStatus.Closed?"closed":"open",AttentionMode=attentionMode,LastMessageAt=c.LastMessageAt,LastMessagePreview=c.LastMessagePreview,UnreadCount=c.UnreadCount,CreatedAt=c.CreatedAt,UpdatedAt=c.UpdatedAt,AttentionModeUpdatedAt=c.AttentionModeUpdatedAt};
  var md=new WhatsAppMessageDto{Id=message.Id,ConversationId=c.Id,WhatsAppMessageId=message.WhatsAppMessageId,Direction="outbound",Type="text",TextBody=text,Status=result.Success?"sent":"failed",Timestamp=now,CreatedAt=message.CreatedAt};
  await notifications.NotifyMessageCreatedAsync(c.BranchId,cd,md,ct);
  return new(result.Success,false,result.WhatsAppMessageId,result.ErrorMessage);
 }
 private static bool IsRetrySafeTransient(string? error)
 {
  const string prefix="Meta WhatsApp HTTP ";
  if(string.IsNullOrWhiteSpace(error)||!error.StartsWith(prefix,StringComparison.Ordinal))return false;
  var separator=error.IndexOf(':',prefix.Length);if(separator<0)return false;
  if(!int.TryParse(error.AsSpan(prefix.Length,separator-prefix.Length),out var statusCode))return false;
  return statusCode is 408 or 409 or 429||statusCode>=500&&statusCode<=599;
 }
 private static bool IsTransient(string? error)=>IsRetrySafeTransient(error);
 public async Task<WhatsAppAutomaticSendResult> SendAgentContentAsync(int conversationId,string dispatchKey,string text,string? imageUrl,CancellationToken ct)
 {
  if(string.IsNullOrWhiteSpace(dispatchKey)||dispatchKey.Length>180)return new(false,false,null,"Clave de envío inválida.");var existing=await db.WhatsAppMessages.AsNoTracking().FirstOrDefaultAsync(x=>x.AgentDispatchKey==dispatchKey,ct);if(existing!=null)return existing.Status==WhatsAppMessageStatus.Sent?new(true,false,existing.WhatsAppMessageId,null):new(false,false,existing.WhatsAppMessageId,"Este envío ya fue intentado y no se repetirá automáticamente.");var c=await db.WhatsAppConversations.Include(x=>x.Branch).Include(x=>x.Customer).FirstOrDefaultAsync(x=>x.Id==conversationId,ct);if(c==null||c.AttentionMode!=WhatsAppAttentionMode.Ai)return new(false,false,null,"La conversación ya no está en IA.");var recipient=WhatsAppRecipientResolver.Resolve(c);if(recipient==null)return new(false,false,null,"La conversación no tiene un destinatario de WhatsApp válido.");var setting=await GetChannelAsync(c,ct);if(setting==null)return new(false,false,null,"WhatsApp no disponible.");var now=clock.UtcNow;var message=new WhatsAppMessage{ConversationId=c.Id,Direction=WhatsAppMessageDirection.Outbound,Type=string.IsNullOrWhiteSpace(imageUrl)?WhatsAppMessageType.Text:WhatsAppMessageType.Image,TextBody=text,MediaUrl=imageUrl,Status=WhatsAppMessageStatus.Failed,Timestamp=now,SentByAi=true,AgentDispatchKey=dispatchKey,RawPayload=JsonSerializer.Serialize(new{origin="ai_tool",dispatchKey})};db.WhatsAppMessages.Add(message);try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){db.Entry(message).State=EntityState.Detached;var duplicate=await db.WhatsAppMessages.AsNoTracking().FirstAsync(x=>x.AgentDispatchKey==dispatchKey,ct);return duplicate.Status==WhatsAppMessageStatus.Sent?new(true,false,duplicate.WhatsAppMessageId,null):new(false,false,duplicate.WhatsAppMessageId,"El envío ya está siendo procesado.");}var result=string.IsNullOrWhiteSpace(imageUrl)?await cloud.SendTextMessageAsync(setting.PhoneNumberId,setting.AccessToken,recipient,text,ct):await cloud.SendImageLinkMessageAsync(setting.PhoneNumberId,setting.AccessToken,recipient,imageUrl,text,ct);message.WhatsAppMessageId=result.WhatsAppMessageId;message.Status=result.Success?WhatsAppMessageStatus.Sent:WhatsAppMessageStatus.Failed;message.RawPayload=JsonSerializer.Serialize(new{origin="ai_tool",dispatchKey,result.Success,result.ErrorMessage});if(result.Success){c.LastMessageAt=now;c.LastMessagePreview=text;}await db.SaveChangesAsync(ct);var cd=new WhatsAppConversationDto{Id=c.Id,BranchId=c.BranchId,BranchName=c.Branch.Name,CustomerId=c.CustomerId,CustomerName=c.Customer?.Name,PhoneNumber=c.PhoneNumber,WhatsAppUsername=c.WhatsAppUsername,HasWhatsAppIdentity=!string.IsNullOrWhiteSpace(c.WhatsAppUserId),Status="open",AttentionMode="ai",LastMessageAt=c.LastMessageAt,LastMessagePreview=c.LastMessagePreview,UnreadCount=c.UnreadCount,CreatedAt=c.CreatedAt,UpdatedAt=c.UpdatedAt};var md=new WhatsAppMessageDto{Id=message.Id,ConversationId=c.Id,WhatsAppMessageId=message.WhatsAppMessageId,Direction="outbound",Type=message.Type==WhatsAppMessageType.Image?"image":"text",TextBody=text,MediaUrl=imageUrl,Status=result.Success?"sent":"failed",Timestamp=now,CreatedAt=message.CreatedAt};await notifications.NotifyMessageCreatedAsync(c.BranchId,cd,md,ct);return new(result.Success,!result.Success&&IsTransient(result.ErrorMessage),result.WhatsAppMessageId,result.ErrorMessage);
 }
 public async Task<WhatsAppAutomaticSendResult> SendAgentReplyButtonsAsync(int conversationId,string dispatchKey,string text,IReadOnlyList<WhatsAppReplyButton> buttons,CancellationToken ct)
 {
  if(buttons.Count is<1 or>3)return new(false,false,null,"Se requieren entre uno y tres botones.");var existing=await db.WhatsAppMessages.AsNoTracking().FirstOrDefaultAsync(x=>x.AgentDispatchKey==dispatchKey,ct);if(existing!=null)return existing.Status==WhatsAppMessageStatus.Sent?new(true,false,existing.WhatsAppMessageId,null):new(false,false,existing.WhatsAppMessageId,"El envío ya fue intentado.");var c=await db.WhatsAppConversations.Include(x=>x.Branch).FirstOrDefaultAsync(x=>x.Id==conversationId,ct);if(c==null||c.AttentionMode!=WhatsAppAttentionMode.Ai)return new(false,false,null,"La conversación ya no está en IA.");var recipient=WhatsAppRecipientResolver.Resolve(c);if(recipient==null)return new(false,false,null,"La conversación no tiene un destinatario de WhatsApp válido.");var setting=await GetChannelAsync(c,ct);if(setting==null)return new(false,false,null,"WhatsApp no disponible.");var result=await cloud.SendReplyButtonsMessageAsync(setting.PhoneNumberId,setting.AccessToken,recipient,text,buttons,ct);var now=clock.UtcNow;var message=new WhatsAppMessage{ConversationId=c.Id,WhatsAppMessageId=result.WhatsAppMessageId,Direction=WhatsAppMessageDirection.Outbound,Type=WhatsAppMessageType.Text,TextBody=text,Status=result.Success?WhatsAppMessageStatus.Sent:WhatsAppMessageStatus.Failed,Timestamp=now,SentByAi=true,AgentDispatchKey=dispatchKey,RawPayload=JsonSerializer.Serialize(new{origin="ai_tool_buttons",dispatchKey,buttons,result.Success,result.ErrorMessage})};db.WhatsAppMessages.Add(message);if(result.Success){c.LastMessageAt=now;c.LastMessagePreview=text;}await db.SaveChangesAsync(ct);return new(result.Success,!result.Success&&IsTransient(result.ErrorMessage),result.WhatsAppMessageId,result.ErrorMessage);
 }
 public async Task<WhatsAppAutomaticSendResult> SendTransferTextAsync(int conversationId,int incomingMessageId,string attemptId,string text,CancellationToken ct)
 {
  var c=await db.WhatsAppConversations.Include(x=>x.Branch).Include(x=>x.Customer).FirstOrDefaultAsync(x=>x.Id==conversationId,ct);var source=await db.WhatsAppMessages.FirstOrDefaultAsync(x=>x.Id==incomingMessageId&&x.ConversationId==conversationId,ct);if(c==null||source==null||c.AttentionMode!=WhatsAppAttentionMode.WaitingForHuman)return new(false,false,null,"La conversación no está esperando asesor.");var recipient=WhatsAppRecipientResolver.Resolve(c);if(recipient==null)return new(false,false,null,"La conversación no tiene un destinatario de WhatsApp válido.");if(!string.IsNullOrWhiteSpace(source.AiResponseWhatsAppMessageId))return new(true,false,source.AiResponseWhatsAppMessageId,null);var s=await GetChannelAsync(c,ct);if(s==null)return new(false,false,null,"WhatsApp no disponible.");var result=await cloud.SendTextMessageAsync(s.PhoneNumberId,s.AccessToken,recipient,text,ct);var now=clock.UtcNow;var m=new WhatsAppMessage{ConversationId=c.Id,WhatsAppMessageId=result.WhatsAppMessageId,Direction=WhatsAppMessageDirection.Outbound,Type=WhatsAppMessageType.Text,TextBody=text,Status=result.Success?WhatsAppMessageStatus.Sent:WhatsAppMessageStatus.Failed,Timestamp=now,SentByAi=true,RawPayload=JsonSerializer.Serialize(new{origin="ai_transfer",attemptId,result.Success,result.ErrorMessage})};db.WhatsAppMessages.Add(m);if(result.Success){source.AiResponseAttemptId=attemptId;source.AiResponseWhatsAppMessageId=result.WhatsAppMessageId;c.LastMessageAt=now;c.LastMessagePreview=text;}await db.SaveChangesAsync(ct);var cd=new WhatsAppConversationDto{Id=c.Id,BranchId=c.BranchId,BranchName=c.Branch.Name,PhoneNumber=c.PhoneNumber,WhatsAppUsername=c.WhatsAppUsername,HasWhatsAppIdentity=!string.IsNullOrWhiteSpace(c.WhatsAppUserId),Status="open",AttentionMode="waitingForHuman",AttentionReason=WhatsAppAiDiagnosticsMapper.SanitizeTechnicalDetail(source.AiProcessingError),LastMessageAt=c.LastMessageAt,LastMessagePreview=c.LastMessagePreview,CreatedAt=c.CreatedAt,UpdatedAt=c.UpdatedAt};var md=new WhatsAppMessageDto{Id=m.Id,ConversationId=c.Id,WhatsAppMessageId=m.WhatsAppMessageId,Direction="outbound",Type="text",TextBody=text,Status=result.Success?"sent":"failed",Timestamp=now,CreatedAt=m.CreatedAt};await notifications.NotifyMessageCreatedAsync(c.BranchId,cd,md,ct);return new(result.Success,!result.Success&&IsTransient(result.ErrorMessage),result.WhatsAppMessageId,result.ErrorMessage);
 }
 private async Task<ChannelCredentials?> GetChannelAsync(WhatsAppConversation conversation,CancellationToken ct,bool requireAway=false)
 {
  if(conversation.ChannelSettingId.HasValue)
  {
   var channel=await db.WhatsAppChannelSettings.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==conversation.ChannelSettingId&&x.TenantId==1&&x.IsActive&&x.IsVerified&&(!requireAway||x.AwayMessageEnabled),ct);
   return channel==null?null:new(channel.PhoneNumberId,channel.AccessToken);
  }
  var branch=await db.WhatsAppBranchSettings.AsNoTracking().FirstOrDefaultAsync(x=>x.BranchId==conversation.BranchId&&x.IsActive&&x.IsVerified&&(!requireAway||x.AwayMessageEnabled),ct);
  return branch==null?null:new(branch.PhoneNumberId,branch.AccessToken);
 }
 private sealed record ChannelCredentials(string PhoneNumberId,string AccessToken);
}
