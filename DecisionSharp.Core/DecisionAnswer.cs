using System.Collections.ObjectModel;
namespace DecisionSharp.Core;

public abstract class DecisionAnswer { private protected DecisionAnswer() { } }
public sealed class YesNoAnswer : DecisionAnswer
{
    public double ProbabilityOfYes { get; }
    public YesNoAnswer(double probabilityOfYes) { AnswerValidation.Probability(probabilityOfYes); ProbabilityOfYes = probabilityOfYes; }
}
public sealed class ChoiceAnswer : DecisionAnswer
{
    public string Choice { get; }
    public IReadOnlyDictionary<string, double> Probabilities { get; }
    public double Confidence { get; }
    public ChoiceAnswer(string choice, IReadOnlyDictionary<string, double> probabilities, double confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choice); AnswerValidation.Probability(confidence);
        Probabilities = AnswerValidation.Distribution(probabilities);
        if (!Probabilities.ContainsKey(choice))
        {
            throw new ArgumentException("Choice is absent from its distribution.", nameof(choice));
        }

        Choice = choice; Confidence = confidence;
    }
}
public sealed class ScoreAnswer : DecisionAnswer
{
    public double Score { get; }
    public IReadOnlyDictionary<string, string> Legend { get; }
    public IReadOnlyDictionary<string, double> Probabilities { get; }
    public double Confidence { get; }
    public ScoreAnswer(double score, IReadOnlyDictionary<string, string> legend, IReadOnlyDictionary<string, double> probabilities, double confidence)
    {
        ArgumentNullException.ThrowIfNull(legend); AnswerValidation.Probability(confidence);
        Probabilities = AnswerValidation.Distribution(probabilities);
        if (legend.Count is < 2 or > 10 || !double.IsFinite(score) || score < 0 || score > legend.Count - 1)
        {
            throw new ArgumentException("Invalid score or level count.");
        }

        for (int i = 0; i < legend.Count; i++)
        {
            var key = i.ToString(System.Globalization.CultureInfo.InvariantCulture); if (!legend.ContainsKey(key) || !Probabilities.ContainsKey(key) || legend[key] is null)
            {
                throw new ArgumentException("Incomplete numeric legend/distribution.");
            }
        }
        if (Probabilities.Count != legend.Count)
        {
            throw new ArgumentException("Legend/distribution mismatch.");
        }

        Legend = new ReadOnlyDictionary<string, string>(legend.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)); Score = score; Confidence = confidence;
    }
}
internal static class AnswerValidation
{
    internal static void Probability(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentException("Probability/confidence must be finite and in [0,1].");
        }
    }
    internal static IReadOnlyDictionary<string, double> Distribution(IReadOnlyDictionary<string, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            throw new ArgumentException("Distribution needs at least two entries.");
        }

        var copy = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in values) { ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key); Probability(pair.Value); copy.Add(pair.Key, pair.Value); }
        if (Math.Abs(copy.Values.Sum() - 1) > 0.001 + 1e-12)
        {
            throw new ArgumentException("Distribution must sum to one within 0.001.");
        }

        return new ReadOnlyDictionary<string, double>(copy);
    }
}
