using Microsoft.Extensions.Logging;
using SenorArroz.Application.Features.DeliveryRouting.Services;
using SenorArroz.Application.Options;
using System.Text.Json;

namespace SenorArroz.Tests;

public sealed class RoutingMeasurementSafetyTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ThrowingLoggerDoesNotChangeOperationalOutcome()
    {
        var settings = new RoutingMeasurementOptions
        {
            Enabled = true, StartedAtUtc = Now, ExpiresAtUtc = Now.AddDays(14)
        };
        var logger = new Recorder { Throw = true };
        var measurement = new RoutingMeasurement(settings, new FakeClock(Now), logger) { TenantId = 1 };
        Assert.Null(Record.Exception(() => measurement.Write(2, "generated")));
    }

    [Fact]
    public void ExpiredConfigurationProducesNoSamplesAfterAProcessRestart()
    {
        var settings = new RoutingMeasurementOptions
        {
            Enabled = true, StartedAtUtc = Now.AddDays(-14), ExpiresAtUtc = Now
        };
        var logger = new Recorder();
        new RoutingMeasurement(settings, new FakeClock(Now), logger) { TenantId = 1 }.Write(2, "reused");
        Assert.Empty(logger.Payloads);
    }

    [Fact]
    public void PayloadContainsOnlyDocumentedFieldsAndUniqueSampleIds()
    {
        var settings = new RoutingMeasurementOptions
        {
            Enabled = true, StartedAtUtc = Now, ExpiresAtUtc = Now.AddDays(14)
        };
        var logger = new Recorder();
        var clock = new FakeClock(Now);
        new RoutingMeasurement(settings, clock, logger) { TenantId = 1 }.Write(2, "reused");
        new RoutingMeasurement(settings, clock, logger) { TenantId = 1 }.Write(2, "failed");
        var allowed = new HashSet<string>
        {
            "schema", "sampleId", "algorithm", "timestampUtc", "experimentStartedAtUtc", "expiresAtUtc",
            "tenantId", "branchId", "outcome", "totalMs", "lockWaitMs", "inputMs", "candidates",
            "solverMsOnPlan", "googleCalls", "googleMs", "googleFailures", "planId", "version", "proposals", "unrouted",
            "routeSnapshots", "exclusions", "returnForecasts"
        };
        Assert.Equal(2, logger.Payloads.Count);
        using var first = JsonDocument.Parse(logger.Payloads[0]);
        using var second = JsonDocument.Parse(logger.Payloads[1]);
        Assert.NotEqual(first.RootElement.GetProperty("sampleId").GetString(), second.RootElement.GetProperty("sampleId").GetString());
        Assert.All(first.RootElement.EnumerateObject(), property => Assert.Contains(property.Name, allowed));
        Assert.Equal(JsonValueKind.Null, first.RootElement.GetProperty("solverMsOnPlan").ValueKind);
    }

    private sealed class Recorder : ILogger
    {
        public bool Throw { get; init; }
        public List<string> Payloads { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (Throw) throw new InvalidOperationException("Logger unavailable");
            var values = (IEnumerable<KeyValuePair<string, object?>>)(object)state!;
            Payloads.Add((string)values.Single(v => v.Key == "Payload").Value!);
        }
    }
}
