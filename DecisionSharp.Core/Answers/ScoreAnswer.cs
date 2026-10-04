namespace DecisionSharp.Core.Answers;

using System.Collections.ObjectModel;

/// <summary>
/// Represents a scored decision answer with a numeric score, associated legend,
/// probability distribution, and confidence level.
/// </summary>
public sealed class ScoreAnswer : DecisionAnswer
{
    /// <summary>
    /// Represents a numeric score associated with a decision result, which is constrained to a range of values
    /// based on the defined number of levels in the associated legend.
    /// </summary>
    /// <remarks>
    /// The score value is a non-negative finite number that falls between 0 and the maximum level count
    /// (number of entries in the legend) minus 1. The score quantifies an outcome within the context
    /// of categorized levels represented in the legend.
    /// </remarks>
    public double Score { get; }

    /// <summary>
    /// Represents a mapping of numeric levels to their corresponding string descriptions
    /// within a scoring system. Each numeric key is a string representation of a score level,
    /// and each associated value provides a descriptive label for that level.
    /// </summary>
    /// <remarks>
    /// The keys in the legend must be numeric strings corresponding to valid score levels,
    /// starting from "0". The count of legend items must also match the exact range of levels
    /// expected by the scoring system. This property is immutable and ensures that
    /// every key-value relationship is complete and consistent with the probabilities
    /// and score range provided.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Legend { get; }

    /// <summary>
    /// Represents the probability distribution of possible outcomes for a decision or score.
    /// </summary>
    /// <remarks>
    /// This property provides a read-only dictionary where each key represents a possible outcome,
    /// and its corresponding value specifies the probability of that outcome.
    /// The sum of all probabilities in the dictionary is required to be approximately 1,
    /// ensuring the validity of the distribution.
    /// </remarks>
    public IReadOnlyDictionary<string, double> Probabilities { get; }

    /// <summary>
    /// Represents the confidence level associated with a decision or outcome.
    /// Confidence is a probability value between 0 and 1 that indicates the degree of certainty
    /// regarding the correctness or likelihood of a specific choice, score, or result.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    /// Represents a score-based decision answer.
    /// This class models an evaluative decision where a numerical score is associated with
    /// a set of predefined levels (legend) and their corresponding probabilities.
    /// </summary>
    public ScoreAnswer(double score, IReadOnlyDictionary<string, string> legend,
        IReadOnlyDictionary<string, double> probabilities, double confidence)
    {
        ArgumentNullException.ThrowIfNull(legend);
        AnswerValidation.Probability(confidence);
        this.Probabilities = AnswerValidation.Distribution(probabilities);
        if (legend.Count is < 2 or > 10 || !double.IsFinite(score) || score < 0 || score > legend.Count - 1)
        {
            throw new ArgumentException("Invalid score or level count.");
        }

        for (int i = 0; i < legend.Count; i++)
        {
            var key = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!legend.ContainsKey(key) || !this.Probabilities.ContainsKey(key) || legend[key] is null)
            {
                throw new ArgumentException("Incomplete numeric legend/distribution.");
            }
        }

        if (this.Probabilities.Count != legend.Count)
        {
            throw new ArgumentException("Legend/distribution mismatch.");
        }

        this.Legend = new ReadOnlyDictionary<string, string>(legend.ToDictionary(p => p.Key, p => p.Value,
            StringComparer.Ordinal));
        this.Score = score;
        this.Confidence = confidence;
    }
}