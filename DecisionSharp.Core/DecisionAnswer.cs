namespace DecisionSharp.Core;

/// <summary>
/// Represents a base class for different types of decision answers.
/// </summary>
/// <remarks>
/// The <c>DecisionAnswer</c> class provides an abstraction for modeling various kinds of decision outcomes.
/// Derived classes, such as <c>YesNoAnswer</c>, <c>ChoiceAnswer</c>, and <c>ScoreAnswer</c>,
/// specialize the behavior and properties associated with specific types of answers.
/// </remarks>
public abstract class DecisionAnswer
{
    /// <summary>
    /// Represents the base class for all decision answers.
    /// This abstract class is intended to be inherited by specific types of answers
    /// such as yes/no, choice-based, or scored answers.
    /// </summary>
    private protected DecisionAnswer()
    {
    }
}