using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Json;
using ConnectorMigrator.Core.Mapping;
using V = ConnectorMigrator.Core.Mapping.ConnectorVocabulary;

namespace ConnectorMigrator.Core.Inspection;

/// <summary>
/// Read-only detection rules. Each method inspects the old structure and returns what the
/// new metadata should contain; the transformers decide where to write it.
/// </summary>
public static class SectionDetector
{
    // ---------------------------------------------------------------- SSO

    /// <summary>
    /// SSO options from the configured blocks under SSOInfo.SupportedSSOMethod.
    /// ArconBOT gives RPA when it holds non-empty steps; CLI gives RPA when it holds non-empty steps
    /// or a configured launcher (SSH, App MobaXterm, ...); SAML/OIDC/OAuth count when they hold any value.
    /// </summary>
    public static IReadOnlyList<string> SsoOptions(JsonObject root, Action<string>? warn = null)
    {
        var methods = JsonHelpers.GetObject(JsonHelpers.GetObject(root, V.SsoInfo), V.SupportedSsoMethod);
        if (methods is null) return [];

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, block) in methods)
        {
            var rule = V.SsoOptionMap.FirstOrDefault(m => string.Equals(m.MethodKey, key, StringComparison.OrdinalIgnoreCase));
            if (rule.MethodKey is null)
            {
                if (JsonHelpers.HasMeaningfulContent(block))
                    warn?.Invoke($"SSOInfo.{V.SupportedSsoMethod}.{key} is configured but has no SSO option mapping; it was not added to {V.SsoOptions}.");
                continue;
            }
            var configured = rule.Detection switch
            {
                V.SsoDetection.Steps => StepInspector.HasNonEmptySteps(block),
                V.SsoDetection.StepsOrLauncher => StepInspector.HasNonEmptySteps(block) || HasConfiguredLauncher(block),
                _ => JsonHelpers.HasMeaningfulContent(block),
            };
            if (configured) found.Add(rule.Option);
        }
        return V.SsoOptionOrder.Where(found.Contains).ToList();
    }

    /// <summary>
    /// True if any child of a CLI block is a launcher entry (e.g. "SSH", "App MobaXterm") whose
    /// <c>Metadata</c> object holds a configured value. Empty <c>{}</c> launchers do not count.
    /// </summary>
    private static bool HasConfiguredLauncher(JsonNode? block) =>
        block is JsonObject cli && cli.Any(p =>
            p.Value is JsonObject launcher && JsonHelpers.HasMeaningfulContent(JsonHelpers.GetObject(launcher, V.Metadata)));

    // ---------------------------------------------------------------- Methods (LCM / UAG)

    /// <summary>
    /// Every method block under a SupportedXxxMethod container (API, SDK, LDAP, SSH, ArconBOT, ...)
    /// that holds non-empty steps, named as written in the file and in file order. Nothing is
    /// hard-coded, so a new method is reported as soon as it has steps.
    /// </summary>
    public static IReadOnlyList<string> MethodsUsed(JsonObject? supportedMethods)
    {
        if (supportedMethods is null) return [];
        var used = new List<string>();
        foreach (var (key, block) in supportedMethods)
        {
            if (StepInspector.HasNonEmptySteps(block) && !used.Contains(key, StringComparer.OrdinalIgnoreCase))
                used.Add(key);
        }
        return used;
    }

    public static JsonObject? LcmMethods(JsonObject root) =>
        JsonHelpers.GetObject(JsonHelpers.GetObject(root, V.LcmInfo), V.SupportedLcmMethod);

    public static JsonObject? ReconMethods(JsonObject root) =>
        JsonHelpers.GetObject(JsonHelpers.GetObject(root, V.UagInfo), V.SupportedReconMethod);

    public static JsonObject? VaultMethods(JsonObject root) =>
        JsonHelpers.GetObject(JsonHelpers.GetObject(root, V.VaultInfo), V.SupportedVaultingMethod);

    // ---------------------------------------------------------------- LCM features

    /// <summary>
    /// Features from every LCM operation container (ProvisioningInfo, DisableInfo, ...) that holds
    /// non-empty steps, across all bot methods. Unknown containers with steps are reported.
    /// </summary>
    public static IReadOnlyList<string> LcmFeatures(JsonObject root, Action<string>? warn = null)
    {
        var methods = LcmMethods(root);
        if (methods is null) return [];

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (methodKey, methodNode) in methods)
        {
            if (methodNode is not JsonObject method) continue;
            foreach (var (sectionKey, sectionNode) in method)
            {
                if (V.IsFormBuilderListKey(sectionKey)) continue;
                if (!StepInspector.HasNonEmptySteps(sectionNode)) continue;

                var normalized = V.NormalizeLcmSectionName(sectionKey);
                var rule = V.LcmFeatureMap.FirstOrDefault(m => m.NormalizedSection == normalized);
                if (rule.Feature is not null)
                    found.Add(rule.Feature);
                else if (!V.LcmNonOperationSections.Contains(normalized))
                    warn?.Invoke($"LCMInfo.{V.SupportedLcmMethod}.{methodKey}.{sectionKey} has steps but no feature mapping; it was not added to {V.Features}.");
            }
        }
        return V.LcmFeatureMap.Select(m => m.Feature).Where(found.Contains).ToList();
    }

    // ---------------------------------------------------------------- Discovery objects

    /// <summary>Discovery objects copied as-is from General Metadata EntityNames (or EntityName).</summary>
    public static IReadOnlyList<string> EntityNames(JsonObject root, Action<string>? warn = null)
    {
        var meta = JsonHelpers.GetObject(root, V.Metadata);
        if (meta is null) return [];
        foreach (var key in V.EntityNameKeys)
        {
            if (!JsonHelpers.TryGetProperty(meta, key, out var actual, out var node)) continue;
            switch (node)
            {
                case JsonArray arr:
                    var list = new List<string>();
                    foreach (var item in arr)
                    {
                        if (item is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
                        {
                            if (!list.Contains(s, StringComparer.Ordinal)) list.Add(s);
                        }
                        else warn?.Invoke($"Metadata.{actual} contains a non-text or empty entry; it was skipped.");
                    }
                    return list;
                case JsonValue v when v.TryGetValue<string>(out var single):
                    // Tolerate "EntityNames": "Groups,Roles"
                    return single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .Distinct(StringComparer.Ordinal).ToList();
                default:
                    warn?.Invoke($"Metadata.{actual} is not a list; {V.DiscoveryObjects} will be empty.");
                    return [];
            }
        }
        return [];
    }

    // ---------------------------------------------------------------- Capabilities

    public static IReadOnlyList<string> Capabilities(JsonObject root, Action<string>? warn = null)
    {
        var caps = new HashSet<string>(StringComparer.Ordinal);
        if (StepInspector.HasNonEmptySteps(ReconMethods(root))) caps.Add(V.CapDiscovery);
        if (StepInspector.HasNonEmptySteps(LcmMethods(root))) caps.Add(V.CapLifecycle);
        if (StepInspector.HasNonEmptySteps(VaultMethods(root))) caps.Add(V.CapVault);
        if (SsoOptions(root, warn).Count > 0) caps.Add(V.CapSso);
        return V.CapabilityOrder.Where(caps.Contains).ToList();
    }
}
