using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConnectorMigrator.Core.Json;

/// <summary>Small helpers over System.Text.Json.Nodes that keep property order intact.</summary>
public static class JsonHelpers
{
    public static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Keep values such as "<username>", "[[x]]" and "&" readable instead of < escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Finds a property by name, case-insensitively. Returns the real key as stored.</summary>
    public static bool TryGetProperty(JsonObject obj, string name, out string actualKey, out JsonNode? value)
    {
        foreach (var (k, v) in obj)
        {
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
            {
                actualKey = k;
                value = v;
                return true;
            }
        }
        actualKey = name;
        value = null;
        return false;
    }

    public static JsonObject? GetObject(JsonObject? obj, string name) =>
        obj is not null && TryGetProperty(obj, name, out _, out var v) ? v as JsonObject : null;

    public static bool HasProperty(JsonObject obj, string name) => TryGetProperty(obj, name, out _, out _);

    public static string? GetString(JsonObject? obj, string name) =>
        obj is not null && TryGetProperty(obj, name, out _, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s)
            ? s
            : null;

    public static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    /// <summary>Puts <paramref name="key"/> first in the object, replacing any existing property of that name.</summary>
    public static void SetFirst(JsonObject obj, string key, JsonNode value) =>
        Rebuild(obj, props =>
        {
            props.RemoveAll(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            props.Insert(0, new(key, value));
        });

    /// <summary>
    /// Inserts (or replaces) <paramref name="key"/> immediately before the first existing key in
    /// <paramref name="beforeKeys"/>; appends at the end when none of them exist.
    /// </summary>
    public static void SetBefore(JsonObject obj, IEnumerable<string> beforeKeys, string key, JsonNode value) =>
        Rebuild(obj, props =>
        {
            props.RemoveAll(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            var anchors = beforeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var idx = props.FindIndex(p => anchors.Contains(p.Key));
            props.Insert(idx < 0 ? props.Count : idx, new(key, value));
        });

    // .NET 8's JsonObject has no Insert(index, ...), so detach everything and re-add in order.
    private static void Rebuild(JsonObject obj, Action<List<KeyValuePair<string, JsonNode?>>> mutate)
    {
        var props = obj.ToList();
        foreach (var p in props) obj.Remove(p.Key);
        mutate(props);
        foreach (var p in props) obj.Add(p.Key, p.Value);
    }

    /// <summary>True if the node holds any non-blank string, number or boolean anywhere inside it.</summary>
    public static bool HasMeaningfulContent(JsonNode? node) => node switch
    {
        JsonObject o => o.Any(p => HasMeaningfulContent(p.Value)),
        JsonArray a => a.Any(HasMeaningfulContent),
        JsonValue v when v.TryGetValue<string>(out var s) => !string.IsNullOrWhiteSpace(s),
        JsonValue => true,
        _ => false,
    };
}
