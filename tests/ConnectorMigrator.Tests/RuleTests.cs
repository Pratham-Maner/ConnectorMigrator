using System.Text.Json.Nodes;
using ConnectorMigrator.Core.Pipeline;
using ConnectorMigrator.Core.Transformers;
using static ConnectorMigrator.Tests.TestSupport;

namespace ConnectorMigrator.Tests;

/// <summary>Each mapping rule from the requirements, on small synthetic connectors.</summary>
public class RuleTests
{
    private static ConversionOutcome Run(JsonObject connector) => Convert(connector.ToJsonString());

    [Fact]
    public void Unconfigured_connector_gets_empty_lists()
    {
        var root = Run(MinimalConnector()).Output;
        Assert.Empty(Strings(root["Metadata"]!["capabilities"]));
        Assert.Empty(Strings(At(root, "SSOInfo", "Metadata", "SSOOptions")));
        Assert.Empty(Strings(At(root, "LCMInfo", "Metadata", "MethodUsed")));
        Assert.Empty(Strings(At(root, "LCMInfo", "Metadata", "features")));
        Assert.Empty(root["FormBuilderInfo"]!.AsArray());
    }

    [Fact]
    public void Sso_browser_steps_need_selenium_data()
    {
        var c = MinimalConnector();
        c["SSOInfo"]!["SupportedSSOMethod"]!["ArconBOT"] = new JsonObject
        {
            ["APPWebBrowser"] = new JsonObject { ["Steps"] = new JsonObject { ["URL"] = "https://x", ["SeleniumData"] = new JsonArray() } },
        };
        Assert.Empty(Strings(At(Run(c).Output, "SSOInfo", "Metadata", "SSOOptions")));

        c["SSOInfo"]!["SupportedSSOMethod"]!["ArconBOT"]!["APPWebBrowser"]!["Steps"]!["SeleniumData"] = new JsonArray(new JsonObject { ["Id"] = "user" });
        var root = Run(c).Output;
        Assert.Equal(["RPA"], Strings(At(root, "SSOInfo", "Metadata", "SSOOptions")));
        Assert.Equal(["SSO"], Strings(root["Metadata"]!["capabilities"]));
    }

    [Fact]
    public void Cli_launchers_with_metadata_count_as_sso()
    {
        var c = MinimalConnector();
        c["SSOInfo"]!["SupportedSSOMethod"]!["CLI"] = new JsonObject
        {
            ["SSH"] = new JsonObject(),                       // empty launcher: not configured
            ["App Xshell"] = new JsonObject { ["Metadata"] = new JsonObject { ["Port"] = "", ["Parameter"] = "" } },
        };
        Assert.Empty(Strings(At(Run(c).Output, "SSOInfo", "Metadata", "SSOOptions")));

        c["SSOInfo"]!["SupportedSSOMethod"]!["CLI"]!["App MobaXterm"] = new JsonObject
        {
            ["Metadata"] = new JsonObject { ["ServiceTypeID"] = 111, ["Parameter"] = "<VER>23.5</VER>", ["Port"] = "" },
        };
        var root = Run(c).Output;
        Assert.Equal(["RPA"], Strings(At(root, "SSOInfo", "Metadata", "SSOOptions")));
        Assert.Equal(["SSO"], Strings(root["Metadata"]!["capabilities"]));
    }

    [Fact]
    public void Configured_saml_oidc_oauth_become_sso_options()
    {
        var c = MinimalConnector();
        c["SSOInfo"]!["SupportedSSOMethod"]!["SAML"] = new JsonObject { ["EntityId"] = "urn:app" };
        c["SSOInfo"]!["SupportedSSOMethod"]!["OAuth"] = new JsonObject { ["ClientId"] = "abc" };
        c["SSOInfo"]!["SupportedSSOMethod"]!["OIDC"] = new JsonObject { ["Issuer"] = "" }; // blank = not configured
        var root = Run(c).Output;
        Assert.Equal(["SAML", "OAuth"], Strings(At(root, "SSOInfo", "Metadata", "SSOOptions")));
        Assert.Equal(["SSO"], Strings(root["Metadata"]!["capabilities"]));
    }

