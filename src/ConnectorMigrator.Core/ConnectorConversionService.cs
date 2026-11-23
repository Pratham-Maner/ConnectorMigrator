using System.Text;
using ConnectorMigrator.Core.Pipeline;

namespace ConnectorMigrator.Core;

public enum FileStatus { Converted, Failed }

public sealed class FileResult
{
    public required string InputPath { get; init; }
    public string? OutputPath { get; init; }
    public FileStatus Status { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public ConversionSummary? Summary { get; init; }
}

public sealed class BatchResult
{
    public required IReadOnlyList<FileResult> Files { get; init; }
    public int Converted => Files.Count(f => f.Status == FileStatus.Converted);
    public int Failed => Files.Count(f => f.Status == FileStatus.Failed);
}

/// <summary>File-level entry point used by both the CLI and the GUI.</summary>
public sealed class ConnectorConversionService(ConversionPipeline? pipeline = null)
{
    private readonly ConversionPipeline _pipeline = pipeline ?? new ConversionPipeline();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Expands folders to their *.json files; reports paths that do not exist.</summary>
    public static IReadOnlyList<string> ExpandInputs(IEnumerable<string> paths, bool recursive, ICollection<string> errors)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var raw in paths)
        {
            var path = Path.GetFullPath(raw);
            if (Directory.Exists(path))
            {
                var files = Directory.EnumerateFiles(path, "*.json", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                if (files.Count == 0) errors.Add($"No .json files found in folder: {raw}");
                foreach (var f in files) if (seen.Add(f)) result.Add(f);
            }
            else if (File.Exists(path))
            {
                if (seen.Add(path)) result.Add(path);
            }
            else errors.Add($"Input not found: {raw}");
        }
        return result;
    }

    /// <summary>
    /// Converts each file independently into <paramref name="outputDirectory"/> using the same file
    /// name. A failure in one file never stops the batch.
    /// </summary>
    public BatchResult ConvertFiles(
        IReadOnlyList<string> inputFiles,
        string outputDirectory,
        ConversionOptions options,
        IProgress<FileResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var outDir = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outDir);

        var ids = new ConnectorIdRegistry();
        var claimedOutputs = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var results = new List<FileResult>();

        foreach (var input in inputFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ConvertOne(Path.GetFullPath(input), outDir, options, ids, claimedOutputs);
            results.Add(result);
            progress?.Report(result);
        }
        return new BatchResult { Files = results };
    }

    private FileResult ConvertOne(string input, string outDir, ConversionOptions options, ConnectorIdRegistry ids, HashSet<string> claimedOutputs)
    {
        var output = Path.Combine(outDir, Path.GetFileName(input));
        FileResult Fail(string message) => new() { InputPath = input, OutputPath = output, Status = FileStatus.Failed, Error = message };

        try
        {
            if (string.Equals(Path.GetFullPath(output), input, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return Fail("Output would overwrite the input file. Choose a different output folder.");
            if (!claimedOutputs.Add(output))
                return Fail($"Another input in this batch has the same file name and already writes '{Path.GetFileName(output)}'.");
            if (File.Exists(output) && !options.Overwrite)
                return Fail("Output file already exists. Enable overwrite to replace it.");

            var text = File.ReadAllText(input);
            var outcome = _pipeline.Convert(text, Path.GetFileName(input), options, ids);
            File.WriteAllText(output, outcome.OutputJson + Environment.NewLine, Utf8NoBom);

            return new FileResult
            {
                InputPath = input,
                OutputPath = output,
                Status = FileStatus.Converted,
                Warnings = outcome.Warnings,
                Summary = outcome.Summary,
            };
        }
        catch (ConnectorValidationException ex) { return Fail(ex.Message); }
        catch (IOException ex) { return Fail($"File error: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { return Fail($"Access denied: {ex.Message}"); }
        catch (DecoderFallbackException ex) { return Fail($"File is not valid text: {ex.Message}"); }
    }
}
