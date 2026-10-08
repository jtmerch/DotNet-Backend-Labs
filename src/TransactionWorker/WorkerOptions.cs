namespace TransactionWorker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public int MaxAttempts { get; set; } = 3;
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a transaction can sit in Submitted before it's assumed the worker that
    /// claimed it crashed, making it eligible to be claimed again.
    /// </summary>
    public TimeSpan StaleClaimThreshold { get; set; } = TimeSpan.FromSeconds(30);
}
