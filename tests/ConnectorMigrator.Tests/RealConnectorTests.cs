using System.Text.Json.Nodes;
using static ConnectorMigrator.Tests.TestSupport;

namespace ConnectorMigrator.Tests;

/// <summary>End-to-end checks against the real connector files supplied with the requirements.</summary>
public class RealConnectorTests
{
    private const string O365 = "Microsoft_O365_OldStructure.json";
    private const string Aws = "AWS_v10.11.005_5.json";

    [Fact]
    public void O365_general_metadata()
    {
        var root = Convert(Fixture(O365)).Output;
        Assert.Equal("microsoft-o365", root["Metadata"]!["connectorId"]!.GetValue<string>());
        Assert.Equal(["DISCOVERY", "LIFECYCLE", "SSO"], Strings(root["Metadata"]!["capabilities"]));
        // Added just before EntityNames, which keeps its old name (brief followed literally).
        Assert.Equal(["AssetName", "Assetlogo", "AssetCategory", "AssetType", "AssetVersion", "AssetURL", "AssetServiceType", "connectorId", "capabilities", "EntityNames"],
            root["Metadata"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void O365_section_metadata()
    {
        var root = Convert(Fixture(O365)).Output;

        Assert.Equal("Metadata", root["SSOInfo"]!.AsObject().First().Key);
        Assert.Equal("https://portal.azure.com/", At(root, "SSOInfo", "Metadata", "ApplicationUrl")!.GetValue<string>());
        Assert.Equal(["RPA"], Strings(At(root, "SSOInfo", "Metadata", "SSOOptions")));

        Assert.Equal(["API"], Strings(At(root, "LCMInfo", "Metadata", "MethodUsed")));
        Assert.Equal(["CREATE", "UPDATE", "UPDATE_ENTITLEMENT", "DISABLE", "DELETE", "REACTIVATE", "REACTIVATE_WITH_UPDATE"],
            Strings(At(root, "LCMInfo", "Metadata", "features")));

        Assert.Equal(["API"], Strings(At(root, "UAGInfo", "Metadata", "MethodUsed")));
        Assert.Equal(["Groups", "Roles"], Strings(At(root, "UAGInfo", "Metadata", "discoveryObjects")));

        Assert.Null(root["VaultInfo"]!["Metadata"]);
    }

    [Fact]
    public void O365_formbuilder_is_relocated_merged_and_tagged()
    {
        var outcome = Convert(Fixture(O365));
        var fields = outcome.Output["FormBuilderInfo"]!.AsArray().Select(f => f!.AsObject()).ToList();

        // 22 provisioning fields + usersendpoint + getgroupendpoint (recon-only).
        Assert.Equal(24, fields.Count);
        Assert.Equal(fields.Count, fields.Select(f => f["KeyName"]!.GetValue<string>()).Distinct().Count());
        Assert.DoesNotContain("FormBuilderData\"", outcome.OutputJson);

        JsonObject F(string key) => fields.Single(f => f["KeyName"]!.GetValue<string>() == key);
        Assert.Equal(["LCMInfo", "UAGInfo"], Strings(F("clientid")["UseItforCapability"]));
        Assert.Equal(["LCMInfo"], Strings(F("Leaver")["UseItforCapability"]));
        Assert.Equal(["UAGInfo"], Strings(F("usersendpoint")["UseItforCapability"]));
        Assert.All(fields, f => Assert.Equal(["API"], Strings(f["ForSupportedBotMethod"])));

        // First occurrence (Provisioning) wins; the ModificationEntitlement copy holds a wrong Value.
        Assert.Equal("https://graph.microsoft.com/v1.0/groups/", F("groupendpoint")["Value"]!.GetValue<string>());
        Assert.Contains(outcome.Warnings, w => w.Contains("'groupendpoint' differs"));

        // New attributes are appended; everything else is untouched.
        Assert.Equal(["UseItforCapability", "ForSupportedBotMethod"], F("clientid").Select(p => p.Key).TakeLast(2));
    }

    [Theory]
    [InlineData(O365)]
    [InlineData(Aws)]
    public void Everything_except_migrated_parts_is_preserved(string file)
    {
        var input = JsonNode.Parse(Fixture(file));
        var output = Convert(Fixture(file)).Output;
        Assert.True(JsonNode.DeepEquals(StripMigratedParts(input), StripMigratedParts(output)));
    }

    [Fact]
    public void O365_matches_reference_new_structure_where_rules_agree()
    {
        var output = Convert(Fixture(O365)).Output;
        var reference = JsonNode.Parse(Fixture("Microsoft_O365_NewStructure.json"))!;

        Assert.Equal(reference["Metadata"]!["connectorId"]!.GetValue<string>(), output["Metadata"]!["connectorId"]!.GetValue<string>());
        Assert.Equal(Strings(reference["Metadata"]!["capabilities"]).Order(), Strings(output["Metadata"]!["capabilities"]).Order());
        Assert.True(JsonNode.DeepEquals(reference["SSOInfo"]!["Metadata"], output["SSOInfo"]!["Metadata"]));
        Assert.True(JsonNode.DeepEquals(At(reference, "LCMInfo", "Metadata", "MethodUsed"), At(output, "LCMInfo", "Metadata", "MethodUsed")));
        Assert.True(JsonNode.DeepEquals(reference["VaultInfo"], output["VaultInfo"]));
        Assert.True(JsonNode.DeepEquals(At(reference, "SSOInfo", "SupportedSSOMethod", "ArconBOT"), At(output, "SSOInfo", "SupportedSSOMethod", "ArconBOT")));
    }

    [Fact]
    public void Aws_sdk_connector()
    {
        var outcome = Convert(Fixture(Aws));
        var root = outcome.Output;

        Assert.Equal("aws", root["Metadata"]!["connectorId"]!.GetValue<string>());
        Assert.Equal(["DISCOVERY", "LIFECYCLE", "SSO"], Strings(root["Metadata"]!["capabilities"]));
        Assert.Equal(["RPA"], Strings(At(root, "SSOInfo", "Metadata", "SSOOptions")));
        Assert.Equal(["SDK"], Strings(At(root, "LCMInfo", "Metadata", "MethodUsed")));
        // ModificationUserInfo is {} and DisableData / ReactivationData are [] -> no UPDATE/DISABLE/REACTIVATE.
        Assert.Equal(["CREATE", "UPDATE_ENTITLEMENT", "DELETE"], Strings(At(root, "LCMInfo", "Metadata", "features")));
        Assert.Equal(["SDK"], Strings(At(root, "UAGInfo", "Metadata", "MethodUsed")));
        Assert.Equal(["Groups", "Policies"], Strings(At(root, "UAGInfo", "Metadata", "discoveryObjects")));

        var fields = root["FormBuilderInfo"]!.AsArray();
        Assert.Equal(6, fields.Count);
        Assert.All(fields, f => Assert.Equal(["SDK"], Strings(f!["ForSupportedBotMethod"])));

        // SOT is outside the scope of the migration and keeps its own FormBuilder list.
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Fixture(Aws))!["SOT"], root["SOT"]));
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void Output_keeps_placeholders_readable()
    {
        var json = Convert(Fixture(Aws)).OutputJson;
        Assert.Contains("\"<username>\"", json);
        Assert.DoesNotContain("\\u003C", json);
    }
}
