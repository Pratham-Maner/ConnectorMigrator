using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Json;
using ConnectorMigrator.Core.Transformers;
using ConnectorMigrator.Core.Validation;

namespace ConnectorMigrator.Core.Pipeline;

public sealed record ConversionOutcome(string OutputJson, JsonObject Output, ConversionSummary Summary, IReadOnlyList<string> Warnings);

/// <summary>Validates one old-structure connector and runs each section transformer over it.</summary>
public sealed class ConversionPipeline
{
    private readonly IReadOnlyList<IConnectorTransformer> _transformers;

    public ConversionPipeline(IEnumerable<IConnectorTransformer>? transformers = null)
    {
        _transformers = transformers?.ToList() ?? DefaultTransformers();
    }

    /// <summary>
    /// Metadata transformers only read steps, and the relocator only moves FormBuilder lists
    /// (which the step detection ignores), so the order affects output layout, not results.
    /// </summary>
    public static IReadOnlyList<IConnectorTransformer> DefaultTransformers() =>
    [
        new ConnectorMetadataTransformer(),
        new SsoMetadataTransformer(),
        new LcmMetadataTransformer(),
        new UagMetadataTransformer(),
        new FormBuilderRelocator(),
    ];

    //hands the tree to each transformer: connector metadata, SSO metadata, LCM metadata, UAG metadata, then the FormBuilder relocator.
    public ConversionOutcome Convert(string inputJson, string sourceName, ConversionOptions? options = null, ConnectorIdRegistry? ids = null)
    {
        var validationWarnings = new List<string>();
        var root = InputValidator.ParseAndValidate(inputJson, validationWarnings.Add);

        var ctx = new ConversionContext(root, sourceName, options ?? new ConversionOptions(), ids ?? new ConnectorIdRegistry());
        foreach (var w in validationWarnings) ctx.Warn(w);

        foreach (var transformer in _transformers)
        {
            try
            {
                transformer.Apply(ctx);
            }
            catch (ConnectorValidationException) { throw; }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
            {
                throw new ConnectorValidationException($"{transformer.Name} failed: {ex.Message}");
            }
        }

        return new ConversionOutcome(root.ToJsonString(JsonHelpers.WriteOptions), root, ctx.Summary, ctx.Warnings);
    }
}
