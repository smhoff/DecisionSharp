using System.Text.Json;

namespace DecisionSharp.Core;

/// <summary>
/// Represents an engine capable of evaluating decision-making processes based on input requests.
/// Provides methods to evaluate decisions asynchronously with various input configurations.
/// </summary>
public interface IDecisionEngine
{
    /// <summary>
    /// Evaluates the provided decision request asynchronously and returns the result.
    /// </summary>
    /// <param name="request">
    /// The decision request containing the state, questions, and optional model to evaluate.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the task to complete. This parameter is optional and defaults to <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation, which upon completion contains the evaluation result as a <see cref="DecisionResult"/>.
    /// </returns>
    Task<DecisionResult> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default);

    /// Evaluates a decision asynchronously based on the provided input state and questions.
    /// Allows an optional model to be specified for evaluation.
    ///
    /// <typeparam name="TState">The type of the input state to be evaluated.</typeparam>
    /// <param name="state">The state input used for the evaluation.</param>
    /// <param name="questions">A dictionary of decision questions to be evaluated.</param>
    /// <param name="model">An optional string specifying the model to use for evaluation. Defaults to null.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the decision result of the evaluation.</returns>
    Task<DecisionResult> EvaluateAsync<TState>(TState state, IReadOnlyDictionary<string, DecisionQuestion> questions,
        string? model = null, CancellationToken cancellationToken = default)
        => EvaluateAsync(new DecisionRequest(JsonSerializer.SerializeToElement(state), questions, model),
            cancellationToken);
}