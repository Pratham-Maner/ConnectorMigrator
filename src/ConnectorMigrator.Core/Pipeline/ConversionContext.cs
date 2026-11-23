using System.Text.Json.Nodes;

namespace ConnectorMigrator.Core.Pipeline;

public enum ConnectorIdMode
{
    /// <summary>Readable id from AssetName, e.g. "Microsoft O365" -> "microsoft-o365" (matches the reference JSON).</summary>
    Slug,
    /// <summary>Random GUID per conversion.</summary>
    Guid,
}

public sealed class ConversionOptions
{
    public ConnectorIdMode IdMode { get; init; } = ConnectorIdMode.Slug;
    /// <summary>Replace output files that already exist.</summary>
    public bool Overwrite { get; init; }
}

/// <summary>What the converter derived for one connector; used for reporting and tests.</summary>
public sealed class ConversionSummary
{
    public string? ConnectorId { get; set; }
    public IReadOnlyList<string> Capabilities { get; set; } = [];
    public IReadOnlyList<string> SsoOptions { get; set; } = [];
    public IReadOnlyList<string> LcmMethodsUsed { get; set; } = [];
    public IReadOnlyList<string> LcmFeatures { get; set; } = [];
    public IReadOnlyList<string> UagMethodsUsed { get; set; } = [];
    public IReadOnlyList<string> DiscoveryObjects { get; set; } = [];
    public int FormBuilderListsRelocated { get; set; }
    public int FormBuilderFieldsWritten { get; set; }
}

/// <summary>Hands out connector ids and guarantees uniqueness within one batch.</summary>
public sealed class ConnectorIdRegistry
{
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public string Reserve(string candidate)
    {
        var id = candidate;
        for (var n = 2; !_used.Add(id); n++) id = $"{candidate}-{n}";
        return id;
    }
}

/// <summary>State shared by the transformers while converting one connector.</summary>
public sealed class ConversionContext
{
    private readonly List<string> _warnings = [];

    public ConversionContext(JsonObject root, string sourceName, ConversionOptions options, ConnectorIdRegistry idRegistry)
    {
        Root = root;
        SourceName = sourceName;
        Options = options;
        IdRegistry = idRegistry;
    }

    public JsonObject Root { get; }
    public string SourceName { get; }
    public ConversionOptions Options { get; }
    public ConnectorIdRegistry IdRegistry { get; }
    public ConversionSummary Summary { get; } = new();
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Records a warning once (the same rule can be checked by more than one transformer).</summary>
    public void Warn(string message)
    {
        if (!_warnings.Contains(message)) _warnings.Add(message);
    }
}

public interface IConnectorTransformer
{
    string Name { get; }
    void Apply(ConversionContext context);
}

public sealed class ConnectorValidationException(string message) : Exception(message);
