using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Pipeline;

namespace ConnectorMigrator.Tests;

internal static class TestSupport
{
    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    public static string Fixture(string name) => File.ReadAllText(FixturePath(name));

    public static ConversionOutcome Convert(string json, string source = "test.json", ConversionOptions? options = null, ConnectorIdRegistry? ids = null) =>
        new ConversionPipeline().Convert(json, source, options, ids);

    public static string[] Strings(JsonNode? node) =>
        node!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

    public static JsonNode? At(JsonNode? node, params string[] path)
    {
        foreach (var p in path) node = node is JsonArray a ? a[int.Parse(p)] : node?[p];
        return node;
    }

    /// <summary>A step list with one step.</summary>
    public static JsonArray OneStep() => new(new JsonObject { ["Step_Count"] = "1", ["Action"] = "APICall" });

    public static JsonObject FormField(string key, string value = "") => new()
    {
        ["Type"] = "TextBox",
        ["Value"] = value,
        ["KeyName"] = key,
        ["APIValueString"] = $"[[{key}]]",
    };

    /// <summary>Minimal old-structure connector; tests mutate it to cover specific rules.</summary>
    public static JsonObject MinimalConnector(string assetName = "Test App") => new()
    {
        ["Metadata"] = new JsonObject
        {
            ["AssetName"] = assetName,
            ["AssetURL"] = "https://app.example.com/",
            ["EntityNames"] = new JsonArray("Groups"),
        },
        ["SSOInfo"] = new JsonObject
        {
            ["SupportedSSOMethod"] = new JsonObject
            {
                ["ArconBOT"] = new JsonObject(),
                ["SAML"] = new JsonObject(),
                ["OIDC"] = new JsonObject(),
                ["OAuth"] = new JsonObject(),
                ["CLI"] = new JsonObject(),
            },
        },
        ["LCMInfo"] = new JsonObject
        {
            ["SupportedLCMMethod"] = new JsonObject { ["ArconBOT"] = new JsonObject(), ["API"] = new JsonObject(), ["SDK"] = new JsonObject() },
        },
        ["UAGInfo"] = new JsonObject
        {
            ["SupportedReconMethod"] = new JsonObject { ["API"] = new JsonObject() },
        },
        ["VaultInfo"] = new JsonObject
        {
            ["SupportedVaultingMethod"] = new JsonObject { ["API"] = new JsonObject() },
        },
    };

    /// <summary>Removes the parts the converter adds/moves, so the remainder can be compared with the input.</summary>
    public static JsonNode? StripMigratedParts(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o
            .Where(p => !p.Key.EndsWith("FormBuilderData", StringComparison.Ordinal)
                        && p.Key is not ("FormBuilderInfo" or "connectorId" or "capabilities")
                        && !(p.Key == "Metadata" && p.Value is JsonObject m && m.ContainsKey("MethodUsed") | m.ContainsKey("SSOOptions")))
            .Select(p => KeyValuePair.Create(p.Key, StripMigratedParts(p.Value)))),
        JsonArray a => new JsonArray(a.Select(StripMigratedParts).ToArray()),
        _ => node?.DeepClone(),
    };
}
