namespace SenorArroz.Application.Options;

public sealed class WhatsAppProcessingOptions
{
    public const string SectionName = "WhatsAppProcessing";
    public string Mode { get; set; } = "Embedded";
    public int Concurrency { get; set; } = 4;
    public int AiConcurrency { get; set; } = 2;
    public int PollSeconds { get; set; } = 15;
    public int LeaseSeconds { get; set; } = 180;
    public int WorkTimeoutSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 5;
    public int MetricsIntervalSeconds { get; set; } = 30;
    public int RealtimeRetentionDays { get; set; } = 2;
    public int PayloadRetentionDays { get; set; } = 7;
    public int CleanupBatchSize { get; set; } = 500;
    public string BusinessApiUrl { get; set; } = string.Empty;
    public string WorkerKey { get; set; } = string.Empty;
    public bool External => string.Equals(Mode, "External", StringComparison.OrdinalIgnoreCase);

    public bool IsValid() => (External || string.Equals(Mode, "Embedded", StringComparison.OrdinalIgnoreCase))
        && Concurrency is >= 1 and <= 16 && AiConcurrency is >= 1 and <= 8 && PollSeconds is >= 1 and <= 60
        && WorkTimeoutSeconds is >= 5 and <= 600 && LeaseSeconds > WorkTimeoutSeconds
        && LeaseSeconds <= 900 && MaxAttempts is >= 1 and <= 10
        && MetricsIntervalSeconds is >= 5 and <= 300 && RealtimeRetentionDays is >= 1 and <= 30
        && PayloadRetentionDays is >= 1 and <= 90 && CleanupBatchSize is >= 1 and <= 2000;
}
