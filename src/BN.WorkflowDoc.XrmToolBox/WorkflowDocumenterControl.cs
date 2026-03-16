using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using BN.WorkflowDoc.XrmToolBox.Services;
using System.Windows.Forms;
using XrmToolBox.Extensibility;

namespace BN.WorkflowDoc.XrmToolBox;

public sealed class WorkflowDocumenterControl : PluginControlBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDataverseWorkflowProvider _workflowProvider;
    private readonly CheckedListBox _categoryFilter;
    private readonly TextBox _searchBox;
    private readonly Button _refreshButton;
    private readonly Button _selectAllButton;
    private readonly Button _clearSelectionButton;
    private readonly Button _exportButton;
    private readonly Label _summaryLabel;
    private readonly ListView _workflowList;
    private IReadOnlyList<WorkflowCatalogItem> _catalogItems = Array.Empty<WorkflowCatalogItem>();

    public WorkflowDocumenterControl()
        : this(new DataverseWorkflowProvider())
    {
    }

    internal WorkflowDocumenterControl(IDataverseWorkflowProvider workflowProvider)
    {
        _workflowProvider = workflowProvider;

        Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        filterPanel.Controls.Add(new Label { AutoSize = true, Text = "Search", Margin = new Padding(0, 8, 8, 0) });
        _searchBox = new TextBox { Width = 280 };
        _searchBox.TextChanged += (_, _) => ApplyFilters();
        filterPanel.Controls.Add(_searchBox);

        filterPanel.Controls.Add(new Label { AutoSize = true, Text = "Categories", Margin = new Padding(16, 8, 8, 0) });
        _categoryFilter = new CheckedListBox
        {
            CheckOnClick = true,
            Height = 58,
            Width = 250
        };
        _categoryFilter.Items.AddRange(new object[] { "Workflow", "Dialog", "Action" });
        for (var i = 0; i < _categoryFilter.Items.Count; i++)
        {
            _categoryFilter.SetItemChecked(i, true);
        }
        _categoryFilter.ItemCheck += (_, _) => BeginInvoke(new Action(ApplyFilters));
        filterPanel.Controls.Add(_categoryFilter);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        _refreshButton = new Button { AutoSize = true, Text = "Load Workflows" };
        _refreshButton.Click += (_, _) => ExecuteMethod(LoadCatalog);
        actionPanel.Controls.Add(_refreshButton);

        _selectAllButton = new Button { AutoSize = true, Text = "Select All" };
        _selectAllButton.Click += (_, _) => SetAllSelections(true);
        actionPanel.Controls.Add(_selectAllButton);

        _clearSelectionButton = new Button { AutoSize = true, Text = "Clear Selection" };
        _clearSelectionButton.Click += (_, _) => SetAllSelections(false);
        actionPanel.Controls.Add(_clearSelectionButton);

        _exportButton = new Button { AutoSize = true, Text = "Generate Documents" };
        _exportButton.Click += (_, _) => ExecuteMethod(ExportSelectedWorkflows);
        actionPanel.Controls.Add(_exportButton);

        topPanel.Controls.Add(filterPanel, 0, 0);
        topPanel.Controls.Add(actionPanel, 1, 0);

        _workflowList = new ListView
        {
            CheckBoxes = true,
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            View = View.Details
        };
        _workflowList.Columns.Add("Workflow", 260);
        _workflowList.Columns.Add("Category", 90);
        _workflowList.Columns.Add("Primary Entity", 120);
        _workflowList.Columns.Add("Mode", 95);
        _workflowList.Columns.Add("Scope", 180);
        _workflowList.Columns.Add("State", 90);
        _workflowList.Columns.Add("Trigger", 260);
        _workflowList.ItemChecked += (_, _) => UpdateSummary();

        _summaryLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Load workflows from a Dataverse connection to begin."
        };

        layout.Controls.Add(topPanel, 0, 0);
        layout.Controls.Add(_workflowList, 0, 1);
        layout.Controls.Add(_summaryLabel, 0, 2);
        Controls.Add(layout);
    }

    public override void UpdateConnection(Microsoft.Xrm.Sdk.IOrganizationService newService, McTools.Xrm.Connection.ConnectionDetail detail, string actionName, object parameter)
    {
        base.UpdateConnection(newService, detail, actionName, parameter);

        if (newService != null && IsHandleCreated)
        {
            LoadCatalog();
        }
    }

    private void LoadCatalog()
    {
        WorkAsync(new WorkAsyncInfo
        {
            Message = "Loading Dataverse workflows...",
            Work = (_, args) =>
            {
                args.Result = _workflowProvider.GetCatalogAsync(Service).GetAwaiter().GetResult();
            },
            PostWorkCallBack = args =>
            {
                if (args.Error != null)
                {
                    ShowErrorDialog(args.Error, "Load workflows");
                    return;
                }

                var result = args.Result as ParseResult<IReadOnlyList<WorkflowCatalogItem>>;
                if (result?.Value == null)
                {
                    var message = result?.ErrorMessage ?? "Workflow catalog could not be loaded.";
                    MessageBox.Show(this, message, "Load workflows", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _catalogItems = result.Value;
                ApplyFilters();

                if (result.Warnings.Count > 0)
                {
                    LogWarning("Workflow catalog loaded with {0} warnings", result.Warnings.Count);
                }
            }
        });
    }

    private void ExportSelectedWorkflows()
    {
        var selectedIds = _workflowList.Items
            .Cast<ListViewItem>()
            .Where(item => item.Checked)
            .Select(item => ((WorkflowCatalogItem)item.Tag).WorkflowId)
            .ToArray();

        if (selectedIds.Length == 0)
        {
            MessageBox.Show(this, "Select one or more workflows before generating documentation.", "Generate documents", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose an output folder for workflow documentation."
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        _exportButton.Enabled = false;

        WorkAsync(new WorkAsyncInfo
        {
            Message = "Generating workflow documentation...",
            Work = (_, args) =>
            {
                var definitionResult = _workflowProvider.GetDefinitionsAsync(Service, selectedIds).GetAwaiter().GetResult();
                if (definitionResult.Value == null)
                {
                    args.Result = new WorkflowExportResult(definitionResult, null, dialog.SelectedPath, null);
                    return;
                }

                var request = new LiveWorkflowDocumentationRequest(
                    SourceName: ConnectionDetail?.ConnectionName ?? "Dataverse Selection",
                    Workflows: definitionResult.Value,
                    Warnings: definitionResult.Warnings);

                var requestPath = Path.Combine(Path.GetTempPath(), $"bn-workflowdoc-{Guid.NewGuid():N}.json");
                File.WriteAllText(requestPath, JsonSerializer.Serialize(request, JsonOptions));

                WorkerInvocationResult? workerResult = null;
                try
                {
                    workerResult = InvokeCliWorker(requestPath, dialog.SelectedPath);
                }
                finally
                {
                    TryDeleteFile(requestPath);
                }

                args.Result = new WorkflowExportResult(definitionResult, workerResult, dialog.SelectedPath, requestPath);
            },
            PostWorkCallBack = args =>
            {
                _exportButton.Enabled = true;

                if (args.Error != null)
                {
                    ShowErrorDialog(args.Error, "Generate documents");
                    return;
                }

                var result = args.Result as WorkflowExportResult;
                if (result?.DefinitionResult?.Value == null)
                {
                    MessageBox.Show(this, result?.DefinitionResult?.ErrorMessage ?? "Workflow definitions could not be loaded.", "Generate documents", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (result.WorkerResult?.Payload == null || !string.Equals(result.WorkerResult.Payload.Status, "Success", StringComparison.OrdinalIgnoreCase) && !string.Equals(result.WorkerResult.Payload.Status, "PartialSuccess", StringComparison.OrdinalIgnoreCase))
                {
                    var errorMessage = result.WorkerResult?.Payload?.Error;
                    if (string.IsNullOrWhiteSpace(errorMessage))
                    {
                        errorMessage = result.WorkerResult?.StdErr;
                    }

                    MessageBox.Show(this, errorMessage ?? "Documentation files could not be written.", "Generate documents", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var workflowFileCount = result.WorkerResult.Payload.WorkflowDocxFiles?.Count ?? 0;
                var workerWarningCount = result.WorkerResult.Payload.Warnings?.Count ?? 0;
                var warningCount = result.DefinitionResult.Warnings.Count + workerWarningCount;
                var message = $"Generated {workflowFileCount} workflow documents and an overview document in:{Environment.NewLine}{result.WorkerResult.Payload.OutputFolder}";

                if (warningCount > 0)
                {
                    message += $"{Environment.NewLine}{Environment.NewLine}Warnings: {warningCount}. Review the generated appendices and CLI output for details.";
                }

                MessageBox.Show(this, message, "Generate documents", MessageBoxButtons.OK, MessageBoxIcon.Information);

                try
                {
                    Process.Start("explorer.exe", result.WorkerResult.Payload.OutputFolder);
                }
                catch
                {
                    LogInfo("Output folder ready at {0}", result.WorkerResult.Payload.OutputFolder);
                }
            }
        });
    }

    private static WorkerInvocationResult InvokeCliWorker(string requestPath, string outputFolder)
    {
        var cliPath = ResolveCliPath();
        var startInfo = BuildStartInfo(cliPath, requestPath, outputFolder);

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        CliDocxResult? payload = null;
        if (!string.IsNullOrWhiteSpace(stdOut))
        {
            try
            {
                payload = JsonSerializer.Deserialize<CliDocxResult>(stdOut, JsonOptions);
            }
            catch
            {
                payload = null;
            }
        }

        if (payload == null)
        {
            payload = new CliDocxResult(
                Status: process.ExitCode == 0 ? "Success" : "Failed",
                Input: requestPath,
                OutputFolder: outputFolder,
                WorkflowDocxFiles: Array.Empty<string>(),
                OverviewDocxFile: null,
                Warnings: Array.Empty<CliWarning>(),
                Error: string.IsNullOrWhiteSpace(stdErr) ? null : stdErr);
        }

        return new WorkerInvocationResult(payload, process.ExitCode, stdOut, stdErr);
    }

    private static ProcessStartInfo BuildStartInfo(string cliPath, string requestPath, string outputFolder)
    {
        var arguments = $"docx-live \"{requestPath}\" \"{outputFolder}\"";

        if (cliPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo("dotnet", $"\"{cliPath}\" {arguments}")
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
        }

        return new ProcessStartInfo(cliPath, arguments)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
    }

    private static string ResolveCliPath()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(WorkflowDocumenterControl).Assembly.Location)
            ?? AppContext.BaseDirectory;

        foreach (var root in EnumerateCandidateRoots(assemblyDir))
        {
            foreach (var candidate in EnumerateCliCandidates(root))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("Could not locate BN.WorkflowDoc.Cli.dll or BN.WorkflowDoc.Cli.exe for live document generation.");
    }

    private static IEnumerable<string> EnumerateCandidateRoots(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current != null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static IEnumerable<string> EnumerateCliCandidates(string root)
    {
        yield return Path.Combine(root, "BN.WorkflowDoc.Cli.dll");
        yield return Path.Combine(root, "BN.WorkflowDoc.Cli.exe");
        yield return Path.Combine(root, "src", "BN.WorkflowDoc.Cli", "bin", "Debug", "net10.0", "BN.WorkflowDoc.Cli.dll");
        yield return Path.Combine(root, "src", "BN.WorkflowDoc.Cli", "bin", "Release", "net10.0", "BN.WorkflowDoc.Cli.dll");
        yield return Path.Combine(root, "src", "BN.WorkflowDoc.Cli", "bin", "Debug", "net10.0", "BN.WorkflowDoc.Cli.exe");
        yield return Path.Combine(root, "src", "BN.WorkflowDoc.Cli", "bin", "Release", "net10.0", "BN.WorkflowDoc.Cli.exe");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private void ApplyFilters()
    {
        var selectedCategories = _categoryFilter.CheckedItems.Cast<object>()
            .Select(item => item.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var search = _searchBox.Text?.Trim() ?? string.Empty;

        var filtered = _catalogItems.Where(item =>
                (selectedCategories.Count == 0 || selectedCategories.Contains(item.Category))
                && (search.Length == 0
                    || item.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || item.PrimaryEntity.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || item.TriggerSummary.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray();

        _workflowList.BeginUpdate();
        _workflowList.Items.Clear();
        foreach (var item in filtered)
        {
            var row = new ListViewItem(item.DisplayName)
            {
                Tag = item
            };
            row.SubItems.Add(item.Category);
            row.SubItems.Add(item.PrimaryEntity);
            row.SubItems.Add(item.ExecutionMode);
            row.SubItems.Add(item.Scope);
            row.SubItems.Add(item.State);
            row.SubItems.Add(item.TriggerSummary);
            _workflowList.Items.Add(row);
        }
        _workflowList.EndUpdate();

        UpdateSummary();
    }

    private void SetAllSelections(bool isChecked)
    {
        foreach (ListViewItem item in _workflowList.Items)
        {
            item.Checked = isChecked;
        }

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selectedCount = _workflowList.Items.Cast<ListViewItem>().Count(item => item.Checked);
        _summaryLabel.Text = $"Loaded {_workflowList.Items.Count} workflows. Selected {selectedCount} for export.";
    }

    private sealed class WorkflowExportResult
    {
        public WorkflowExportResult(
            ParseResult<IReadOnlyList<WorkflowDefinitionPayload>>? definitionResult,
            WorkerInvocationResult? workerResult,
            string outputFolder,
            string? requestPath)
        {
            DefinitionResult = definitionResult;
            WorkerResult = workerResult;
            OutputFolder = outputFolder;
            RequestPath = requestPath;
        }

        public ParseResult<IReadOnlyList<WorkflowDefinitionPayload>>? DefinitionResult { get; }

        public WorkerInvocationResult? WorkerResult { get; }

        public string OutputFolder { get; }

        public string? RequestPath { get; }
    }

    private sealed class WorkerInvocationResult
    {
        public WorkerInvocationResult(CliDocxResult payload, int exitCode, string stdOut, string stdErr)
        {
            Payload = payload;
            ExitCode = exitCode;
            StdOut = stdOut;
            StdErr = stdErr;
        }

        public CliDocxResult Payload { get; }

        public int ExitCode { get; }

        public string StdOut { get; }

        public string StdErr { get; }
    }
}
