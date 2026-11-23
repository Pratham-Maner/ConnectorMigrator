using System.Text.Json;
using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Json;
using ConnectorMigrator.Core.Pipeline;
using V = ConnectorMigrator.Core.Mapping.ConnectorVocabulary;

namespace ConnectorMigrator.Core.Validation;

/// <summary>
/// Parses the input and checks it is an old-structure connector. Throws
/// <see cref="ConnectorValidationException"/> with a readable message when it is not.
/// </summary>
public static class InputValidator
{
    private static readonly (string Section, string Container)[] Sections =
    [
        (V.SsoInfo, V.SupportedSsoMethod),
        (V.LcmInfo, V.SupportedLcmMethod),
        (V.UagInfo, V.SupportedReconMethod),
        (V.VaultInfo, V.SupportedVaultingMethod),
    ];

    public static JsonObject ParseAndValidate(string text, Action<string> warn)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ConnectorValidationException("File is empty.");

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, documentOptions: JsonHelpers.ReadOptions);
        }
        catch (JsonException ex)
        {
            var where = ex.LineNumber is { } line ? $" at line {line + 1}, position {(ex.BytePositionInLine ?? 0) + 1}" : "";
            throw new ConnectorValidationException($"Invalid JSON{where}: {FirstSentence(ex.Message)}");
        }

        if (node is not JsonObject root)
            throw new ConnectorValidationException($"The JSON root must be an object, but it is {(node is null ? "null" : node.GetValueKind().ToString().ToLowerInvariant())}.");

        EnsureNoDuplicateKeys(root, "$");

        if (!JsonHelpers.TryGetProperty(root, V.Metadata, out _, out var meta) || meta is not JsonObject metaObj)
            throw new ConnectorValidationException($"Missing or invalid '{V.Metadata}' object; this does not look like a connector file.");

        RejectIfAlreadyConverted(root, metaObj);

        var present = 0;
        foreach (var (section, container) in Sections)
        {
            if (!JsonHelpers.TryGetProperty(root, section, out var key, out var value))
            {
                warn($"Section '{section}' is not present; its metadata was not generated.");
                continue;
            }
            if (value is not JsonObject sectionObj)
                throw new ConnectorValidationException($"'{key}' must be an object.");
            present++;

            if (!JsonHelpers.TryGetProperty(sectionObj, container, out var cKey, out var cValue))
                warn($"'{key}' has no '{container}'; it is treated as not configured.");
            else if (cValue is not JsonObject)
                throw new ConnectorValidationException($"'{key}.{cKey}' must be an object.");
        }
        if (present == 0)
            throw new ConnectorValidationException($"None of {string.Join(", ", Sections.Select(s => s.Section))} were found; this does not look like a connector file.");

        return root;
    }

    private static void RejectIfAlreadyConverted(JsonObject root, JsonObject meta)
    {
        var markers = new List<string>();
        if (JsonHelpers.HasProperty(root, V.FormBuilderInfo)) markers.Add(V.FormBuilderInfo);
        if (JsonHelpers.HasProperty(meta, V.ConnectorId)) markers.Add($"{V.Metadata}.{V.ConnectorId}");
        if (JsonHelpers.HasProperty(meta, V.Capabilities)) markers.Add($"{V.Metadata}.{V.Capabilities}");
        foreach (var (section, _) in Sections.Take(3))
            if (JsonHelpers.GetObject(JsonHelpers.GetObject(root, section), V.Metadata) is not null)
                markers.Add($"{section}.{V.Metadata}");

        if (markers.Count > 0)
            throw new ConnectorValidationException(
                $"File already appears to be in the new connector structure (found {string.Join(", ", markers)}).");
    }

    /// <summary>
    /// JsonNode parses lazily and only fails on a duplicate property when the object is first read.
    /// Walk the whole tree once so the error is reported with a path, not mid-conversion.
    /// </summary>
    private static void EnsureNoDuplicateKeys(JsonNode? node, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                List<KeyValuePair<string, JsonNode?>> props;
                try { props = obj.ToList(); }
                catch (ArgumentException ex)
                {
                    throw new ConnectorValidationException($"Duplicate property name in object at {path}: {FirstSentence(ex.Message)}");
                }
                foreach (var (k, v) in props) EnsureNoDuplicateKeys(v, $"{path}.{k}");
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++) EnsureNoDuplicateKeys(arr[i], $"{path}[{i}]");
                break;
        }
    }

    private static string FirstSentence(string message)
    {
        var cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        if (cut < 0) cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return (cut > 0 ? message[..cut] : message).Trim();
    }
}
