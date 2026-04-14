using SenorArroz.Domain.Entities;

namespace SenorArroz.Tests;

public class PasswordResetTokenClockTests
{
    [Fact]
    public void IsExpiredAt_e_IsValidAt_usan_el_instante_suministrado()
    {
        var created = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var token = PasswordResetToken.Create(1, "x@y.co", 60, created);

        Assert.False(token.IsExpiredAt(created.AddMinutes(59)));
        Assert.True(token.IsExpiredAt(created.AddMinutes(60)));

        Assert.True(token.IsValidAt(created.AddMinutes(59)));
        Assert.False(token.IsValidAt(created.AddMinutes(60)));
    }

    [Fact]
    public void FakeClock_expone_instante_configurable()
    {
        var t = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var clock = new FakeClock(t);
        Assert.Equal(t, clock.UtcNow);

        clock.UtcNow = t.AddHours(1);
        Assert.Equal(t.AddHours(1), clock.UtcNow);
    }
}
