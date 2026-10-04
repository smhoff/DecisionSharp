using System.Collections.ObjectModel;
using System.Text.Json;

namespace DecisionSharp.Core;

/// <summary>
/// Represents a request to evaluate a decision using a specified state, a set of questions,
/// and optionally a machine learning model.
/// </summary>
public sealed class DecisionRequest
{
    /// <summary>
    /// Represents the current state of the decision process as a JSON object.
    /// </summary>
    /// <remarks>
    /// This property contains a serialized JSON structure that defines the input state
    /// for a decision request. It is immutable and used throughout the decision engine
    /// to evaluate or process requests. The structure of the JSON is determined by the
    /// requirements of the decision model or logic.
    /// </remarks>
    public JsonElement State { get; }

    /// <summary>
    /// A collection of decision questions associated with the current decision request.
    /// </summary>
    /// <remarks>
    /// The <c>Questions</c> property represents a read-only dictionary where the keys are strings
    /// that uniquely identify each question, and the values are instances of <c>DecisionQuestion</c>.
    /// This property allows access to the questions being evaluated in the context of a decision request.
    /// The retrieval of these questions is crucial for processing and generating decision results in
    /// the decision engine workflow.
    /// </remarks>
    public IReadOnlyDictionary<string, DecisionQuestion> Questions { get; }

    /// <summary>
    /// Represents the model name associated with the decision request.
    /// This property is optional and can be null if no specific model is assigned.
    /// </summary>
    /// <remarks>
    /// The model name is utilized when generating requests, particularly in scenarios where specific contexts
    /// or configurations require association with a designated model.
    /// </remarks>
    public string? Model { get; }

    /// <summary>
    /// Represents a request structure used in decision-making processes.
    /// </summary>
    /// <remarks>
    /// A decision request contains the state, a set of questions, and optionally, a model
    /// that guides the evaluation process. It ensures that the state and questions are valid,
    /// and enforces constraints such as requiring at least one question. The provided state
    /// and questions are internally handled to prevent external modifications.
    /// </remarks>
    public DecisionRequest(JsonElement state, IReadOnlyDictionary<string, DecisionQuestion> questions,
        string? model = null)
    {
        State = InputJson.Own(state, nameof(state));
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
        {
            throw new ArgumentException("At least one question is required.", nameof(questions));
        }

        var copy = new Dictionary<string, DecisionQuestion>(StringComparer.Ordinal);
        foreach (var pair in questions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            copy.Add(pair.Key, pair.Value);
        }

        Questions = new ReadOnlyDictionary<string, DecisionQuestion>(copy);
        if (model is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(model);
        }

        Model = model;
    }
}