using System.Diagnostics;
using System.Diagnostics.Metrics;
namespace DecisionSharp.Jev;

/// <summary>
/// Provides telemetry capabilities for monitoring the performance and behavior
/// of decision-making engines within the DecisionSharp framework.
/// This static class defines telemetry instrumentation, including metrics and
/// activity tracking, that can be used to observe evaluations, failures, cache hits,
/// and the duration of operations.
/// </summary>
internal static class DecisionTelemetry
{
    /// <summary>
    /// Represents an <see cref="ActivitySource"/> for generating and managing
    /// diagnostic activities within the DecisionSharp library. Activities are used
    /// to trace the execution of operations, enabling telemetry collection for
    /// performance analysis and debugging purposes.
    /// </summary>
    internal static readonly ActivitySource Activities = new("DecisionSharp");

    /// <summary>
    /// Represents a telemetry meter used for defining and managing various metrics
    /// related to decision-making processes within the system.
    /// </summary>
    /// <remarks>
    /// This meter is utilized for observing and recording telemetry information,
    /// including evaluations, failures, cache hits, and durations of decision operations.
    /// </remarks>
    private static readonly Meter meter = new("DecisionSharp");

    /// <summary>
    /// A performance counter that tracks the total number of evaluations performed by the system.
    /// </summary>
    /// <remarks>
    /// The counter is incremented each time an evaluation process is executed
    /// successfully or unsuccessfully. This metric can be useful for monitoring the system's
    /// overall throughput and usage trends over time.
    /// </remarks>
    internal static readonly Counter<long> Evaluations = meter.CreateCounter<long>("decision.evaluations");

    /// <summary>
    /// Tracks the count of evaluation failures in the decision-making process.
    /// </summary>
    /// <remarks>
    /// This counter is incremented whenever an operation within the decision engine fails,
    /// such as when a timeout, exception, or other error condition occurs. The failures
    /// are categorized and tagged with information such as provider details and question type,
    /// aiding in the monitoring and debugging of decision-processing issues.
    /// </remarks>
    internal static readonly Counter<long> Failures = meter.CreateCounter<long>("decision.failures");

    /// <summary>
    /// Tracks the number of times a cache hit occurs during decision evaluations.
    /// </summary>
    /// <remarks>
    /// This counter is incremented each time a decision request is successfully retrieved
    /// from the cache instead of being evaluated anew. It helps monitor the efficiency
    /// and utilization of the caching mechanism.
    /// </remarks>
    internal static readonly Counter<long> CacheHits = meter.CreateCounter<long>("decision.cache.hits");

    /// <summary>
    /// Represents a histogram for recording the duration of decision evaluations in seconds.
    /// </summary>
    /// <remarks>
    /// This variable is used to measure the duration of operations performed during decision evaluation
    /// processes. The recorded durations are utilized for telemetry and performance monitoring purposes.
    /// </remarks>
    internal static readonly Histogram<double> Duration = meter.CreateHistogram<double>("decision.duration", "s");
}
