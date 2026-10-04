namespace DecisionSharp.Core;

using System.Text.Json;

/// <summary>
/// Provides utility methods for working with JSON elements, specifically for input validation and
/// reformatting of JSON string, object, or array data.
/// </summary>
internal static class InputJson
{
    /// <summary>
    /// Serializes the provided string value into a JSON element.
    /// </summary>
    /// <param name="value">The string value to serialize. Cannot be null, empty, or whitespace.</param>
    /// <returns>A JSON element representing the serialized string value.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is null, empty, or consists of only whitespace.</exception>
    internal static JsonElement Text(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return JsonSerializer.SerializeToElement(value);
    }

    /// <summary>
    /// Validates and clones a JSON element, ensuring it meets the specified criteria.
    /// </summary>
    /// <param name="value">
    /// The JSON element to validate. Must be of type string, object, or array.
    /// If the JSON element is a string, it must not be null, empty, or consist only of whitespace.
    /// </param>
    /// <param name="name">
    /// The name of the parameter being validated, used for exception messages.
    /// </param>
    /// <returns>
    /// A cloned instance of the validated JSON element.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the JSON element is not of type string, object, or array,
    /// or if a string element is null, empty, or consists only of whitespace.
    /// </exception>
    internal static JsonElement Own(JsonElement value, string name)
    {
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
        {
            throw new ArgumentException("Expected string, object or array.", name);
        }

        if (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException("Text cannot be blank.", name);
        }

        CheckDuplicates(value, name);
        return value.Clone();
    }

    /// <summary>
    /// Recursively checks for duplicate keys in a JSON object or array.
    /// Throws an <see cref="ArgumentException"/> if any duplicate keys are found.
    /// </summary>
    /// <param name="value">The JSON element to inspect. It must be an object or array.</param>
    /// <param name="name">The name of the parameter being validated, used in exception messages.</param>
    /// <exception cref="ArgumentException">
    /// Thrown if duplicate property names are found in the JSON object, or
    /// if the provided JSON value does not conform to the expected format.
    /// </exception>
    private static void CheckDuplicates(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                {
                    throw new ArgumentException("Duplicate JSON property.", name);
                }

                CheckDuplicates(property.Value, name);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                CheckDuplicates(item, name);
            }
        }
    }
}