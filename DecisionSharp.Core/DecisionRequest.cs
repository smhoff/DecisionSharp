using System.Collections.ObjectModel;
using System.Text.Json;
namespace DecisionSharp.Core;
public sealed class DecisionRequest
{
    public JsonElement State { get; }
    public IReadOnlyDictionary<string,DecisionQuestion> Questions { get; }
    public string? Model { get; }
    public DecisionRequest(JsonElement state, IReadOnlyDictionary<string,DecisionQuestion> questions, string? model = null)
    {
        State = InputJson.Own(state, nameof(state));
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0) throw new ArgumentException("At least one question is required.", nameof(questions));
        var copy = new Dictionary<string,DecisionQuestion>(StringComparer.Ordinal);
        foreach (var pair in questions) { ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key); ArgumentNullException.ThrowIfNull(pair.Value); copy.Add(pair.Key, pair.Value); }
        Questions = new ReadOnlyDictionary<string,DecisionQuestion>(copy);
        if (model is not null) ArgumentException.ThrowIfNullOrWhiteSpace(model);
        Model = model;
    }
}
internal static class InputJson
{
    internal static JsonElement Text(string value) { ArgumentException.ThrowIfNullOrWhiteSpace(value); return JsonSerializer.SerializeToElement(value); }
    internal static JsonElement Own(JsonElement value, string name)
    {
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array)) throw new ArgumentException("Expected string, object or array.",name);
        if (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())) throw new ArgumentException("Text cannot be blank.",name);
        CheckDuplicates(value,name);
        return value.Clone();
    }
    private static void CheckDuplicates(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { if (!keys.Add(property.Name)) throw new ArgumentException("Duplicate JSON property.", name); CheckDuplicates(property.Value,name); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckDuplicates(item,name);
    }
}
