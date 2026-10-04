namespace DecisionSharp.Core.Questions;

using System.Collections.ObjectModel;
using System.Text.Json;

/// <summary>
/// Represents a decision-making question where the user selects one option from a set of predefined choices.
/// </summary>
/// <remarks>
/// The <see cref="ChoiceQuestion"/> class is a specialized question type that inherits from
/// <see cref="DecisionQuestion"/>. It enforces constraints for the number of choices and validates input
/// during initialization. It is typically used in scenarios where a user or system must choose from
/// multiple possible options.
/// </remarks>
/// 
/// <example>
/// A <see cref="ChoiceQuestion"/> can have between 2 and 255 options, inclusive. Each option is defined
/// by a unique key with an optional JSON-based value to provide additional metadata or context.
/// </example>
/// 
/// <exception cref="ArgumentNullException">
/// Thrown if the criteria or its keys are null.
/// </exception>
/// <exception cref="ArgumentException">
/// Thrown if the number of options provided in the <paramref name="criteria"/> is outside the allowed range
/// of 2 to 255 or if an option key is null or whitespace.
/// </exception>
/// 
/// <seealso cref="DecisionQuestion"/>
public sealed class ChoiceQuestion : DecisionQuestion
{
    /// <summary>
    /// Represents the collection of criteria associated with a <see cref="ChoiceQuestion"/>.
    /// Each criterion is defined as a key-value pair, where the key is a case-sensitive string
    /// and the value is an optional <see cref="JsonElement"/> providing further details about
    /// the criterion. The criteria must adhere to predefined constraints on their count.
    /// </summary>
    /// <remarks>
    /// The <see cref="Criteria"/> property is immutable and constructed during the initialization
    /// of the <see cref="ChoiceQuestion"/>. The dictionary is case-sensitive and ensures a minimum
    /// of 2 and a maximum of 255 criteria. Modifications are not permitted after initialization.
    /// This property is commonly serialized for output and deserialized for input in decision-making workflows.
    /// </remarks>
    public IReadOnlyDictionary<string, JsonElement?> Criteria { get; }

    /// <summary>
    /// Represents a decision-making question where the responder is required
    /// to choose from a set of predefined options.
    /// </summary>
    /// <remarks>
    /// This class extends the abstract <see cref="DecisionQuestion"/> class and is designed to handle
    /// questions requiring a selection based on criteria. Each option in the selection is paired
    /// with specific criteria, which can be represented as JSON elements.
    /// </remarks>
    public ChoiceQuestion(string instructions, IReadOnlyDictionary<string, string?> criteria) : this(
        InputJson.Text(instructions), ToJson(criteria))
    {
    }

    /// <summary>
    /// Converts a dictionary of string keys and nullable string values into a dictionary of string keys
    /// and nullable JSON elements, where each string value is serialized into a JSON element.
    /// </summary>
    /// <param name="criteria">
    /// A dictionary containing string keys and nullable string values that represent the input data to be
    /// serialized. The keys must be unique, and the values can be either null or strings.
    /// </param>
    /// <returns>
    /// A dictionary where the keys are the same as the input dictionary, and the values are
    /// nullable JSON elements derived from serializing the corresponding input string values. If a value in
    /// the input dictionary is null, the corresponding value in the output dictionary will also be null.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if the <paramref name="criteria"/> parameter is null.
    /// </exception>
    private static Dictionary<string, JsonElement?> ToJson(IReadOnlyDictionary<string, string?> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return criteria.ToDictionary(p => p.Key, p => p.Value is null ? (JsonElement?)null : InputJson.Text(p.Value),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Represents a decision-based question where the user must choose from a set of options.
    /// Each option is defined by a key and an optional additional JSON-based criterion value.
    /// </summary>
    /// <remarks>
    /// This class enforces the following constraints on the set of options (criteria):
    /// - A minimum of 2 and a maximum of 255 options must be provided.
    /// - Option keys must be non-null, non-whitespace, and unique.
    /// - Optional JSON-based criteria values must be valid, or null for options without additional data.
    /// </remarks>
    /// <example>
    /// This question type is typically used in situations where a user needs to pick one option from
    /// multiple possibilities, with each option potentially carrying optional metadata.
    /// </example>
    public ChoiceQuestion(JsonElement instructions, IReadOnlyDictionary<string, JsonElement?> criteria) :
        base(instructions)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count is < 2 or > 255)
        {
            throw new ArgumentException("Choice requires 2–255 options.", nameof(criteria));
        }

        var copy = new Dictionary<string, JsonElement?>(StringComparer.Ordinal);
        foreach (var pair in criteria)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            copy.Add(pair.Key,
                pair.Value is { ValueKind: not JsonValueKind.Null } value ? InputJson.Own(value, nameof(criteria))
                    : null);
        }

        this.Criteria = new ReadOnlyDictionary<string, JsonElement?>(copy);
    }
}