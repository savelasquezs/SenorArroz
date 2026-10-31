namespace SenorArroz.Application.Options;

public sealed class WhatsAppProcessingOptions
{
    public const string SectionName = "WhatsAppProcessing";
    public string Mode { get; set; } = "Embedded";
    public int Concurrency { get; set; } = 4;
    public int PollSeconds { get; set; } = 15;
    public int LeaseSeconds { get; set; } = 180;
    public int WorkTimeoutSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 5;
    public string BusinessApiUrl { get; set; } = string.Empty;
    public string WorkerKey { get; set; } = string.Empty;
    public bool External => string.Equals(Mode, "External", StringComparison.OrdinalIgnoreCase);

    public bool IsValid() => (External || string.Equals(Mode, "Embedded", StringComparison.OrdinalIgnoreCase))
        && Concurrency is >= 1 and <= 16 && PollSeconds is >= 1 and <= 60
        && WorkTimeoutSeconds is >= 5 and <= 600 && LeaseSeconds > WorkTimeoutSeconds
        && LeaseSeconds <= 900 && MaxAttempts is >= 1 and <= 10;
}
