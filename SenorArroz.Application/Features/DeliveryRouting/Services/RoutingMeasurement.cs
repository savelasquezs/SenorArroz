using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DeliveryRouting.DTOs;
using SenorArroz.Application.Options;

namespace SenorArroz.Application.Features.DeliveryRouting.Services;

/// <summary>Temporary P0 instrumentation. Removal checklist: docs/ROUTING_P0_P1.md.</summary>
internal sealed class RoutingMeasurement(RoutingMeasurementOptions options, IClock clock, ILogger logger)
{
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    internal int TenantId { get; set; }
    internal int CandidateCount { get; set; }
    internal long InputMs { get; set; }
    internal long LockWaitMs { get; set; }
    internal int GoogleCalls { get; set; }
    internal long GoogleMs { get; set; }
    internal int GoogleFailures { get; set; }

    internal void Write(int branchId, string outcome, DeliveryRoutingPlanDto? plan = null)
    {
        // The clock and logger can fail too; diagnostics never fail a routing request.
        try
        {
            if (!options.IsActive(clock.UtcNow) || TenantId <= 0) return;
            logger.LogInformation("RoutingMeasurementV1 {Payload}", JsonSerializer.Serialize(new
            {
                schema = 1, sampleId = Guid.NewGuid(), algorithm = "routing-p1", timestampUtc = clock.UtcNow,
                experimentStartedAtUtc = options.StartedAtUtc, expiresAtUtc = options.ExpiresAtUtc,
                tenantId = TenantId, branchId, outcome, totalMs = _elapsed.ElapsedMilliseconds,
                lockWaitMs = LockWaitMs, inputMs = InputMs, candidates = CandidateCount,
                solverMsOnPlan = outcome == "generated" ? plan?.SolverDurationMs : null,
                googleCalls = GoogleCalls, googleMs = GoogleMs, googleFailures = GoogleFailures,
                planId = plan?.Id, version = plan?.Version,
                proposals = plan?.Proposals.Count, unrouted = plan?.UnroutedOrders.Count
            }));
        }
        catch { /* Experiment logging must not change operational success/failure. */ }
    }
}
