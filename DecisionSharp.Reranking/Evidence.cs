using System.Collections.ObjectModel;
using System.Text.Json;

namespace DecisionSharp.Reranking;

/// <summary>
/// Represents a piece of evidence with an associated identifier, content, optional metadata, and an optional retrieval score.
/// </summary>
public sealed class Evidence
{
    /// <summary>
    /// Gets the unique identifier for the evidence instance.
    /// </summary>
    /// <remarks>
    /// The identifier is used to uniquely distinguish each evidence object. It is required to be a
    /// non-null and non-whitespace string. This property is immutable and is assigned during the
    /// construction of the <c>Evidence</c> object.
    /// </remarks>
    public string Id { get; }

    /// <summary>
    /// Gets the textual content associated with the evidence.
    /// This property contains the core data or information that
    /// is used for processing, evaluation, or ranking tasks.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets the metadata associated with the evidence.
    /// This metadata is represented as a dictionary where the keys are strings and the values are JSON elements.
    /// It provides additional contextual information about the evidence, such as its source or other attributes.
    /// </summary>
    /// <remarks>
    /// The metadata is optional and can be null if no metadata is provided for the evidence.
    /// If the metadata is specified, it is stored internally as a read-only dictionary to ensure immutability.
    /// </remarks>
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; }

    /// <summary>
    /// Represents the retrieval score associated with an evidence instance.
    /// This score indicates the relevance of the evidence based on retrieval algorithms,
    /// and can be used for ranking or filtering purposes during the reranking process.
    /// </summary>
    /// <remarks>
    /// The value of the retrieval score is nullable and must be a finite double if specified.
    /// A null value indicates the absence of a retrieval score for the evidence.
    /// </remarks>
    public double? RetrievalScore { get; }

    /// Represents an evidence object used in reranking operations.
    ///
    /// This class encapsulates the core properties and behaviors of a piece of evidence used
    /// in relevance-based reranking systems. Instances of this class are immutable.
    ///
    /// Properties:
    /// - `Id`: A unique identifier associated with the evidence.
    /// - `Content`: The textual content of the evidence.
    /// - `RetrievalScore`: An optional numerical score representing the retrieval rank or relevance.
    /// - `Metadata`: An optional read-only dictionary containing additional metadata for the evidence.
    ///
    /// Constructor:
    /// The constructor enforces validation on the input data:
    /// - Throws an `ArgumentException` if `id` or `content` is null, empty, or consists only of whitespace.
    /// - Throws an `ArgumentException` if `retrievalScore` is not a finite double value.
    /// - Converts `metadata` into a read-only dictionary with cloned values (if provided).
    public Evidence(string id, string content, IReadOnlyDictionary<string, JsonElement>? metadata = null,
        double? retrievalScore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (retrievalScore is { } score && !double.IsFinite(score))
        {
            throw new ArgumentException("Retrieval score must be finite.", nameof(retrievalScore));
        }

        Id = id;
        Content = content;
        RetrievalScore = retrievalScore;
        if (metadata is not null)
        {
            Metadata = new ReadOnlyDictionary<string, JsonElement>(metadata.ToDictionary(p => p.Key,
                p => p.Value.Clone(), StringComparer.Ordinal));
        }
    }
}