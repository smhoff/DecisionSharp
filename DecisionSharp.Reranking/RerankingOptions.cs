namespace DecisionSharp.Reranking;

/// <summary>
/// Configuration options for reranking operations within the DecisionSharp framework.
/// </summary>
public sealed class RerankingOptions
{
    /// <summary>
    /// Gets or sets the maximum number of concurrent operations allowed during the reranking process.
    /// </summary>
    /// <remarks>
    /// A positive integer value is required. If set to a non-positive value, an <see cref="ArgumentOutOfRangeException"/>
    /// will be thrown. By default, the value is set to 4.
    /// </remarks>
    public int Concurrency { get; set; } = 4;

    /// <summary>
    /// Specifies the minimum score required for an evidence item to be considered during the reranking process.
    /// </summary>
    /// <remarks>
    /// The value should be a double between 0.0 and 1.0, inclusive. If set to <c>null</c>, no threshold is applied.
    /// This property filters out evidence items with scores below the specified threshold before applying further
    /// reranking logic.
    /// </remarks>
    public double? Threshold { get; set; }

    /// <summary>
    /// Specifies the maximum number of top-ranked results to retain after reranking.
    /// </summary>
    /// <remarks>
    /// When defined, this property limits the number of results returned from the reranker
    /// to the top K entries based on their scores. If not set, all entries are retained,
    /// subject to other options such as thresholds. A value below or equal to zero
    /// is considered invalid and will raise an <see cref="ArgumentOutOfRangeException"/>.
    /// </remarks>
    public int? TopK { get; set; }

    /// <summary>
    /// Creates a snapshot of the current <see cref="RerankingOptions"/> instance.
    /// The snapshot reflects the current state of the configuration
    /// and validates the properties to ensure they meet constraints.
    /// </summary>
    /// <returns>
    /// A new <see cref="RerankingOptions"/> instance that mirrors the current configuration state.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the <see cref="Concurrency"/> value is less than or equal to zero,
    /// <see cref="TopK"/> value is less than or equal to zero,
    /// or <see cref="Threshold"/> is not within the valid range [0, 1].
    /// </exception>
    internal RerankingOptions Snapshot()
    {
        if (Concurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Concurrency));
        }

        if (TopK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TopK));
        }

        if (Threshold is { } threshold && (!double.IsFinite(threshold) || threshold < 0 || threshold > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(Threshold));
        }

        return new RerankingOptions { Concurrency = Concurrency, Threshold = Threshold, TopK = TopK };
    }
}