    [Fact]
    public void Api_and_sdk_both_reported_and_formbuilder_methods_merged()
    {
        var c = MinimalConnector();
        c["LCMInfo"]!["SupportedLCMMethod"]!["API"] = new JsonObject
        {
            ["ProvisioningInfo"] = new JsonObject
            {
                ["ProvisioningData"] = new JsonArray(new JsonObject { ["steps"] = OneStep() }),
                ["FormBuilderData"] = new JsonArray(FormField("token")),
            },
        };
        c["LCMInfo"]!["SupportedLCMMethod"]!["SDK"] = new JsonObject
        {
            ["DeprovisioningInfo"] = new JsonObject
            {
                ["DeProvisioningData"] = new JsonArray(new JsonObject { ["steps"] = OneStep() }),
                ["DeProvisioningFormBuilderData"] = new JsonArray(FormField("token"), FormField("region")),
            },
        };
        var root = Run(c).Output;
        Assert.Equal(["API", "SDK"], Strings(At(root, "LCMInfo", "Metadata", "MethodUsed")));
        Assert.Equal(["CREATE", "DELETE"], Strings(At(root, "LCMInfo", "Metadata", "features")));
        var fb = root["FormBuilderInfo"]!.AsArray();
        Assert.Equal(2, fb.Count);
        Assert.Equal(["API", "SDK"], Strings(fb[0]!["ForSupportedBotMethod"]));
        Assert.Equal(["SDK"], Strings(fb[1]!["ForSupportedBotMethod"]));
    }

    [Theory]
    [InlineData("ProvisioningInfo", "CREATE")]
    [InlineData("ModificationUserInfo", "UPDATE")]
    [InlineData("ModificationEntitlementInfo", "UPDATE_ENTITLEMENT")]
    [InlineData("DisableInfo", "DISABLE")]
    [InlineData("DeprovisioningInfo", "DELETE")]
    [InlineData("ReActivationInfo", "REACTIVATE")]
    [InlineData("ReactivationInfo", "REACTIVATE")]
    [InlineData("ReactivationWithModificationInfo", "REACTIVATE_WITH_UPDATE")]
    public void Lcm_section_maps_to_feature(string section, string feature)
    {
        var c = MinimalConnector();
        c["LCMInfo"]!["SupportedLCMMethod"]!["API"]![section] = new JsonObject
        {
            ["Data"] = new JsonArray(new JsonObject { ["steps"] = OneStep() }),
        };
        var root = Run(c).Output;
        Assert.Equal([feature], Strings(At(root, "LCMInfo", "Metadata", "features")));
        Assert.Equal(["LIFECYCLE"], Strings(root["Metadata"]!["capabilities"]));
    }

    [Fact]
    public void Lcm_section_with_empty_steps_is_ignored()
    {
        var c = MinimalConnector();
        c["LCMInfo"]!["SupportedLCMMethod"]!["API"]!["DisableInfo"] = new JsonObject
        {
            ["DisableData"] = new JsonArray(new JsonObject { ["steps"] = new JsonArray() }),
        };
        var root = Run(c).Output;
        Assert.Empty(Strings(At(root, "LCMInfo", "Metadata", "features")));
        Assert.Empty(Strings(At(root, "LCMInfo", "Metadata", "MethodUsed")));
    }

    [Fact]
    public void Unknown_lcm_section_with_steps_is_warned_not_guessed()
    {
        var c = MinimalConnector();
        c["LCMInfo"]!["SupportedLCMMethod"]!["API"]!["PasswordResetInfo"] = new JsonObject
        {
            ["PasswordResetData"] = new JsonArray(new JsonObject { ["steps"] = OneStep() }),
        };
        var outcome = Run(c);
        Assert.Empty(Strings(At(outcome.Output, "LCMInfo", "Metadata", "features")));
        Assert.Contains(outcome.Warnings, w => w.Contains("PasswordResetInfo") && w.Contains("no feature mapping"));
    }

