namespace DecisionSharp.Reranking;

/// <summary>
/// Represents a piece of evidence that has been scored based on its relevance to a query.
/// </summary>
/// <remarks>
/// The <see cref="ScoredEvidence"/> class encapsulates an instance of <see cref="Evidence"/>,
/// its computed relevance probability, and the name of the decision model used to compute the score.
/// This class is immutable and is primarily intended for use in ranking or filtering operations.
/// </remarks>
public sealed class ScoredEvidence
{
    /// <summary>
    /// Represents a unit of information or data used during the reranking process.
    /// </summary>
    /// <remarks>
    /// The <see cref="Evidence"/> class encapsulates essential details about a piece of content,
    /// including its identifier, textual content, optional metadata, and an optional retrieval score,
    /// allowing it to be evaluated and prioritized according to specific criteria in the reranking process.
    /// </remarks>
    public Evidence Evidence { get; }

    /// <summary>
    /// Gets the probability that the associated evidence is relevant to the given query,
    /// as computed by the reranking model.
    /// </summary>
    /// <remarks>
    /// This value is a double-precision floating-point number in the range [0, 1], where
    /// higher values indicate greater relevance. It is calculated based on the scoring logic
    /// of the reranking model applied to the evidence.
    /// </remarks>
    public double RelevanceProbability { get; }

    /// <summary>
    /// Gets the name of the resolved model that was used during the reranking process.
    /// This property provides insight into which model contributed to generating the
    /// <see cref="RelevanceProbability"/> score for the associated evidence instance.
    /// </summary>
    public string ResolvedModel { get; }

    /// Represents evidence that has been scored based on its relevance to a given query.
    /// This class encapsulates an instance of the Evidence object, its relevance probability,
    /// and the resolved model used for scoring.
    internal ScoredEvidence(Evidence evidence, double relevanceProbability, string resolvedModel)
    { this.Evidence = evidence; this.RelevanceProbability = relevanceProbability; this.ResolvedModel = resolvedModel; }
}