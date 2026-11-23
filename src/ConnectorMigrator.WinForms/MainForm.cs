using System.Diagnostics;
using ConnectorMigrator.Core;
using ConnectorMigrator.Core.Pipeline;

namespace ConnectorMigrator.WinForms;

/// <summary>
/// Pick old-structure connector files, pick an output folder, convert.
/// All conversion logic lives in ConnectorMigrator.Core; this form only gathers input and shows results.
/// </summary>
public sealed class MainForm : Form
{
    private readonly ListBox _inputs = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true, IntegralHeight = false };
    private readonly TextBox _outputDir = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _overwrite = new() { Text = "Overwrite existing output files", AutoSize = true };
    private readonly CheckBox _recursive = new() { Text = "Include sub-folders when adding a folder", AutoSize = true };
    private readonly ComboBox _idMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly Button _convert = new() { Text = "Convert", AutoSize = true, Padding = new Padding(12, 2, 12, 2) };
    private readonly Button _openOutput = new() { Text = "Open output folder", AutoSize = true, Enabled = false };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { AutoSize = true, Text = "Add one or more connector JSON files.", Anchor = AnchorStyles.Left };
    private readonly DataGridView _results = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
    };
    private readonly List<FileResult> _resultItems = [];

    public MainForm()
    {
        Text = "Connector Structure Migrator";
        MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        AllowDrop = true;

        _idMode.Items.AddRange(["Slug from AssetName (e.g. microsoft-o365)", "Random GUID"]);
        _idMode.SelectedIndex = 0;

        _results.Columns.Add("File", "File");
        _results.Columns.Add("Status", "Status");
        _results.Columns.Add("ConnectorId", "connectorId");
        _results.Columns.Add("Capabilities", "Capabilities");
        _results.Columns.Add("Details", "Error / warnings");
        _results.Columns["Status"]!.FillWeight = 40;
        _results.Columns["Details"]!.FillWeight = 180;
        _results.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowDetails(_resultItems[e.RowIndex]); };

        Controls.Add(BuildLayout());

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddInputs(paths); };
        _convert.Click += async (_, _) => await ConvertAsync();
        _openOutput.Click += (_, _) => OpenFolder(_outputDir.Text);
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));

        // 1. Input files
        var inputGroup = new GroupBox { Text = "1. Old-structure connector files (drag && drop supported)", Dock = DockStyle.Fill };
        var inputButtons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        inputButtons.Controls.AddRange([
            MakeButton("Add files...", (_, _) => BrowseFiles()),
            MakeButton("Add folder...", (_, _) => BrowseFolder()),
            MakeButton("Remove selected", (_, _) => RemoveSelected()),
            MakeButton("Clear", (_, _) => _inputs.Items.Clear()),
            _recursive,
        ]);
        inputGroup.Controls.Add(_inputs);
        inputGroup.Controls.Add(inputButtons);
        root.Controls.Add(inputGroup);

        // 2. Output folder
        var outputGroup = new GroupBox { Text = "2. Output folder", Dock = DockStyle.Top, AutoSize = true };
        var outputRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        outputRow.Controls.Add(_outputDir, 0, 0);
        outputRow.Controls.Add(MakeButton("Browse...", (_, _) => BrowseOutput()), 1, 0);
        outputGroup.Controls.Add(outputRow);
        root.Controls.Add(outputGroup);

        // 3. Options
        var optionsGroup = new GroupBox { Text = "3. Options", Dock = DockStyle.Top, AutoSize = true };
        var optionsRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        optionsRow.Controls.AddRange([_overwrite, new Label { Text = "connectorId:", AutoSize = true, Margin = new Padding(20, 6, 3, 0) }, _idMode]);
        optionsGroup.Controls.Add(optionsRow);
        root.Controls.Add(optionsGroup);

        // 4. Run
        var runRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        runRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        runRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        runRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        runRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        runRow.Controls.AddRange([_convert, _progress, _status, _openOutput]);
        root.Controls.Add(runRow);

        // 5. Results
        var resultsGroup = new GroupBox { Text = "Results (double-click a row for details)", Dock = DockStyle.Fill };
        resultsGroup.Controls.Add(_results);
        root.Controls.Add(resultsGroup);

        return root;
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(120, 0) };
        b.Click += onClick;
        return b;
    }

    private void BrowseFiles()
    {
        using var dlg = new OpenFileDialog { Filter = "Connector JSON (*.json)|*.json|All files (*.*)|*.*", Multiselect = true, Title = "Select old-structure connector files" };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddInputs(dlg.FileNames);
    }

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Folder containing connector JSON files", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddInputs([dlg.SelectedPath]);
    }

    private void BrowseOutput()
    {
        using var dlg = new FolderBrowserDialog { Description = "Folder for converted files", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(_outputDir.Text)) dlg.InitialDirectory = _outputDir.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _outputDir.Text = dlg.SelectedPath;
    }

    private void AddInputs(IEnumerable<string> paths)
    {
        var errors = new List<string>();
        var files = ConnectorConversionService.ExpandInputs(paths, _recursive.Checked, errors);
        var existing = _inputs.Items.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files.Where(existing.Add)) _inputs.Items.Add(f);
        if (string.IsNullOrWhiteSpace(_outputDir.Text) && files.Count > 0)
            _outputDir.Text = Path.Combine(Path.GetDirectoryName(files[0])!, "converted");
        _status.Text = $"{_inputs.Items.Count} file(s) selected.";
        if (errors.Count > 0) MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Some inputs were skipped", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void RemoveSelected()
    {
        foreach (var item in _inputs.SelectedItems.Cast<object>().ToList()) _inputs.Items.Remove(item);
        _status.Text = $"{_inputs.Items.Count} file(s) selected.";
    }

    private async Task ConvertAsync()
    {
        var files = _inputs.Items.Cast<string>().ToList();
        if (files.Count == 0) { Warn("Add at least one connector JSON file."); return; }
        if (string.IsNullOrWhiteSpace(_outputDir.Text)) { Warn("Choose an output folder."); return; }

        var options = new ConversionOptions
        {
            Overwrite = _overwrite.Checked,
            IdMode = _idMode.SelectedIndex == 1 ? ConnectorIdMode.Guid : ConnectorIdMode.Slug,
        };
        var outputDir = _outputDir.Text.Trim();

        _results.Rows.Clear();
        _resultItems.Clear();
        _progress.Maximum = files.Count;
        _progress.Value = 0;
        SetBusy(true);

        // Progress<T> captures the UI SynchronizationContext, so AddResult runs on the UI thread.
        var progress = new Progress<FileResult>(AddResult);
        try
        {
            var batch = await Task.Run(() => new ConnectorConversionService().ConvertFiles(files, outputDir, options, progress));
            _status.Text = $"Done: {batch.Converted} converted, {batch.Failed} failed.";
            _openOutput.Enabled = batch.Converted > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _status.Text = "Conversion stopped.";
            MessageBox.Show(this, ex.Message, "Cannot use the output folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AddResult(FileResult r)
    {
        _resultItems.Add(r);
        var ok = r.Status == FileStatus.Converted;
        var details = ok
            ? (r.Warnings.Count == 0 ? "" : $"{r.Warnings.Count} warning(s): {r.Warnings[0]}")
            : r.Error ?? "";
        var row = _results.Rows[_results.Rows.Add(
            Path.GetFileName(r.InputPath),
            ok ? (r.Warnings.Count > 0 ? "Converted (warnings)" : "Converted") : "Failed",
            r.Summary?.ConnectorId ?? "",
            r.Summary is null ? "" : string.Join(", ", r.Summary.Capabilities),
            details)];
        row.DefaultCellStyle.BackColor = !ok ? Color.MistyRose : r.Warnings.Count > 0 ? Color.LightYellow : Color.Honeydew;
        _progress.Value = Math.Min(_progress.Maximum, _progress.Value + 1);
    }

    private void ShowDetails(FileResult r)
    {
        var lines = new List<string> { $"Input:  {r.InputPath}", $"Output: {r.OutputPath}", $"Status: {r.Status}", "" };
        if (r.Error is not null) lines.Add($"Error: {r.Error}");
        if (r.Summary is { } s)
        {
            static string J(IReadOnlyList<string> x) => x.Count == 0 ? "(none)" : string.Join(", ", x);
            lines.AddRange([
                $"connectorId:       {s.ConnectorId}",
                $"capabilities:      {J(s.Capabilities)}",
                $"SSOOptions:        {J(s.SsoOptions)}",
                $"LCM MethodUsed:    {J(s.LcmMethodsUsed)}",
                $"LCM features:      {J(s.LcmFeatures)}",
                $"UAG MethodUsed:    {J(s.UagMethodsUsed)}",
                $"discoveryObjects:  {J(s.DiscoveryObjects)}",
                $"FormBuilderInfo:   {s.FormBuilderFieldsWritten} field(s) from {s.FormBuilderListsRelocated} list(s)",
            ]);
        }
        if (r.Warnings.Count > 0)
        {
            lines.Add("");
            lines.Add("Warnings:");
            lines.AddRange(r.Warnings.Select(w => " - " + w));
        }

        using var dlg = new Form { Text = Path.GetFileName(r.InputPath), Size = new Size(820, 480), StartPosition = FormStartPosition.CenterParent, Font = Font };
        dlg.Controls.Add(new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9F), Text = string.Join(Environment.NewLine, lines),
        });
        dlg.ShowDialog(this);
    }

    private void SetBusy(bool busy)
    {
        _convert.Enabled = !busy;
        UseWaitCursor = busy;
        if (busy) { _status.Text = "Converting..."; _openOutput.Enabled = false; }
    }

    private void Warn(string message) => MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
