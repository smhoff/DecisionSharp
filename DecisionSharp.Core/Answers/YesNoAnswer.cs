namespace DecisionSharp.Core.Answers;

/// <summary>
/// Represents a decision answer specifically designed for "Yes" or "No" responses.
/// </summary>
/// <remarks>
/// This class encapsulates the probability of a "Yes" response as a double value.
/// It inherits from the abstract base class <c>DecisionAnswer</c>.
/// </remarks>
public sealed class YesNoAnswer : DecisionAnswer
{
    /// <summary>
    /// Represents the probability that the answer to a given decision question is "Yes".
    /// </summary>
    /// <remarks>
    /// The value of this property ranges from 0.0 to 1.0, where 0.0 indicates absolute certainty of "No"
    /// and 1.0 indicates absolute certainty of "Yes". Intermediate values represent varying levels of confidence.
    /// Any value provided to this property is validated to ensure it is within the acceptable range.
    /// </remarks>
    public double ProbabilityOfYes { get; }

    /// <summary>
    /// Represents a binary decision answer with a probability of "Yes".
    /// </summary>
    public YesNoAnswer(double probabilityOfYes)
    {
        AnswerValidation.Probability(probabilityOfYes);
        this.ProbabilityOfYes = probabilityOfYes;
    }
}