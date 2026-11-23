using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Mapping;

namespace ConnectorMigrator.Core.Inspection;

/// <summary>Answers "is this part of the connector actually configured?".</summary>
public static class StepInspector
{
    /// <summary>
    /// True if a "steps"/"Steps" property anywhere below <paramref name="node"/> is non-empty.
    /// <list type="bullet">
    /// <item>LCM/UAG/Vault: <c>"steps": [ ... ]</c> must contain at least one step.</item>
    /// <item>SSO browser bots: <c>"Steps": { "URL": ..., "SeleniumData": [ ... ] }</c> must contain
    /// at least one non-empty array (e.g. SeleniumData), so a template with only a URL does not count.</item>
    /// </list>
    /// FormBuilder lists are skipped: they describe form fields, not automation steps.
    /// </summary>
    public static bool HasNonEmptySteps(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (ConnectorVocabulary.IsFormBuilderListKey(key)) continue;
                    if (string.Equals(key, ConnectorVocabulary.Steps, StringComparison.OrdinalIgnoreCase)
                        && StepsCollectionIsNonEmpty(value))
                        return true;
                    if (HasNonEmptySteps(value)) return true;
                }
                return false;
            case JsonArray arr:
                return arr.Any(HasNonEmptySteps);
            default:
                return false;
        }
    }

    private static bool StepsCollectionIsNonEmpty(JsonNode? steps) => steps switch
    {
        JsonArray a => a.Count > 0,
        JsonObject o => o.Any(p => p.Value is JsonArray a && a.Count > 0),
        _ => false,
    };
}
