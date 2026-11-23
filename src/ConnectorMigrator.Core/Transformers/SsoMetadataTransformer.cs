using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Inspection;
using ConnectorMigrator.Core.Json;
using ConnectorMigrator.Core.Pipeline;
using V = ConnectorMigrator.Core.Mapping.ConnectorVocabulary;

namespace ConnectorMigrator.Core.Transformers;

/// <summary>SSOInfo.Metadata: ApplicationUrl (from Metadata.AssetURL) and SSOOptions.</summary>
public sealed class SsoMetadataTransformer : IConnectorTransformer
{
    public string Name => "SSOInfo metadata";

    public void Apply(ConversionContext ctx)
    {
        var sso = JsonHelpers.GetObject(ctx.Root, V.SsoInfo);
        if (sso is null) return; // section absent: InputValidator already warned

        var url = JsonHelpers.GetString(JsonHelpers.GetObject(ctx.Root, V.Metadata), V.AssetUrl) ?? "";
        if (url.Length == 0) ctx.Warn($"Metadata.{V.AssetUrl} is empty; SSOInfo.Metadata.{V.ApplicationUrl} will be empty.");

        var options = SectionDetector.SsoOptions(ctx.Root, ctx.Warn);

        JsonHelpers.SetFirst(sso, V.Metadata, new JsonObject
        {
            [V.ApplicationUrl] = url,
            [V.SsoOptions] = JsonHelpers.ToJsonArray(options),
        });
        ctx.Summary.SsoOptions = options;
    }
}
