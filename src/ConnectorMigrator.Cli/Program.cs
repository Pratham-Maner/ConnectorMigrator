using System.Text.Encodings.Web;
using System.Text.Json;
using ConnectorMigrator.Core;
using ConnectorMigrator.Core.Pipeline;

namespace ConnectorMigrator.Cli;

internal static class Program
{
    private const int ExitOk = 0, ExitSomeFailed = 1, ExitUsage = 2;

    private const string Usage = """
        Connector Migrator - converts old-structure connector JSON files to the new structure.

        Usage:
          ConnectorMigrator <input>... -o <output-folder> [options]

        Inputs can be .json files and/or folders (every *.json in the folder is converted).

        Options:
          -o, --output <folder>    Folder for converted files (created if missing). Required.
          -r, --recursive          Include *.json files in sub-folders of input folders.
              --overwrite          Replace output files that already exist.
              --id-mode <mode>     connectorId style: slug (default, e.g. "microsoft-o365") or guid.
              --report <file>      Also write a JSON report of the run.
          -q, --quiet              Print only failures and the summary.
          -h, --help               Show this help.

        Exit codes: 0 = all converted, 1 = one or more files failed, 2 = invalid arguments.
        """;

    private sealed class Args
    {
        public List<string> Inputs { get; } = [];
        public string? Output { get; set; }
        public bool Recursive { get; set; }
        public bool Overwrite { get; set; }
        public ConnectorIdMode IdMode { get; set; } = ConnectorIdMode.Slug;
        public string? Report { get; set; }
        public bool Quiet { get; set; }
    }

    private static int Main(string[] argv)
    {
        if (argv.Length == 0 || argv.Any(a => a is "-h" or "--help" or "/?"))
        {
            Console.WriteLine(Usage);
            return argv.Length == 0 ? ExitUsage : ExitOk;
        }

        if (!TryParse(argv, out var args, out var parseError))
        {
            Console.Error.WriteLine($"Error: {parseError}");
            Console.Error.WriteLine("Run with --help for usage.");
            return ExitUsage;
        }

        var inputErrors = new List<string>();
        var files = ConnectorConversionService.ExpandInputs(args.Inputs, args.Recursive, inputErrors);
        foreach (var e in inputErrors) Console.Error.WriteLine($"Error: {e}");
        if (files.Count == 0)
        {
            Console.Error.WriteLine("Error: no input files to convert.");
            return ExitUsage;
        }

        var options = new ConversionOptions { IdMode = args.IdMode, Overwrite = args.Overwrite };
        var service = new ConnectorConversionService();
        var progress = new SyncProgress<FileResult>(r => Print(r, args.Quiet));

        Console.WriteLine($"Converting {files.Count} file(s) -> {Path.GetFullPath(args.Output!)}");
        var batch = service.ConvertFiles(files, args.Output!, options, progress);

        Console.WriteLine();
        Console.WriteLine($"Done: {batch.Converted} converted, {batch.Failed} failed" +
                          (inputErrors.Count > 0 ? $", {inputErrors.Count} input path problem(s)" : "") + ".");

        if (args.Report is not null) WriteReport(args.Report, batch, inputErrors);

        return batch.Failed > 0 || inputErrors.Count > 0 ? ExitSomeFailed : ExitOk;
    }

    private static bool TryParse(string[] argv, out Args args, out string error)
    {
        args = new Args();
        error = "";
        for (var i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            string? Next()
            {
                if (i + 1 < argv.Length) return argv[++i];
                return null;
            }

            switch (a)
            {
                case "-o" or "--output":
                    args.Output = Next();
                    if (args.Output is null) { error = $"{a} needs a folder."; return false; }
                    break;
                case "-r" or "--recursive": args.Recursive = true; break;
                case "--overwrite": args.Overwrite = true; break;
                case "-q" or "--quiet": args.Quiet = true; break;
                case "--report":
                    args.Report = Next();
                    if (args.Report is null) { error = "--report needs a file path."; return false; }
                    break;
                case "--id-mode":
                    var mode = Next();
                    if (!Enum.TryParse<ConnectorIdMode>(mode, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                    {
                        error = $"--id-mode must be 'slug' or 'guid' (got '{mode}').";
                        return false;
                    }
                    args.IdMode = parsed;
                    break;
                default:
                    if (a.StartsWith('-') && a.Length > 1 && !File.Exists(a) && !Directory.Exists(a))
                    {
                        error = $"Unknown option '{a}'.";
                        return false;
                    }
                    args.Inputs.Add(a);
                    break;
            }
        }
        if (args.Inputs.Count == 0) { error = "No input files or folders given."; return false; }
        if (string.IsNullOrWhiteSpace(args.Output)) { error = "Output folder is required (-o <folder>)."; return false; }
        return true;
    }

    private static void Print(FileResult r, bool quiet)
    {
        var name = Path.GetFileName(r.InputPath);
        if (r.Status == FileStatus.Failed)
        {
            WriteColored($"[FAIL] {name}", ConsoleColor.Red, Console.Error);
            Console.Error.WriteLine($"       {r.Error}");
            return;
        }
        if (quiet) return;

        var s = r.Summary!;
        WriteColored($"[ OK ] {name}", ConsoleColor.Green, Console.Out);
        Console.WriteLine($"       connectorId      : {s.ConnectorId}");
        Console.WriteLine($"       capabilities     : {Join(s.Capabilities)}");
        Console.WriteLine($"       SSOOptions       : {Join(s.SsoOptions)}");
        Console.WriteLine($"       LCM MethodUsed   : {Join(s.LcmMethodsUsed)}   features: {Join(s.LcmFeatures)}");
        Console.WriteLine($"       UAG MethodUsed   : {Join(s.UagMethodsUsed)}   discoveryObjects: {Join(s.DiscoveryObjects)}");
        Console.WriteLine($"       FormBuilderInfo  : {s.FormBuilderFieldsWritten} field(s) from {s.FormBuilderListsRelocated} list(s)");
        foreach (var w in r.Warnings)
            WriteColored($"       warning: {w}", ConsoleColor.Yellow, Console.Out);
    }

    private static string Join(IReadOnlyList<string> items) => items.Count == 0 ? "(none)" : string.Join(", ", items);

    private static void WriteColored(string text, ConsoleColor color, TextWriter writer)
    {
        var redirected = writer == Console.Error ? Console.IsErrorRedirected : Console.IsOutputRedirected;
        if (!redirected) Console.ForegroundColor = color;
        writer.WriteLine(text);
        if (!redirected) Console.ResetColor();
    }

    private static void WriteReport(string path, BatchResult batch, List<string> inputErrors)
    {
        var report = new
        {
            generatedUtc = DateTime.UtcNow,
            converted = batch.Converted,
            failed = batch.Failed,
            inputErrors,
            files = batch.Files.Select(f => new
            {
                input = f.InputPath,
                output = f.Status == FileStatus.Converted ? f.OutputPath : null,
                status = f.Status.ToString(),
                error = f.Error,
                warnings = f.Warnings,
                summary = f.Summary,
            }),
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json);
        Console.WriteLine($"Report written to {full}");
    }

    /// <summary>IProgress that runs the callback inline (Progress&lt;T&gt; posts to the thread pool in a console app).</summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
