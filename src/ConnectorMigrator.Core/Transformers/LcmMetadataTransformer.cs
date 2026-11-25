using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Inspection;
using ConnectorMigrator.Core.Json;
using ConnectorMigrator.Core.Pipeline;
using V = ConnectorMigrator.Core.Mapping.ConnectorVocabulary;

namespace ConnectorMigrator.Core.Transformers;

/// <summary>LCMInfo.Metadata: MethodUsed (every method with steps) and features (CREATE, UPDATE, ...).</summary>
public sealed class LcmMetadataTransformer : IConnectorTransformer
{
    public string Name => "LCMInfo metadata";

    public void Apply(ConversionContext ctx)
    {
        var lcm = JsonHelpers.GetObject(ctx.Root, V.LcmInfo);
        if (lcm is null) return;

        var methods = SectionDetector.MethodsUsed(SectionDetector.LcmMethods(ctx.Root));
        var features = SectionDetector.LcmFeatures(ctx.Root, ctx.Warn);

        JsonHelpers.SetFirst(lcm, V.Metadata, new JsonObject
        {
            [V.MethodUsed] = JsonHelpers.ToJsonArray(methods),
            [V.Features] = JsonHelpers.ToJsonArray(features),
        });
        ctx.Summary.LcmMethodsUsed = methods;
        ctx.Summary.LcmFeatures = features;
    }
}