    [Fact]
    public void Any_method_with_steps_is_listed_in_method_used()
    {
        var c = MinimalConnector();
        c["LCMInfo"]!["SupportedLCMMethod"]!["ArconBOT"] = new JsonObject
        {
            ["ProvisioningInfo"] = new JsonObject { ["ProvisioningData"] = new JsonArray(new JsonObject { ["steps"] = OneStep() }) },
        };
        c["LCMInfo"]!["SupportedLCMMethod"]!["SSH"] = new JsonObject();   // empty: not listed
        c["LCMInfo"]!["SupportedLCMMethod"]!["LDAP"] = new JsonObject
        {
            ["DisableInfo"] = new JsonObject { ["DisableData"] = new JsonArray(new JsonObject { ["steps"] = OneStep() }) },
        };
        c["UAGInfo"]!["SupportedReconMethod"]!["SSH"] = new JsonObject
        {
            ["ReconData"] = new JsonArray(new JsonObject { ["steps"] = OneStep() }),
        };
        var outcome = Run(c);
        Assert.Equal(["DISCOVERY", "LIFECYCLE"], Strings(outcome.Output["Metadata"]!["capabilities"]));
        Assert.Equal(["ArconBOT", "LDAP"], Strings(At(outcome.Output, "LCMInfo", "Metadata", "MethodUsed")));
        Assert.Equal(["SSH"], Strings(At(outcome.Output, "UAGInfo", "Metadata", "MethodUsed")));
        Assert.DoesNotContain(outcome.Warnings, w => w.Contains("MethodUsed"));
    }

    [Fact]
    public void Vault_with_cli_service_type_counts_as_capability()
    {
        var c = MinimalConnector();
        c["VaultInfo"]!["SupportedVaultingMethod"]!["CLI"] = new JsonObject
        {
            ["WindowRDP"] = new JsonObject(),
            ["SSH"] = new JsonObject(),
            ["ServiceType"] = new JsonArray(),                           // empty: not configured
            ["VaultFormBuilderData"] = new JsonArray(FormField("key")),  // form fields alone do not count
        };
        Assert.Empty(Strings(Run(c).Output["Metadata"]!["capabilities"]));

        c["VaultInfo"]!["SupportedVaultingMethod"]!["CLI"]!["ServiceType"] = new JsonArray(49);
        Assert.Equal(["VAULT"], Strings(Run(c).Output["Metadata"]!["capabilities"]));
    }

    [Fact]
    public void Vault_and_discovery_capabilities()
    {
        var c = MinimalConnector();
        c["VaultInfo"]!["SupportedVaultingMethod"]!["API"] = new JsonObject
        {
            ["VaultData"] = new JsonArray(new JsonObject { ["steps"] = OneStep() }),
        };
        c["UAGInfo"]!["SupportedReconMethod"]!["SDK"] = new JsonObject
        {
            ["ReconData"] = new JsonArray(new JsonObject { ["steps"] = OneStep() }),
            ["ReconFormBuilderData"] = new JsonArray(FormField("key")),
        };
        var root = Run(c).Output;
        Assert.Equal(["DISCOVERY", "VAULT"], Strings(root["Metadata"]!["capabilities"]));
        Assert.Equal(["SDK"], Strings(At(root, "UAGInfo", "Metadata", "MethodUsed")));
        Assert.Equal(["Groups"], Strings(At(root, "UAGInfo", "Metadata", "discoveryObjects")));
        Assert.Equal(["UAGInfo"], Strings(root["FormBuilderInfo"]![0]!["UseItforCapability"]));
    }

    [Fact]
    public void Ids_are_unique_within_a_batch_and_guid_mode_works()
    {
        var ids = new ConnectorIdRegistry();
        var a = Convert(MinimalConnector().ToJsonString(), ids: ids).Summary.ConnectorId;
        var b = Convert(MinimalConnector().ToJsonString(), ids: ids).Summary.ConnectorId;
        Assert.Equal("test-app", a);
        Assert.Equal("test-app-2", b);

        var g = Convert(MinimalConnector().ToJsonString(), options: new ConversionOptions { IdMode = ConnectorIdMode.Guid }).Summary.ConnectorId;
        Assert.True(Guid.TryParse(g, out _));
    }

    [Fact]
    public void Empty_asset_name_falls_back_to_file_name()
    {
        var outcome = Convert(MinimalConnector("  ").ToJsonString(), source: "My Connector v2.json");
        Assert.Equal("my-connector-v2", outcome.Summary.ConnectorId);
        Assert.Contains(outcome.Warnings, w => w.Contains("AssetName"));
    }

    [Theory]
    [InlineData("Microsoft O365", "microsoft-o365")]
    [InlineData("AWS", "aws")]
    [InlineData("  SAP  S/4HANA (Cloud) ", "sap-s-4hana-cloud")]
    [InlineData("---", "")]
    public void Slugify(string input, string expected) =>
        Assert.Equal(expected, ConnectorMetadataTransformer.Slugify(input));
}
