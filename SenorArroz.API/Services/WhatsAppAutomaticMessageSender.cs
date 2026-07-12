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
 public async Task<bool> SendTextAsync(int conversationId,string text,CancellationToken ct){var c=await db.WhatsAppConversations.Include(x=>x.Branch).Include(x=>x.Customer).FirstOrDefaultAsync(x=>x.Id==conversationId,ct);if(c==null||c.AttentionMode!=WhatsAppAttentionMode.Ai)return false;var s=await db.WhatsAppBranchSettings.AsNoTracking().FirstOrDefaultAsync(x=>x.BranchId==c.BranchId&&x.IsActive&&x.IsVerified,ct);if(s==null)return false;var result=await cloud.SendTextMessageAsync(s.PhoneNumberId,s.AccessToken,c.PhoneNumber,text,ct);var now=clock.UtcNow;var m=new WhatsAppMessage{ConversationId=c.Id,WhatsAppMessageId=result.WhatsAppMessageId,Direction=WhatsAppMessageDirection.Outbound,Type=WhatsAppMessageType.Text,TextBody=text,Status=result.Success?WhatsAppMessageStatus.Sent:WhatsAppMessageStatus.Failed,Timestamp=now,SentByAi=true,RawPayload=JsonSerializer.Serialize(new{origin="ai",result.Success,result.ErrorMessage})};db.WhatsAppMessages.Add(m);c.LastMessageAt=now;c.LastMessagePreview=text;await db.SaveChangesAsync(ct);if(!result.Success)return false;await notifications.NotifyMessageCreatedAsync(c.BranchId,new WhatsAppConversationDto{Id=c.Id,BranchId=c.BranchId,BranchName=c.Branch.Name,CustomerId=c.CustomerId,CustomerName=c.Customer?.Name,PhoneNumber=c.PhoneNumber,ContactName=c.ContactName,Status="open",AttentionMode="ai",LastMessageAt=now,LastMessagePreview=text,UnreadCount=c.UnreadCount,CreatedAt=c.CreatedAt,UpdatedAt=c.UpdatedAt,AttentionModeUpdatedAt=c.AttentionModeUpdatedAt},new WhatsAppMessageDto{Id=m.Id,ConversationId=c.Id,WhatsAppMessageId=m.WhatsAppMessageId,Direction="outbound",Type="text",TextBody=text,Status="sent",Timestamp=now,CreatedAt=m.CreatedAt},ct);return true;}
}
