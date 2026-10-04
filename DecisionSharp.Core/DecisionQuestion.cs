using System.Text.Json;

namespace DecisionSharp.Core;

/// Represents a base class for decision questions.
/// Encapsulates common functionality and properties for different types of questions.
public abstract class DecisionQuestion
{
    /// Represents a base class for all decision questions.
    /// Holds generic properties and behaviors common to different types of decision questions.
    public JsonElement Instructions { get; }

    /// <summary>
    /// Represents an abstract base class for a question within a decision-making system.
    /// </summary>
    /// <remarks>
    /// This class provides a common structure for all types of decision questions,
    /// each of which includes specific instructions represented as a JSON element.
    /// Derived classes must implement their own criteria handling.
    /// </remarks>
    private protected DecisionQuestion(JsonElement instructions) =>
        Instructions = InputJson.Own(instructions, nameof(instructions));
}