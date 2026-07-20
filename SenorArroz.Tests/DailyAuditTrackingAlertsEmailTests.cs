using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SenorArroz.Domain.Models;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class DailyAuditTrackingAlertsEmailTests
{
    [Fact]
    public async Task DailyAuditEmail_IncludesTrackingAlertSummary()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new EmailService(
            db,
            configuration,
            NullLogger<EmailService>.Instance,
            new FakeClock(new DateTime(2026, 7, 20, 23, 0, 0, DateTimeKind.Utc)));
        var payload = new DailyMonetaryAuditEmailPayload
        {
            BranchName = "Centro",
            BusinessDate = new DateTime(2026, 7, 20),
            PeriodStartUtc = new DateTime(2026, 7, 20, 5, 0, 0, DateTimeKind.Utc),
            PeriodEndUtc = new DateTime(2026, 7, 21, 4, 59, 0, DateTimeKind.Utc),
            TrackingAlertGroups =
            [
                new DailyTrackingAlertEmailGroup
                {
                    Title = "GPS apagado",
                    Severity = "Advertencia",
                    EventCount = 3,
                    ActiveCount = 1,
                }
            ],
        };

        var result = await service.SendDailyMonetaryAuditEmailAsync(["admin@test.co"], payload);

        Assert.True(result.Success);
        var email = Assert.Single(db.EmailOutboxMessages);
        Assert.Contains("Auditoría diaria", email.Subject);
        Assert.Contains("Alertas de seguimiento de domiciliarios", email.Body);
        Assert.Contains("GPS apagado", email.Body);
        Assert.Contains(">3<", email.Body);
    }
}
