using System.Collections.ObjectModel;

namespace DecisionSharp.Core;

/// <summary>
/// Represents the result of a decision evaluation process, containing the model information,
/// a collection of answers, and token usage details.
/// </summary>
public sealed class DecisionResult
{
    /// <summary>
    /// Gets the name of the model used for the decision result.
    /// </summary>
    /// <remarks>
    /// This property represents the specific model identifier that was utilized
    /// during the decision-making process. The model name can be used for
    /// tracing, debugging, or analytics purposes, and reflects the system or
    /// version employed in generating the result.
    /// </remarks>
    public string Model { get; }

    /// <summary>
    /// Gets the collection of decision answers associated with the result.
    /// </summary>
    /// <remarks>
    /// The <c>Answers</c> property provides a read-only dictionary containing unique identifiers
    /// as keys and their corresponding <see cref="DecisionAnswer"/> instances as values.
    /// Each entry represents a specific decision outcome within the result.
    /// </remarks>
    public IReadOnlyDictionary<string, DecisionAnswer> Answers { get; }

    /// <summary>
    /// Represents the usage metrics of tokens during the evaluation of a decision process.
    /// The usage is categorized into input and output tokens, providing insights into
    /// the computational resources involved in processing the decision.
    /// </summary>
    public TokenUsage Usage { get; }

    /// <summary>
    /// Represents the result of a decision-making process, including the model used,
    /// the answers provided, and token usage data.
    /// </summary>
    public DecisionResult(string model, IReadOnlyDictionary<string, DecisionAnswer> answers, TokenUsage usage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(usage);
        var copy = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        foreach (var pair in answers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            copy.Add(pair.Key, pair.Value);
        }

        Model = model;
        Answers = new ReadOnlyDictionary<string, DecisionAnswer>(copy);
        Usage = usage;
    }

    /// Retrieves a specific answer by its identifier and casts it to the specified type.
    /// <typeparam name="TAnswer">The type of the answer to retrieve, which must derive from DecisionAnswer.</typeparam>
    /// <param name="id">The identifier of the desired answer.</param>
    /// <returns>The answer corresponding to the provided identifier, cast to the specified type.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no answer is found with the specified identifier.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the answer exists but cannot be cast to the specified type.</exception>
    public TAnswer GetAnswer<TAnswer>(string id) where TAnswer : DecisionAnswer
    {
        if (!Answers.TryGetValue(id, out var answer))
        {
            throw new KeyNotFoundException($"No answer for '{id}'.");
        }

        return answer as TAnswer ??
            throw new InvalidOperationException($"Answer '{id}' is not {typeof(TAnswer).Name}.");
    }
}