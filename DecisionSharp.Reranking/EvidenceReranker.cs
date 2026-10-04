using System.Runtime.ExceptionServices;
using System.Text.Json;
using DecisionSharp.Core;

namespace DecisionSharp.Reranking;

using Core.Answers;
using Core.Questions;

/// <summary>
/// Provides functionalities for re-ranking a list of evidence based on their relevance to a given query.
/// The class ensures stable ordering, applies relevance thresholds, and supports parallel processing
/// for improved efficiency.
/// </summary>
public sealed class EvidenceReranker
{
    /// <summary>
    /// Represents the decision engine utilized for evaluating decision requests.
    /// The engine implements the <see cref="IDecisionEngine"/> interface and serves
    /// as the core component for processing and scoring evidence based on relevance
    /// to user-defined queries.
    /// </summary>
    private readonly IDecisionEngine engine;

    /// <summary>
    /// Configuration options for controlling the behavior of the EvidenceReranker.
    /// </summary>
    /// <remarks>
    /// The <c>options</c> field determines key parameters for the reranking process,
    /// such as concurrency level, scoring thresholds, and the limit on the number of
    /// results returned.
    /// </remarks>
    /// <seealso cref="RerankingOptions"/>
    private readonly RerankingOptions options;

    /// <summary>
    /// A service responsible for reranking evidence data based on a given query and configurable options.
    /// </summary>
    public EvidenceReranker(IDecisionEngine engine, RerankingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        this.engine = engine;
        this.options = (options ?? new()).Snapshot();
    }

    /// <summary>
    /// Re-ranks the given list of evidence based on their relevance to the specified query.
    /// </summary>
    /// <param name="query">The query string used to evaluate the relevance of the evidence.</param>
    /// <param name="evidence">The list of evidence to be re-ranked.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous operation. The task's result is a read-only list of ranked evidence with their associated relevance scores.</returns>
    public async Task<IReadOnlyList<ScoredEvidence>> RerankAsync
        (string query, IReadOnlyList<Evidence> evidence, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(evidence);
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = evidence.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (!ids.Add(candidate.Id))
            {
                throw new ArgumentException("Duplicate evidence ID.", nameof(evidence));
            }
        }

        if (candidates.Length == 0)
        {
            return [];
        }

        var results = new ScoredEvidence[candidates.Length];
        ExceptionDispatchInfo? firstFailure = null;
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, candidates.Length),
                new ParallelOptions { MaxDegreeOfParallelism = options.Concurrency, CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var candidate = candidates[index];
                        var request = new DecisionRequest(
                            JsonSerializer.SerializeToElement(new { query, content = candidate.Content }),
                            new Dictionary<string, DecisionQuestion>
                            {
                                ["relevance"] =
                                    new YesNoQuestion("Does the content supply relevant evidence for the query?")
                            });
                        var result = await engine.EvaluateAsync(request, token).ConfigureAwait(false);
                        results[index] = new ScoredEvidence(candidate,
                            result.GetAnswer<YesNoAnswer>("relevance").ProbabilityOfYes, result.Model);
                    }
                    catch (Exception ex)
                    {
                        // Parallel.ForEachAsync cancels token for the caller and after any body throws.
                        if (ex is OperationCanceledException && token.IsCancellationRequested)
                        {
                            throw;
                        }

                        Interlocked.CompareExchange(ref firstFailure, ExceptionDispatchInfo.Capture(ex), null);
                        throw;
                    }
                }).ConfigureAwait(false);
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            firstFailure?.Throw();
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ordered = results.Select((item, index) => (item, index))
            .OrderByDescending(pair => pair.item.RelevanceProbability).ThenBy(pair => pair.index)
            .Select(pair => pair.item);
        IEnumerable<ScoredEvidence> filtered = options.Threshold is { } threshold
            ? ordered.Where(item => item.RelevanceProbability >= threshold) : ordered;
        if (options.TopK is { } count)
        {
            filtered = filtered.Take(count);
        }

        return Array.AsReadOnly(filtered.ToArray());
    }
}