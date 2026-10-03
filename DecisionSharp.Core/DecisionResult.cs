using System.Collections.ObjectModel;
namespace DecisionSharp.Core;

public sealed class TokenUsage
{
    public long InputTokens { get; }
    public long OutputTokens { get; }
    public TokenUsage(long inputTokens, long outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputTokens));
        }

        InputTokens = inputTokens; OutputTokens = outputTokens;
    }
}
public sealed class DecisionResult
{
    public string Model { get; }
    public IReadOnlyDictionary<string, DecisionAnswer> Answers { get; }
    public TokenUsage Usage { get; }
    public DecisionResult(string model, IReadOnlyDictionary<string, DecisionAnswer> answers, TokenUsage usage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model); ArgumentNullException.ThrowIfNull(answers); ArgumentNullException.ThrowIfNull(usage);
        var copy = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        foreach (var pair in answers) { ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key); ArgumentNullException.ThrowIfNull(pair.Value); copy.Add(pair.Key, pair.Value); }
        Model = model; Answers = new ReadOnlyDictionary<string, DecisionAnswer>(copy); Usage = usage;
    }
    public TAnswer GetAnswer<TAnswer>(string id) where TAnswer : DecisionAnswer
    {
        if (!Answers.TryGetValue(id, out var answer))
        {
            throw new KeyNotFoundException($"No answer for '{id}'.");
        }

        return answer as TAnswer ?? throw new InvalidOperationException($"Answer '{id}' is not {typeof(TAnswer).Name}.");
    }
}
