using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Inspection;
using ConnectorMigrator.Core.Json;
using ConnectorMigrator.Core.Pipeline;
using V = ConnectorMigrator.Core.Mapping.ConnectorVocabulary;

namespace ConnectorMigrator.Core.Transformers;

/// <summary>UAGInfo.Metadata: MethodUsed (every method with steps) and discoveryObjects (from Metadata.EntityNames).</summary>
public sealed class UagMetadataTransformer : IConnectorTransformer
{
    public string Name => "UAGInfo metadata";

    public void Apply(ConversionContext ctx)
    {
        var uag = JsonHelpers.GetObject(ctx.Root, V.UagInfo);
        if (uag is null) return;

        var methods = SectionDetector.MethodsUsed(SectionDetector.ReconMethods(ctx.Root));
        var objects = SectionDetector.EntityNames(ctx.Root, ctx.Warn);
        if (objects.Count == 0 && methods.Count > 0)
            ctx.Warn($"Discovery is configured but Metadata.EntityNames is empty; {V.DiscoveryObjects} will be empty.");

        JsonHelpers.SetFirst(uag, V.Metadata, new JsonObject
        {
            [V.MethodUsed] = JsonHelpers.ToJsonArray(methods),
            [V.DiscoveryObjects] = JsonHelpers.ToJsonArray(objects),
        });
        ctx.Summary.UagMethodsUsed = methods;
        ctx.Summary.DiscoveryObjects = objects;
    }
}
