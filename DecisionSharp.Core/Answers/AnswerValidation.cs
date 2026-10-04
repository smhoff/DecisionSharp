namespace DecisionSharp.Core.Answers;

using System.Collections.ObjectModel;

/// <summary>
/// Provides methods for validating decision-related answers, including probabilities and distributions.
/// </summary>
internal static class AnswerValidation
{
    /// <summary>
    /// Validates the specified probability value to ensure it is within the valid range of [0, 1].
    /// </summary>
    /// <param name="value">A double value representing the probability to validate. Must be finite and within the range of 0 to 1 inclusive.</param>
    /// <exception cref="ArgumentException">Thrown if the probability value is not finite or is outside the range of [0, 1].</exception>
    internal static void Probability(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentException("Probability/confidence must be finite and in [0,1].");
        }
    }

    /// <summary>
    /// Validates and ensures a proper probability distribution.
    /// The sum of all probabilities must equal 1 within a margin of 0.001,
    /// and each entry in the distribution must have a valid key and a probability between 0 and 1.
    /// </summary>
    /// <param name="values">A dictionary representing the probability distribution where each key is
    /// an event, and the value represents the probability of that event.</param>
    /// <returns>A readonly dictionary containing the validated probability distribution.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the dictionary is null.</exception>
    /// <exception cref="ArgumentException">Thrown when:
    /// The dictionary contains fewer than two entries,
    /// any key is null, empty or whitespace,
    /// any value is not a finite number between 0 and 1,
    /// or the sum of all probabilities does not equal 1 within the specified tolerance.</exception>
    internal static IReadOnlyDictionary<string, double> Distribution(IReadOnlyDictionary<string, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            throw new ArgumentException("Distribution needs at least two entries.");
        }

        var copy = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            Probability(pair.Value);
            copy.Add(pair.Key, pair.Value);
        }

        if (Math.Abs(copy.Values.Sum() - 1) > 0.001 + 1e-12)
        {
            throw new ArgumentException("Distribution must sum to one within 0.001.");
        }

        return new ReadOnlyDictionary<string, double>(copy);
    }
}