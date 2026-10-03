using DecisionSharp.Jev;
namespace DecisionSharp.Extensions.DependencyInjection;

public sealed class DecisionSharpOptions
{
    public JevOptions Jev { get; set; } = new();
    public bool EnableRetries { get; set; }
    public bool EnableCircuitBreaker { get; set; }
    public double CircuitFailureRatio { get; set; } = 0.5;
    public int CircuitMinimumThroughput { get; set; } = 10;
    public TimeSpan CircuitSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CircuitBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
    public DecisionCacheOptions Cache { get; set; } = new();
    internal DecisionSharpOptions Snapshot()
    {
        ArgumentNullException.ThrowIfNull(Jev); ArgumentNullException.ThrowIfNull(Cache);
        if (EnableCircuitBreaker && (!double.IsFinite(CircuitFailureRatio) || CircuitFailureRatio <= 0 || CircuitFailureRatio > 1 || CircuitMinimumThroughput < 2 || CircuitSamplingDuration < TimeSpan.FromMilliseconds(500) || CircuitBreakDuration < TimeSpan.FromMilliseconds(500)))
        {
            throw new ArgumentException("Invalid circuit-breaker settings.");
        }

        return new DecisionSharpOptions { Jev = Jev.Snapshot(), EnableRetries = EnableRetries, EnableCircuitBreaker = EnableCircuitBreaker, CircuitFailureRatio = CircuitFailureRatio, CircuitMinimumThroughput = CircuitMinimumThroughput, CircuitSamplingDuration = CircuitSamplingDuration, CircuitBreakDuration = CircuitBreakDuration, Cache = Cache.Snapshot() };
    }
}
public sealed class DecisionCacheOptions
{
    public bool Enabled { get; set; }
    public string? Namespace { get; set; }
    public int Capacity { get; set; } = 1024;
    public TimeSpan AbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(5);
    internal DecisionCacheOptions Snapshot()
    {
        if (Enabled)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Namespace); if (Capacity <= 0 || AbsoluteExpiration <= TimeSpan.Zero)
            {
                throw new ArgumentException("Invalid cache capacity or expiration.");
            }
        }
        return new DecisionCacheOptions { Enabled = Enabled, Namespace = Namespace, Capacity = Capacity, AbsoluteExpiration = AbsoluteExpiration };
    }
}
