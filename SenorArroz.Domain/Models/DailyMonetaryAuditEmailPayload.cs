namespace SenorArroz.Domain.Models;

public class DailyMonetaryAuditEmailPayload
{
    public string BranchName { get; set; } = string.Empty;
    public DateTime BusinessDate { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public IReadOnlyCollection<string> RecipientEmails { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<DailyMonetaryAuditEmailGroup> Groups { get; set; } = Array.Empty<DailyMonetaryAuditEmailGroup>();
    public IReadOnlyCollection<DailyTrackingAlertEmailGroup> TrackingAlertGroups { get; set; } = Array.Empty<DailyTrackingAlertEmailGroup>();
}

public class DailyTrackingAlertEmailGroup
{
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public int ActiveCount { get; set; }
    public IReadOnlyCollection<DailyTrackingAlertEmailDetail> Details { get; set; } = Array.Empty<DailyTrackingAlertEmailDetail>();
}

public class DailyTrackingAlertEmailDetail
{
    public string DeliverymanName { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? StartLatitude { get; set; }
    public decimal? StartLongitude { get; set; }
    public DateTime? StartLocationRecordedAt { get; set; }
    public string StartLocationLabel { get; set; } = "Ver ubicación inicial";
    public decimal? EndLatitude { get; set; }
    public decimal? EndLongitude { get; set; }
    public DateTime? EndLocationRecordedAt { get; set; }
    public string EndLocationLabel { get; set; } = "Ver ubicación final";
}

public class DailyMonetaryAuditEmailGroup
{
    public string Title { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public decimal NetDifference { get; set; }
    public IReadOnlyCollection<string> Lines { get; set; } = Array.Empty<string>();
}
