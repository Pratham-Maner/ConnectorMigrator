using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Json;
using ConnectorMigrator.Core.Pipeline;
using V = ConnectorMigrator.Core.Mapping.ConnectorVocabulary;

namespace ConnectorMigrator.Core.Transformers;

/// <summary>
/// Moves every *FormBuilderData list out of LCMInfo and UAGInfo into one top-level FormBuilderInfo array.
/// <list type="bullet">
/// <item>Entries are de-duplicated by KeyName (first occurrence wins, in document order, so
/// Provisioning's version is kept). Conflicting duplicates produce a warning.</item>
/// <item>Each entry is otherwise unchanged; two attributes are appended:
/// UseItforCapability (LCMInfo / UAGInfo) and ForSupportedBotMethod (the bot method the list was
/// found under, e.g. API or SDK), each the union over all places the field appeared.</item>
/// <item>Other sections (e.g. SOT.SOTFormBuilderData) are not touched.</item>
/// </list>
/// </summary>
public sealed class FormBuilderRelocator : IConnectorTransformer
{
    public string Name => "FormBuilderInfo relocation";

    private static readonly (string Section, string Container)[] Sources =
    [
        (V.LcmInfo, V.SupportedLcmMethod),
        (V.UagInfo, V.SupportedReconMethod),
    ];

    private sealed record FoundList(JsonObject Parent, string Key, JsonNode? List, string Capability, string BotMethod, string Path);

    private sealed class MergedField(JsonObject entry, string firstPath)
    {
        public JsonObject Entry { get; } = entry;
        public string FirstPath { get; } = firstPath;
        public List<string> Capabilities { get; } = [];
        public List<string> BotMethods { get; } = [];
    }

    public void Apply(ConversionContext ctx)
    {
        var lists = new List<FoundList>();
        foreach (var (section, container) in Sources)
        {
            var methods = JsonHelpers.GetObject(JsonHelpers.GetObject(ctx.Root, section), container);
            if (methods is null) continue;
            foreach (var (methodKey, methodNode) in methods)
                Collect(methodNode, section, methodKey, $"{section}.{container}.{methodKey}", lists);
        }

        var merged = new List<MergedField>();
        var byKey = new Dictionary<string, MergedField>(StringComparer.Ordinal);

        foreach (var found in lists)
        {
            if (found.List is not JsonArray arr)
            {
                ctx.Warn($"{found.Path}.{found.Key} is not a list; it was removed without relocation.");
                continue;
            }
            for (var i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject entry)
                {
                    ctx.Warn($"{found.Path}.{found.Key}[{i}] is not an object; it was dropped.");
                    continue;
                }

                var identity = IdentityOf(entry);
                MergedField field;
                if (identity is not null && byKey.TryGetValue(identity, out var existing))
                {
                    field = existing;
                    var differences = Differences(existing.Entry, entry);
                    if (differences.Count > 0)
                        ctx.Warn($"FormBuilder field '{identity}' differs between {existing.FirstPath} and {found.Path}.{found.Key} " +
                                 $"({string.Join(", ", differences)}); kept the first version.");
                }
                else
                {
                    field = new MergedField((JsonObject)entry.DeepClone(), $"{found.Path}.{found.Key}");
                    merged.Add(field);
                    if (identity is not null) byKey[identity] = field;
                    else ctx.Warn($"{found.Path}.{found.Key}[{i}] has no KeyName or APIValueString; it was kept without de-duplication.");
                }

                AddOnce(field.Capabilities, found.Capability);
                AddOnce(field.BotMethods, found.BotMethod);
            }
        }

        foreach (var found in lists) found.Parent.Remove(found.Key);

        var output = new JsonArray();
        foreach (var field in merged)
        {
            field.Entry[V.UseItForCapability] = JsonHelpers.ToJsonArray(
                Sources.Select(s => s.Section).Where(field.Capabilities.Contains));
            field.Entry[V.ForSupportedBotMethod] = JsonHelpers.ToJsonArray(field.BotMethods);
            output.Add(field.Entry);
        }

        if (JsonHelpers.HasProperty(ctx.Root, V.FormBuilderInfo))
            throw new ConnectorValidationException($"{V.FormBuilderInfo} already exists."); // validator should have caught this
        ctx.Root[V.FormBuilderInfo] = output;

        ctx.Summary.FormBuilderListsRelocated = lists.Count;
        ctx.Summary.FormBuilderFieldsWritten = output.Count;
    }

    private static void Collect(JsonNode? node, string capability, string botMethod, string path, List<FoundList> into)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (V.IsFormBuilderListKey(key))
                        into.Add(new FoundList(obj, key, value, capability, botMethod, path));
                    else
                        Collect(value, capability, botMethod, $"{path}.{key}", into);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++) Collect(arr[i], capability, botMethod, $"{path}[{i}]", into);
                break;
        }
    }

    private static string? IdentityOf(JsonObject entry)
    {
        var keyName = JsonHelpers.GetString(entry, "KeyName");
        if (!string.IsNullOrWhiteSpace(keyName)) return keyName;
        var apiValue = JsonHelpers.GetString(entry, "APIValueString");
        return string.IsNullOrWhiteSpace(apiValue) ? null : apiValue;
    }

    private static List<string> Differences(JsonObject a, JsonObject b)
    {
        var diffs = new List<string>();
        foreach (var key in a.Select(p => p.Key).Union(b.Select(p => p.Key)))
        {
            if (key is V.UseItForCapability or V.ForSupportedBotMethod) continue;
            a.TryGetPropertyValue(key, out var va);
            b.TryGetPropertyValue(key, out var vb);
            if (!JsonNode.DeepEquals(va, vb)) diffs.Add(key);
        }
        return diffs;
    }

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value)) list.Add(value);
    }
}
