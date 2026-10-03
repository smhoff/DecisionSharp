using System.Text.Json;
namespace DecisionSharp.Core;
public interface IDecisionEngine
{
    Task<DecisionResult> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default);
    Task<DecisionResult> EvaluateAsync<TState>(TState state, IReadOnlyDictionary<string,DecisionQuestion> questions, string? model = null, CancellationToken cancellationToken = default)
        => EvaluateAsync(new DecisionRequest(JsonSerializer.SerializeToElement(state), questions, model), cancellationToken);
}
