using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BN.WorkflowDoc.XrmToolBox.Services;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using XrmToolBox.Extensibility;

namespace BN.WorkflowDoc.XrmToolBox;

public sealed class WorkflowDocumenterControl : PluginControlBase
{
    private const string WorkflowCategory = "Workflow";
    private const string DialogCategory = "Dialog";
    private const string ActionCategory = "Action";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDataverseWorkflowProvider _workflowProvider;
    private readonly Dictionary<string, CheckBox> _categoryChips;
    private readonly TextBox _searchBox;
    private readonly Button _refreshButton;
    private readonly Button _selectAllButton;
    private readonly Button _selectVisibleButton;
    private readonly Button _clearSelectionButton;
    private readonly Button _settingsButton;
    private readonly Button _exportButton;
    private readonly Label _summaryLabel;
    private readonly Label _timestampLabel;
    private readonly ListView _workflowList;
    private IReadOnlyList<WorkflowCatalogItem> _catalogItems = Array.Empty<WorkflowCatalogItem>();
    private readonly HashSet<Guid> _selectedWorkflowIds = new();
    private readonly string _settingsFilePath;
    private WorkflowPluginSettings _settings;
    private bool _suppressSelectionEvents;
    private int _filteredItemCount;

    public WorkflowDocumenterControl()
        : this(new DataverseWorkflowProvider())
    {
    }

    internal WorkflowDocumenterControl(IDataverseWorkflowProvider workflowProvider)
    {
        _workflowProvider = workflowProvider;
        _settingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BridgeNexa",
            "WorkflowDocumenter",
            "xrmtoolbox-settings.json");
        _settings = LoadSettings(_settingsFilePath);

        Dock = DockStyle.Fill;

