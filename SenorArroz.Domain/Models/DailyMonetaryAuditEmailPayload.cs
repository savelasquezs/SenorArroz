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
}

public class DailyMonetaryAuditEmailGroup
{
    public string Title { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public decimal NetDifference { get; set; }
    public IReadOnlyCollection<string> Lines { get; set; } = Array.Empty<string>();
}
