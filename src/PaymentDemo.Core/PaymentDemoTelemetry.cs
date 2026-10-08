using System.Diagnostics;

namespace PaymentDemo.Core;

/// <summary>
/// One shared ActivitySource for the custom spans this project emits on top of the
/// automatic ASP.NET Core / HttpClient instrumentation - idempotency cache lookups,
/// claiming a transaction, and each simulator attempt.
/// </summary>
public static class PaymentDemoTelemetry
{
    public const string SourceName = "PaymentDemo";

    public static readonly ActivitySource Source = new(SourceName);
}