        BackColor = Color.FromArgb(244, 246, 250);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var topPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
            Padding = new Padding(12)
        };
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182));

        var filterPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            BackColor = Color.White
        };
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        filterPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        filterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var searchHeader = new Label
        {
            AutoSize = true,
            Text = "SEARCH WORKFLOWS",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(87, 96, 115),
            Margin = new Padding(0, 0, 0, 6)
        };
        filterPanel.Controls.Add(searchHeader, 0, 0);

        var categoryHeader = new Label
        {
            AutoSize = true,
            Text = "FILTER BY CATEGORY",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(87, 96, 115),
            Margin = new Padding(10, 0, 0, 6)
        };
        filterPanel.Controls.Add(categoryHeader, 1, 0);

        var searchPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = Color.White
        };

        _searchBox = new TextBox
        {
            Width = 330,
            Font = new Font("Segoe UI", 9f),
            BorderStyle = BorderStyle.FixedSingle,
            Text = string.Empty
        };
        _searchBox.HandleCreated += (_, _) => NativeMethods.SetCueBanner(_searchBox, "Filter by name or entity...");
        _searchBox.TextChanged += (_, _) => ApplyFilters();
        searchPanel.Controls.Add(_searchBox);
        _searchBox.Location = new Point(0, 4);
        filterPanel.Controls.Add(searchPanel, 0, 1);

        var chipPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Margin = new Padding(10, 0, 0, 0),
            BackColor = Color.White
        };

        _categoryChips = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase)
        {
            [WorkflowCategory] = CreateCategoryChip(WorkflowCategory, Color.FromArgb(45, 116, 245)),
            [DialogCategory] = CreateCategoryChip(DialogCategory, Color.FromArgb(143, 52, 233)),
            [ActionCategory] = CreateCategoryChip(ActionCategory, Color.FromArgb(2, 166, 110))
        };

        foreach (var chip in _categoryChips.Values)
        {
            chip.CheckedChanged += (_, _) => ApplyFilters();
            chipPanel.Controls.Add(chip);
        }

        filterPanel.Controls.Add(chipPanel, 1, 1);

        var actionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
            Margin = new Padding(14, 0, 0, 0)
        };
        actionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _refreshButton = CreateActionButton("Load Workflows", false);
        _refreshButton.Click += (_, _) => ExecuteMethod(LoadCatalog);
        actionPanel.Controls.Add(_refreshButton, 0, 0);

        _selectAllButton = CreateActionButton("Select All Found", false);
        _selectAllButton.Click += (_, _) => SetAllSelections(true);
        actionPanel.Controls.Add(_selectAllButton, 0, 1);

        _selectVisibleButton = CreateActionButton("Select Visible", false);
        _selectVisibleButton.Click += (_, _) => SetVisibleSelections(true);
        actionPanel.Controls.Add(_selectVisibleButton, 0, 2);

        _clearSelectionButton = CreateActionButton("Clear All", false);
        _clearSelectionButton.Click += (_, _) => SetAllSelections(false);
        actionPanel.Controls.Add(_clearSelectionButton, 0, 3);

        _settingsButton = CreateActionButton("Settings", false);
        _settingsButton.Click += (_, _) => OpenSettingsDialog();
        actionPanel.Controls.Add(_settingsButton, 0, 4);

        _exportButton = CreateActionButton("Generate Docs", true);
        _exportButton.Click += (_, _) => ExecuteMethod(ExportSelectedWorkflows);
        actionPanel.Controls.Add(_exportButton, 0, 5);

        topPanel.Controls.Add(filterPanel, 0, 0);
        topPanel.Controls.Add(actionPanel, 1, 0);

        _workflowList = new ListView
        {
            CheckBoxes = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            View = View.Details,
            Font = new Font("Segoe UI", 9f),
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        _workflowList.Columns.Add("Workflow Name", 290);
        _workflowList.Columns.Add("Category", 96);
        _workflowList.Columns.Add("Primary Entity", 120);
        _workflowList.Columns.Add("Mode", 95);
        _workflowList.Columns.Add("Scope", 180);
        _workflowList.Columns.Add("State", 90);
        _workflowList.Columns.Add("Trigger", 320);
        _workflowList.ItemChecked += (_, args) => HandleWorkflowItemChecked(args.Item);

        var footerPanel = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(235, 240, 248),
            Padding = new Padding(8, 6, 8, 6)
        };
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _summaryLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(46, 62, 90),
            Font = new Font("Segoe UI", 9f),
            Text = "Showing 0 of 0 workflows"
        };

        _timestampLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            ForeColor = Color.FromArgb(80, 93, 116),
            Font = new Font("Segoe UI", 8.5f),
            Text = "Last updated: --"
        };

        footerPanel.Controls.Add(_summaryLabel, 0, 0);
        footerPanel.Controls.Add(_timestampLabel, 1, 0);

        layout.Controls.Add(topPanel, 0, 0);
        layout.Controls.Add(_workflowList, 0, 1);
        layout.Controls.Add(footerPanel, 0, 2);
        Controls.Add(layout);

        UpdateCategoryChipCounts();
    }

    private static Button CreateActionButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Width = 150,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = primary ? Color.FromArgb(132, 137, 255) : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(44, 55, 75)
        };

        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(132, 137, 255) : Color.FromArgb(208, 214, 226);
        return button;
    }

    private static CheckBox CreateCategoryChip(string name, Color color)
    {
        var chip = new CheckBox
        {
            Appearance = Appearance.Button,
            AutoSize = false,
            Width = 120,
            Height = 46,
            Text = name,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat,
            Checked = true,
            ForeColor = Color.White,
            BackColor = color,
            Margin = new Padding(0, 4, 10, 0),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        chip.FlatAppearance.BorderSize = 0;
        chip.FlatAppearance.CheckedBackColor = color;
        chip.FlatAppearance.MouseDownBackColor = color;
        chip.FlatAppearance.MouseOverBackColor = Color.FromArgb(
            Math.Max(color.R - 10, 0),
            Math.Max(color.G - 10, 0),
            Math.Max(color.B - 10, 0));

        return chip;
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
                _selectedWorkflowIds.IntersectWith(_catalogItems.Select(item => item.WorkflowId));
                _timestampLabel.Text = $"Last updated: {DateTime.Now:HH:mm:ss}";
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
        var selectedIds = _selectedWorkflowIds.ToArray();

        if (selectedIds.Length == 0)
        {
            MessageBox.Show(this, "Select one or more workflows before generating documentation.", "Generate documents", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var outputFolder = _settings.OutputFolder?.Trim();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose an output folder for workflow documentation."
            };

            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }

            outputFolder = dialog.SelectedPath;
            _settings = _settings with { OutputFolder = outputFolder };
            SaveSettings(_settingsFilePath, _settings);
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
                    args.Result = new WorkflowExportResult(definitionResult, null, outputFolder!, null);
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
                    workerResult = InvokeCliWorker(requestPath, outputFolder!, _settings);
                }
                finally
                {
                    TryDeleteFile(requestPath);
                }

                args.Result = new WorkflowExportResult(definitionResult, workerResult, outputFolder!, requestPath);
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
                var message = _settings.OutputMode == OutputMode.SingleDocument
                    ? $"Generated a combined full-detail document for {selectedIds.Length} selected workflow(s) in:{Environment.NewLine}{result.WorkerResult.Payload.OutputFolder}{Environment.NewLine}{Environment.NewLine}File:{Environment.NewLine}{result.WorkerResult.Payload.CombinedFullDetailDocxFile ?? "combined-full-detail.docx"}"
                    : $"Generated {workflowFileCount} workflow documents and an overview document in:{Environment.NewLine}{result.WorkerResult.Payload.OutputFolder}";

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

    private static WorkerInvocationResult InvokeCliWorker(string requestPath, string outputFolder, WorkflowPluginSettings settings)
    {
        var cliPath = ResolveCliPath();
        var startInfo = BuildStartInfo(cliPath, requestPath, outputFolder, settings);

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
                CombinedFullDetailDocxFile: null,
                Warnings: Array.Empty<CliWarning>(),
                Error: string.IsNullOrWhiteSpace(stdErr) ? null : stdErr);
        }

        return new WorkerInvocationResult(payload, process.ExitCode, stdOut, stdErr);
    }

    private static ProcessStartInfo BuildStartInfo(string cliPath, string requestPath, string outputFolder, WorkflowPluginSettings settings)
    {
        var diagramDetail = settings.DiagramDetail == DiagramDetail.Standard ? "standard" : "detailed";
        var narrativeTone = settings.NarrativeTone == NarrativeTone.Technical ? "technical" : "business";
        var outputMode = settings.OutputMode == OutputMode.SingleDocument ? "single" : "per-workflow";

        var argumentBuilder = new StringBuilder();
        argumentBuilder.Append("docx-live ");
        argumentBuilder.Append('"').Append(requestPath).Append('"').Append(' ');
        argumentBuilder.Append('"').Append(outputFolder).Append('"');
        argumentBuilder.Append(" --diagram-detail ").Append(diagramDetail);
        argumentBuilder.Append(" --narrative-tone ").Append(narrativeTone);
        argumentBuilder.Append(" --output-mode ").Append(outputMode);
        var arguments = argumentBuilder.ToString();

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
        var embeddedCliPath = TryResolveEmbeddedCliPath();
        if (!string.IsNullOrWhiteSpace(embeddedCliPath) && File.Exists(embeddedCliPath))
        {
            return embeddedCliPath!;
        }

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

    private static string? TryResolveEmbeddedCliPath()
    {
        const string bundleResourceName = "BN.WorkflowDoc.XrmToolBox.CliRuntime.zip";
        var assembly = typeof(WorkflowDocumenterControl).Assembly;
        Stream? stream = assembly.GetManifestResourceStream(bundleResourceName);

        if (stream == null)
        {
            var assemblyDirectory = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
            var sidecarPath = Path.Combine(assemblyDirectory, "BN.WorkflowDoc.Cli.runtime.zip");
            if (File.Exists(sidecarPath))
            {
                stream = File.OpenRead(sidecarPath);
            }
        }

        if (stream == null)
        {
            return null;
        }

        using (stream)
        {
            var pluginVersion = assembly.GetName().Version?.ToString() ?? "unknown";
            var runtimeRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BridgeNexa",
                "WorkflowDocumenter",
                "cli-runtime",
                pluginVersion);
            var markerPath = Path.Combine(runtimeRoot, ".ready");

            if (!File.Exists(markerPath))
            {
                Directory.CreateDirectory(runtimeRoot);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                foreach (var entry in archive.Entries)
                {
                    var destinationPath = Path.Combine(runtimeRoot, entry.FullName);
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);

                    if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    using var entryStream = entry.Open();
                    using var fileStream = File.Create(destinationPath);
                    entryStream.CopyTo(fileStream);
                }

                File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            }

            var dllPath = Path.Combine(runtimeRoot, "BN.WorkflowDoc.Cli.dll");
            if (File.Exists(dllPath))
            {
                return dllPath;
            }

            var exePath = Path.Combine(runtimeRoot, "BN.WorkflowDoc.Cli.exe");
            return File.Exists(exePath) ? exePath : null;
        }
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
        yield return Path.Combine(root, "cli", "BN.WorkflowDoc.Cli.dll");
        yield return Path.Combine(root, "cli", "BN.WorkflowDoc.Cli.exe");
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
        var selectedCategories = _categoryChips
            .Where(pair => pair.Value.Checked)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var search = _searchBox.Text?.Trim() ?? string.Empty;

        var filtered = _catalogItems.Where(item =>
                (selectedCategories.Count == 0 || selectedCategories.Contains(item.Category))
                && (search.Length == 0
                    || item.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || item.PrimaryEntity.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || item.TriggerSummary.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray();

        _filteredItemCount = filtered.Length;
        UpdateCategoryChipCounts();

        _workflowList.BeginUpdate();
        _suppressSelectionEvents = true;
        _workflowList.Items.Clear();
        foreach (var item in filtered)
        {
            var row = new ListViewItem(item.DisplayName)
            {
                Tag = item,
                UseItemStyleForSubItems = false
            };
            row.Checked = _selectedWorkflowIds.Contains(item.WorkflowId);
            row.SubItems.Add(item.Category);
            row.SubItems.Add(item.PrimaryEntity);
            row.SubItems.Add(item.ExecutionMode);
            row.SubItems.Add(item.Scope);
            row.SubItems.Add(item.State);
            row.SubItems.Add(item.TriggerSummary);

            ApplyRowVisuals(row, _workflowList.Items.Count);
            _workflowList.Items.Add(row);
        }
        _suppressSelectionEvents = false;
        _workflowList.EndUpdate();

        UpdateSummary();
    }

    private void HandleWorkflowItemChecked(ListViewItem item)
    {
        if (_suppressSelectionEvents || item?.Tag is not WorkflowCatalogItem workflow)
        {
            return;
        }

        if (item.Checked)
        {
            _selectedWorkflowIds.Add(workflow.WorkflowId);
        }
        else
        {
            _selectedWorkflowIds.Remove(workflow.WorkflowId);
        }

        UpdateSummary();
    }

    private void SetAllSelections(bool isChecked)
    {
        SetSelections(isChecked, _catalogItems);
    }

    private void SetVisibleSelections(bool isChecked)
    {
        var visibleItems = _workflowList.Items
            .Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<WorkflowCatalogItem>()
            .ToArray();

        SetSelections(isChecked, visibleItems);
    }

    private void SetSelections(bool isChecked, IEnumerable<WorkflowCatalogItem> items)
    {
        var workflowIds = items
            .Select(item => item.WorkflowId)
            .Distinct()
            .ToArray();

        if (isChecked)
        {
            foreach (var workflowId in workflowIds)
            {
                _selectedWorkflowIds.Add(workflowId);
            }
        }
        else
        {
            foreach (var workflowId in workflowIds)
            {
                _selectedWorkflowIds.Remove(workflowId);
            }
        }

        ApplyFilters();
    }

    private void UpdateSummary()
    {
        var visibleSelectedCount = _workflowList.Items
            .Cast<ListViewItem>()
            .Count(item => item.Checked);

        _summaryLabel.Text = $"Showing {_filteredItemCount} of {_catalogItems.Count} workflows | Selected {_selectedWorkflowIds.Count} total, {visibleSelectedCount} visible";
    }

    private void UpdateCategoryChipCounts()
    {
        if (_categoryChips.Count == 0)
        {
            return;
        }

        var counts = _catalogItems
            .GroupBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _categoryChips)
        {
            counts.TryGetValue(pair.Key, out var count);
            pair.Value.Text = $"{pair.Key}{Environment.NewLine}{count} items";
            pair.Value.ForeColor = pair.Value.Checked ? Color.White : Color.FromArgb(65, 72, 91);
            pair.Value.BackColor = pair.Value.Checked
                ? pair.Value.FlatAppearance.CheckedBackColor
                : Color.FromArgb(236, 239, 246);
            pair.Value.FlatAppearance.BorderSize = pair.Value.Checked ? 0 : 1;
            pair.Value.FlatAppearance.BorderColor = Color.FromArgb(205, 212, 226);
        }
    }

    private static void ApplyRowVisuals(ListViewItem row, int index)
    {
        row.BackColor = index % 2 == 0
            ? Color.White
            : Color.FromArgb(248, 250, 254);
        row.ForeColor = Color.FromArgb(40, 50, 68);

        if (row.SubItems.Count > 3)
        {
            var modeText = row.SubItems[3].Text ?? string.Empty;
            row.SubItems[3].ForeColor = modeText.IndexOf("Sync", StringComparison.OrdinalIgnoreCase) >= 0
                ? Color.FromArgb(39, 118, 245)
                : Color.FromArgb(209, 118, 14);
        }

        if (row.SubItems.Count > 5)
        {
            var stateText = row.SubItems[5].Text ?? string.Empty;
            row.SubItems[5].ForeColor = stateText.IndexOf("Activated", StringComparison.OrdinalIgnoreCase) >= 0
                ? Color.FromArgb(0, 153, 92)
                : Color.FromArgb(120, 126, 140);
        }
    }

    private static class NativeMethods
    {
        private const int EmSetCueBanner = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void SetCueBanner(TextBox textBox, string cueText)
        {
            if (textBox.IsHandleCreated)
            {
                SendMessage(textBox.Handle, EmSetCueBanner, (IntPtr)1, cueText);
            }
        }
    }

    private void OpenSettingsDialog()
    {
        using var dialog = new WorkflowSettingsDialog(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings = dialog.Settings;
        SaveSettings(_settingsFilePath, _settings);
    }

    private static WorkflowPluginSettings LoadSettings(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new WorkflowPluginSettings();
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<WorkflowPluginSettings>(json, JsonOptions);
            return loaded ?? new WorkflowPluginSettings();
        }
        catch
        {
            return new WorkflowPluginSettings();
        }
    }

    private static void SaveSettings(string path, WorkflowPluginSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Settings persistence should not block plugin usage.
        }
    }

    private sealed class WorkflowSettingsDialog : Form
    {
        private readonly TextBox _outputPathTextBox;
        private readonly ComboBox _narrativeToneCombo;
        private readonly ComboBox _diagramDetailCombo;
        private readonly ComboBox _outputModeCombo;

        public WorkflowSettingsDialog(WorkflowPluginSettings settings)
        {
            Text = "Workflow Documenter Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(620, 240);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 5,
                Padding = new Padding(12)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(new Label { Text = "Output folder", AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 0);
            _outputPathTextBox = new TextBox { Dock = DockStyle.Fill, Text = settings.OutputFolder ?? string.Empty };
            layout.Controls.Add(_outputPathTextBox, 1, 0);

            var browseButton = new Button { AutoSize = true, Text = "Browse..." };
            browseButton.Click += (_, _) =>
            {
                using var folderDialog = new FolderBrowserDialog
                {
                    Description = "Choose a default output folder for workflow documentation."
                };

                if (folderDialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
                {
                    _outputPathTextBox.Text = folderDialog.SelectedPath;
                }
            };
            layout.Controls.Add(browseButton, 2, 0);

            layout.Controls.Add(new Label { Text = "Narrative style", AutoSize = true, Margin = new Padding(0, 12, 8, 0) }, 0, 1);
            _narrativeToneCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Left,
                Width = 220
            };
            _narrativeToneCombo.Items.Add("Business (non-technical)");
            _narrativeToneCombo.Items.Add("Technical");
            _narrativeToneCombo.SelectedIndex = settings.NarrativeTone == NarrativeTone.Technical ? 1 : 0;
            layout.Controls.Add(_narrativeToneCombo, 1, 1);

            layout.Controls.Add(new Label { Text = "Diagram detail", AutoSize = true, Margin = new Padding(0, 12, 8, 0) }, 0, 2);
            _diagramDetailCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Left,
                Width = 220
            };
            _diagramDetailCombo.Items.Add("Detailed");
            _diagramDetailCombo.Items.Add("Standard (less detailed)");
            _diagramDetailCombo.SelectedIndex = settings.DiagramDetail == DiagramDetail.Standard ? 1 : 0;
            layout.Controls.Add(_diagramDetailCombo, 1, 2);

            layout.Controls.Add(new Label { Text = "Output documents", AutoSize = true, Margin = new Padding(0, 12, 8, 0) }, 0, 3);
            _outputModeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Left,
                Width = 320
            };
            _outputModeCombo.Items.Add("Per-workflow documents + overview");
            _outputModeCombo.Items.Add("Single combined full-detail document");
            _outputModeCombo.SelectedIndex = settings.OutputMode == OutputMode.SingleDocument ? 1 : 0;
            layout.Controls.Add(_outputModeCombo, 1, 3);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 16, 0, 0)
            };

            var okButton = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
            var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(okButton);
            buttons.Controls.Add(cancelButton);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 3);

            AcceptButton = okButton;
            CancelButton = cancelButton;
            Controls.Add(layout);
        }

        public WorkflowPluginSettings Settings => new(
            OutputFolder: string.IsNullOrWhiteSpace(_outputPathTextBox.Text) ? null : _outputPathTextBox.Text.Trim(),
            NarrativeTone: _narrativeToneCombo.SelectedIndex == 1 ? NarrativeTone.Technical : NarrativeTone.Business,
            DiagramDetail: _diagramDetailCombo.SelectedIndex == 1 ? DiagramDetail.Standard : DiagramDetail.Detailed,
            OutputMode: _outputModeCombo.SelectedIndex == 1 ? OutputMode.SingleDocument : OutputMode.PerWorkflowAndOverview);
    }

    private enum NarrativeTone
    {
        Business,
        Technical
    }

    private enum DiagramDetail
    {
        Detailed,
        Standard
    }

    private enum OutputMode
    {
        PerWorkflowAndOverview,
        SingleDocument
    }

    private sealed record WorkflowPluginSettings(
        string? OutputFolder = null,
        NarrativeTone NarrativeTone = NarrativeTone.Business,
        DiagramDetail DiagramDetail = DiagramDetail.Detailed,
        OutputMode OutputMode = OutputMode.PerWorkflowAndOverview);

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
