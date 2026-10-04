namespace DecisionSharp.Core.Answers;

/// <summary>
/// Represents an answer to a choice-based decision question, including the selected choice,
/// the associated probability distribution, and the confidence level in the choice.
/// </summary>
public sealed class ChoiceAnswer : DecisionAnswer
{
    /// <summary>
    /// Represents a specific choice made as an answer to a decision question.
    /// </summary>
    public string Choice { get; }

    /// <summary>
    /// Represents the probability distribution associated with a choice in a decision-making context.
    /// This property provides a mapping between possible choices and their respective probabilities,
    /// enabling the analysis of the likelihood of each choice in the context of the decision made.
    /// </summary>
    /// <remarks>
    /// The keys in the probability distribution represent the possible choices,
    /// while the values are the corresponding probabilities, which are expected to sum to 1.
    /// If the selected choice of the decision is not present in the distribution,
    /// an exception is raised during initialization.
    /// </remarks>
    public IReadOnlyDictionary<string, double> Probabilities { get; }

    /// <summary>
    /// Gets the confidence level of the selected choice within the distribution of probabilities.
    /// </summary>
    /// <remarks>
    /// Confidence represents the degree of certainty that the provided choice is the most appropriate selection,
    /// as indicated by the input distribution.
    /// </remarks>
    public double Confidence { get; }

    /// <summary>
    /// Represents an answer to a decision problem where one of multiple choices is selected.
    /// </summary>
    public ChoiceAnswer(string choice, IReadOnlyDictionary<string, double> probabilities, double confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(choice);
        AnswerValidation.Probability(confidence);
        this.Probabilities = AnswerValidation.Distribution(probabilities);
        if (!this.Probabilities.ContainsKey(choice))
        {
            throw new ArgumentException("Choice is absent from its distribution.", nameof(choice));
        }

        this.Choice = choice;
        this.Confidence = confidence;
    }
}