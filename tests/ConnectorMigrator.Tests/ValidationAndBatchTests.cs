using ConnectorMigrator.Core;
using ConnectorMigrator.Core.Pipeline;
using static ConnectorMigrator.Tests.TestSupport;

namespace ConnectorMigrator.Tests;

public class ValidationAndBatchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("connector-migrator-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Error(string json) =>
        Assert.Throws<ConnectorValidationException>(() => Convert(json)).Message;

    [Fact]
    public void Invalid_json_reports_line_and_position()
    {
        var msg = Error("{\n  \"Metadata\": {\n    \"AssetName\": \"x\"\n  ,,\n}");
        Assert.StartsWith("Invalid JSON at line 4", msg);
    }

    [Theory]
    [InlineData("", "File is empty")]
    [InlineData("[1,2]", "root must be an object")]
    [InlineData("{\"SSOInfo\":{}}", "Missing or invalid 'Metadata'")]
    [InlineData("{\"Metadata\":{\"AssetName\":\"x\"}}", "does not look like a connector")]
    [InlineData("{\"Metadata\":{},\"LCMInfo\":[]}", "'LCMInfo' must be an object")]
    [InlineData("{\"Metadata\":{},\"LCMInfo\":{\"SupportedLCMMethod\":\"API\"}}", "SupportedLCMMethod' must be an object")]
    [InlineData("{\"Metadata\":{\"a\":1,\"a\":2},\"LCMInfo\":{}}", "Duplicate property")]
    public void Rejects_invalid_input(string json, string expected) =>
        Assert.Contains(expected, Error(json));

    [Fact]
    public void Rejects_file_already_in_new_structure()
    {
        var msg = Error(Fixture("Microsoft_O365_NewStructure.json"));
        Assert.Contains("already appears to be in the new connector structure", msg);
    }

    [Fact]
    public void Tolerates_comments_and_trailing_commas()
    {
        var json = "{ // comment\n \"Metadata\": {\"AssetName\": \"x\",}, \"LCMInfo\": {\"SupportedLCMMethod\": {}}, }";
        Assert.Equal("x", Convert(json).Summary.ConnectorId);
    }

    [Fact]
    public void Missing_sections_produce_warnings()
    {
        var outcome = Convert("{\"Metadata\":{\"AssetName\":\"x\"},\"LCMInfo\":{\"SupportedLCMMethod\":{}}}");
        Assert.Contains(outcome.Warnings, w => w.Contains("'SSOInfo' is not present"));
    }

    [Fact]
    public void Batch_continues_past_bad_files_and_guards_outputs()
    {
        var inDir = Directory.CreateDirectory(Path.Combine(_dir, "in")).FullName;
        var outDir = Path.Combine(_dir, "out");
        File.Copy(FixturePath("AWS_v10.11.005_5.json"), Path.Combine(inDir, "aws.json"));
        File.Copy(FixturePath("Microsoft_O365_OldStructure.json"), Path.Combine(inDir, "o365.json"));
        File.WriteAllText(Path.Combine(inDir, "broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(inDir, "notes.txt"), "ignored");

        var errors = new List<string>();
        var files = ConnectorConversionService.ExpandInputs([inDir], recursive: false, errors);
        Assert.Equal(3, files.Count);
        Assert.Empty(errors);

        var service = new ConnectorConversionService();
        var first = service.ConvertFiles(files, outDir, new ConversionOptions());
        Assert.Equal(2, first.Converted);
        Assert.Equal(1, first.Failed);
        Assert.Contains("Invalid JSON", first.Files.Single(f => f.Status == FileStatus.Failed).Error);
        Assert.True(File.Exists(Path.Combine(outDir, "o365.json")));

        // Re-running without overwrite refuses to clobber; with overwrite it succeeds.
        var second = service.ConvertFiles(files, outDir, new ConversionOptions());
        Assert.Equal(0, second.Converted);
        Assert.Contains(second.Files, f => f.Error!.Contains("already exists"));
        Assert.Equal(2, service.ConvertFiles(files, outDir, new ConversionOptions { Overwrite = true }).Converted);

        // Writing into the input folder would overwrite the source.
        var inPlace = service.ConvertFiles([Path.Combine(inDir, "aws.json")], inDir, new ConversionOptions { Overwrite = true });
        Assert.Contains("overwrite the input", inPlace.Files[0].Error);
    }

    [Fact]
    public void Missing_input_paths_are_reported()
    {
        var errors = new List<string>();
        var files = ConnectorConversionService.ExpandInputs([Path.Combine(_dir, "nope.json")], false, errors);
        Assert.Empty(files);
        Assert.Single(errors);
    }
}
