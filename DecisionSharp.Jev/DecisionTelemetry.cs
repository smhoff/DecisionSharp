using System.Diagnostics;
using System.Diagnostics.Metrics;
namespace DecisionSharp.Jev;

internal static class DecisionTelemetry
{
    internal static readonly ActivitySource Activities = new("DecisionSharp");
    private static readonly Meter Meter = new("DecisionSharp");
    internal static readonly Counter<long> Evaluations = Meter.CreateCounter<long>("decision.evaluations");
    internal static readonly Counter<long> Failures = Meter.CreateCounter<long>("decision.failures");
    internal static readonly Counter<long> CacheHits = Meter.CreateCounter<long>("decision.cache.hits");
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("decision.duration", "s");
}
