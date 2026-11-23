using System.Text;
using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Inspection;
using ConnectorMigrator.Core.Json;
using ConnectorMigrator.Core.Pipeline;
using V = ConnectorMigrator.Core.Mapping.ConnectorVocabulary;

namespace ConnectorMigrator.Core.Transformers;

/// <summary>General Metadata: adds connectorId and capabilities.</summary>
public sealed class ConnectorMetadataTransformer : IConnectorTransformer
{
    public string Name => "Connector metadata";

    public void Apply(ConversionContext ctx)
    {
        var meta = JsonHelpers.GetObject(ctx.Root, V.Metadata)!; // guaranteed by InputValidator

        var id = ctx.IdRegistry.Reserve(BuildId(ctx, meta));
        var capabilities = SectionDetector.Capabilities(ctx.Root, ctx.Warn);
        if (capabilities.Count == 0)
            ctx.Warn("No configured steps were found in any section; capabilities is empty.");

        // Placed just before EntityNames (as in the reference JSON), otherwise appended.
        JsonHelpers.SetBefore(meta, V.EntityNameKeys, V.ConnectorId, JsonValue.Create(id));
        JsonHelpers.SetBefore(meta, V.EntityNameKeys, V.Capabilities, JsonHelpers.ToJsonArray(capabilities));

        ctx.Summary.ConnectorId = id;
        ctx.Summary.Capabilities = capabilities;
    }

    private static string BuildId(ConversionContext ctx, JsonObject meta)
    {
        if (ctx.Options.IdMode == ConnectorIdMode.Guid)
            return Guid.NewGuid().ToString();

        var name = JsonHelpers.GetString(meta, V.AssetName);
        var slug = Slugify(name);
        if (slug.Length > 0) return slug;

        var fallback = Slugify(Path.GetFileNameWithoutExtension(ctx.SourceName));
        ctx.Warn($"Metadata.{V.AssetName} is empty; {V.ConnectorId} was derived from the file name.");
        return fallback.Length > 0 ? fallback : Guid.NewGuid().ToString();
    }

    /// <summary>"Microsoft O365" -> "microsoft-o365"; "AWS v10.11" -> "aws-v10-11".</summary>
    public static string Slugify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text.Length);
        var pendingDash = false;
        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                if (pendingDash && sb.Length > 0) sb.Append('-');
                sb.Append(ch);
                pendingDash = false;
            }
            else pendingDash = true;
        }
        return sb.ToString();
    }
}
