namespace DecisionSharp.Reranking;

public sealed class RerankingOptions
{
    public int Concurrency { get; set; } = 4;
    public double? Threshold { get; set; }
    public int? TopK { get; set; }
    internal RerankingOptions Snapshot()
    {
        if (Concurrency <= 0) throw new ArgumentOutOfRangeException(nameof(Concurrency));
        if (TopK <= 0) throw new ArgumentOutOfRangeException(nameof(TopK));
        if (Threshold is { } threshold && (!double.IsFinite(threshold) || threshold < 0 || threshold > 1)) throw new ArgumentOutOfRangeException(nameof(Threshold));
        return new RerankingOptions { Concurrency = Concurrency, Threshold = Threshold, TopK = TopK };
    }
}
