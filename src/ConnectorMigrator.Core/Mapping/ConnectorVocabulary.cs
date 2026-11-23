namespace ConnectorMigrator.Core.Mapping;

/// <summary>
/// Every name and mapping rule the converter relies on, kept in one place so
/// business rules can be changed without touching transformation logic.
/// </summary>
public static class ConnectorVocabulary
{
    // ---- Top-level sections -------------------------------------------------
    public const string Metadata = "Metadata";
    public const string SsoInfo = "SSOInfo";
    public const string LcmInfo = "LCMInfo";
    public const string UagInfo = "UAGInfo";
    public const string VaultInfo = "VaultInfo";
    public const string FormBuilderInfo = "FormBuilderInfo";

    // ---- "Supported method" containers inside each section -------------------
    public const string SupportedSsoMethod = "SupportedSSOMethod";
    public const string SupportedLcmMethod = "SupportedLCMMethod";
    public const string SupportedReconMethod = "SupportedReconMethod";
    public const string SupportedVaultingMethod = "SupportedVaultingMethod";

    // ---- General Metadata fields --------------------------------------------
    public const string AssetName = "AssetName";
    public const string AssetUrl = "AssetURL";
    /// <summary>Old structure spells it "EntityNames"; also accept "EntityName".</summary>
    public static readonly string[] EntityNameKeys = ["EntityNames", "EntityName"];

    // ---- New attribute names (spelling taken from the reference new JSON) ----
    public const string ConnectorId = "connectorId";
    public const string Capabilities = "capabilities";
    public const string ApplicationUrl = "ApplicationUrl";
    public const string SsoOptions = "SSOOptions";
    public const string MethodUsed = "MethodUsed";
    public const string Features = "features";
    public const string DiscoveryObjects = "discoveryObjects";
    public const string UseItForCapability = "UseItforCapability";
    public const string ForSupportedBotMethod = "ForSupportedBotMethod";

    /// <summary>Suffix that identifies a FormBuilder list (FormBuilderData, ReconFormBuilderData, ...).</summary>
    public const string FormBuilderListSuffix = "FormBuilderData";

    /// <summary>The steps-collection property name (old SSO uses "Steps", LCM/UAG use "steps").</summary>
    public const string Steps = "steps";

    // ---- Capabilities, in the FRS onboarding order -----------------------------
    public const string CapDiscovery = "DISCOVERY";
    public const string CapLifecycle = "LIFECYCLE";
    public const string CapVault = "VAULT";
    public const string CapSso = "SSO";
    public static readonly string[] CapabilityOrder = [CapDiscovery, CapLifecycle, CapVault, CapSso];

    // ---- SSO method block -> SSO option ---------------------------------------
    /// <summary>
    /// Key = method block under SupportedSSOMethod. StepBased = true means the block
    /// counts only if it holds non-empty steps (bot/RPA); otherwise any configured value counts.
    /// </summary>
    public static readonly IReadOnlyList<(string MethodKey, string Option, bool StepBased)> SsoOptionMap =
    [
        ("ArconBOT", "RPA", true),
        ("CLI", "RPA", true),
        ("SAML", "SAML", false),
        ("OIDC", "OIDC", false),
        ("OAuth", "OAuth", false),
    ];
    public static readonly string[] SsoOptionOrder = ["RPA", "SAML", "OIDC", "OAuth"];

    // ---- Bot methods reported in MethodUsed ------------------------------------
    public static readonly string[] TrackedBotMethods = ["API", "SDK"];

    // ---- LCM operation section -> feature ---------------------------------------
    /// <summary>
    /// Key = LCM operation container name, lower-cased with any trailing "info" removed
    /// (so "ReActivationInfo" and "ReactivationInfo" both become "reactivation").
    /// Exact matching is used on purpose: "Provisioning" is a substring of "Deprovisioning".
    /// The order here is the order features are written in.
    /// </summary>
    public static readonly IReadOnlyList<(string NormalizedSection, string Feature)> LcmFeatureMap =
    [
        ("provisioning", "CREATE"),
        ("modificationuser", "UPDATE"),
        ("modificationentitlement", "UPDATE_ENTITLEMENT"),
        ("disable", "DISABLE"),
        ("deprovisioning", "DELETE"),
        ("reactivation", "REACTIVATE"),
        ("reactivationwithmodification", "REACTIVATE_WITH_UPDATE"),
    ];

    /// <summary>LCM containers that never hold operations and are ignored silently.</summary>
    public static readonly string[] LcmNonOperationSections = ["awametadata"];

    public static string NormalizeLcmSectionName(string key)
    {
        var k = key.ToLowerInvariant();
        return k.EndsWith("info", StringComparison.Ordinal) ? k[..^4] : k;
    }

    public static bool IsFormBuilderListKey(string key) =>
        key.EndsWith(FormBuilderListSuffix, StringComparison.OrdinalIgnoreCase);
}
