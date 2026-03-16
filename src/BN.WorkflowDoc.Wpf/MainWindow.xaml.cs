using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using BN.WorkflowDoc.Core.Parsing;
using Microsoft.Win32;

namespace BN.WorkflowDoc.Wpf;

public partial class MainWindow : Window
{
    private bool _isUiInitialized;
    private CancellationTokenSource? _cts;
    private string? _lastOutputFolder;
    private string? _lastArchivePath;
    private List<string> _warningSummary = [];
    private List<string> _warningDetails = [];
    private bool _showWarningDetails;
    private readonly List<string> _batchZipPaths = [];

    public MainWindow()
    {
        InitializeComponent();
        OutputFolderBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WorkflowDocs");
        RefreshRunModeUi();
        RefreshGenerateButtonState();
        _isUiInitialized = true;
    }

    // ── File browse handlers ──────────────────────────────────────────────────

    private void BrowseZip_Click(object sender, RoutedEventArgs e)
    {
        if (BatchRunRadio.IsChecked == true)
        {
            AddBatchZips();
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Select D365 Solution Package",
            Filter = "Solution ZIP (*.zip)|*.zip|All Files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true)
        {
            SetZipFile(dlg.FileName);
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Output Folder" };
        if (dlg.ShowDialog() == true)
        {
            OutputFolderBox.Text = dlg.FolderName;
        }
    }

    // ── Drag-and-drop ─────────────────────────────────────────────────────────

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsZipDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (TryGetZipsFromDrop(e, out var paths))
        {
            HandleDroppedZips(paths!);
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        var valid = IsZipDrop(e);
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        DropZoneBorder.BorderBrush = valid
            ? new SolidColorBrush(Color.FromRgb(138, 75, 255))
            : new SolidColorBrush(Color.FromRgb(203, 211, 226));
        DropZoneBorder.Background = valid
            ? new SolidColorBrush(Color.FromRgb(244, 238, 255))
            : new SolidColorBrush(Color.FromRgb(248, 250, 253));
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        ResetDropZoneStyle();
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        ResetDropZoneStyle();
        if (TryGetZipsFromDrop(e, out var paths))
        {
            HandleDroppedZips(paths!);
        }
    }

    private static bool IsZipDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        return files?.Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool TryGetZipsFromDrop(DragEventArgs e, out IReadOnlyList<string>? paths)
    {
        paths = null;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        var zipFiles = files?
            .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (zipFiles is null || zipFiles.Length == 0)
        {
            return false;
        }

        paths = zipFiles;
        return true;
    }

    private void ResetDropZoneStyle()
    {
        DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 211, 226));
        DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(248, 250, 253));
    }

    // ── ZIP selection ─────────────────────────────────────────────────────────

    private void SetZipFile(string path)
    {
        ZipPathBox.Text = path;
        ZipPathBox.Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39));

        // Auto-derive output folder from solution name
        var baseName = ArtifactPathNaming.SanitizeFileName(Path.GetFileNameWithoutExtension(path), "solution");
        OutputFolderBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WorkflowDocs", baseName);

        RefreshGenerateButtonState();
        HideResults();
    }

    private void AddBatchZips_Click(object sender, RoutedEventArgs e)
    {
        AddBatchZips();
    }

    private void ClearBatchZips_Click(object sender, RoutedEventArgs e)
    {
        _batchZipPaths.Clear();
        RefreshBatchList();
        RefreshGenerateButtonState();
        HideResults();
    }

    private void RunMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isUiInitialized)
        {
            return;
        }

        RefreshRunModeUi();
        RefreshGenerateButtonState();
        HideResults();
    }

    // ── Generate ──────────────────────────────────────────────────────────────

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        var zipPath = ZipPathBox.Text;
        var outputFolder = OutputFolderBox.Text.Trim();
        var packMode = ModePackRadio.IsChecked == true;
        var batchMode = BatchRunRadio.IsChecked == true;
        var narrativeTone = NarrativeToneBox.SelectedIndex == 1
            ? DocumentNarrativeTone.Technical
            : DocumentNarrativeTone.Business;
        var diagramDetailLevel = DiagramDetailBox.SelectedIndex == 0
            ? DiagramDetailLevel.Standard
            : DiagramDetailLevel.Detailed;

        if (!batchMode && !File.Exists(zipPath))
        {
            ShowError("The selected ZIP file does not exist. Please choose a valid solution package.");
            return;
        }

        if (batchMode && _batchZipPaths.Count == 0)
        {
            ShowError("Select at least one ZIP file for batch mode.");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            ShowError("Please specify an output folder.");
            return;
        }

        HideResults();
        SetUiRunning(true);
        _cts = new CancellationTokenSource();
        var progressStopwatch = Stopwatch.StartNew();
        var progress = new Progress<ProgressUpdate>(update =>
        {
            StatusText.Text = update.Stage;
            ProgressDetailText.Text = update.Detail;
            MainProgress.IsIndeterminate = update.IsIndeterminate;
            if (!update.IsIndeterminate)
            {
                MainProgress.Value = Math.Clamp(update.Percent, 0, 100);
            }

            var elapsed = progressStopwatch.Elapsed;
            if (!update.IsIndeterminate && update.Percent > 0.01)
            {
                var remaining = TimeSpan.FromSeconds(
                    Math.Max(0, elapsed.TotalSeconds * ((100d - update.Percent) / update.Percent)));
                ProgressTimeText.Text = $"Elapsed: {MainWindowFormatting.FormatDuration(elapsed)} | ETA: {MainWindowFormatting.FormatDuration(remaining)}";
            }
            else
            {
                ProgressTimeText.Text = $"Elapsed: {MainWindowFormatting.FormatDuration(elapsed)}";
            }
        });

        try
        {
            var result = batchMode
                ? await Task.Run(async () =>
                    await RunBatchGenerationAsync(_batchZipPaths, outputFolder, packMode, narrativeTone, diagramDetailLevel, progress, _cts.Token))
                : await Task.Run(async () =>
                    await RunGenerationAsync(zipPath, outputFolder, packMode, narrativeTone, diagramDetailLevel, progress, _cts.Token));
            ShowResults(result);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            ShowError($"Unexpected error: {ex.Message}");
        }
        finally
        {
            progressStopwatch.Stop();
            SetUiRunning(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    // ── Core pipeline ─────────────────────────────────────────────────────────

    private static async Task<GenerationResult> RunGenerationAsync(
        string zipPath,
        string outputFolder,
        bool packMode,
        DocumentNarrativeTone narrativeTone,
        DiagramDetailLevel diagramDetailLevel,
        IProgress<ProgressUpdate> progress,
        CancellationToken ct)
    {
        var reader = new SolutionPackageReader();
        var parser = new WorkflowDefinitionParser();
        var extractionPipeline = new WorkflowExtractionPipeline(reader, parser);
        var workflowBuilder = new DeterministicWorkflowDocumentBuilder();
        var overviewBuilder = new DeterministicOverviewDocumentBuilder();
        var docPipeline = new WorkflowDocumentationPipeline(
            extractionPipeline, workflowBuilder, overviewBuilder);

        progress.Report(new ProgressUpdate(
            Stage: "Parsing solution XML and workflow definitions...",
            Detail: "Step 1/4",
            Percent: 0,
            IsIndeterminate: true));
        var docResult = await docPipeline.GenerateAsync(zipPath, ct).ConfigureAwait(false);

        if (docResult.Status == ProcessingStatus.Failed || docResult.Value is null)
        {
            return GenerationResult.Failure(
                docResult.ErrorMessage ?? "Solution parsing failed. No workflows could be extracted.",
                outputFolder,
                docResult.Warnings.Select(FormatWarning).ToList());
        }

        progress.Report(new ProgressUpdate(
            Stage: $"Building DOCX for {docResult.Value.WorkflowDocuments.Count} workflow(s)...",
            Detail: "Step 2/4",
            Percent: 0,
            IsIndeterminate: true));
        Directory.CreateDirectory(outputFolder);

        var docxWriter = new OpenXmlDocxArtifactWriter(new DeterministicPngDiagramRenderer(diagramDetailLevel), narrativeTone);
        var docxResult = await docxWriter.WriteAsync(docResult.Value, outputFolder, ct).ConfigureAwait(false);

        if (docxResult.Status == ProcessingStatus.Failed || docxResult.Value is null)
        {
            return GenerationResult.Failure(
                docxResult.ErrorMessage ?? "DOCX generation failed.",
                outputFolder,
                docxResult.Warnings.Select(FormatWarning).ToList());
        }

        // SVG diagram export
        progress.Report(new ProgressUpdate(
            Stage: "Rendering SVG diagrams...",
            Detail: "Step 3/4",
            Percent: 0,
            IsIndeterminate: true));
        var svgRenderer = new DeterministicSvgDiagramRenderer(diagramDetailLevel);
        var diagFolder = Path.Combine(outputFolder, "diagrams");
        Directory.CreateDirectory(diagFolder);

        for (var i = 0; i < docResult.Value.WorkflowDocuments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var wfDoc = docResult.Value.WorkflowDocuments[i];
            var renderResult = await svgRenderer.RenderAsync(wfDoc.Diagrams, ct).ConfigureAwait(false);
            if (renderResult.Value is not null)
            {
                var slug = ArtifactPathNaming.SanitizeFileName(wfDoc.WorkflowName, "workflow");
                foreach (var asset in renderResult.Value)
                {
                    var assetPath = Path.Combine(diagFolder, $"{i + 1:D3}-{slug}-{asset.FileName}");
                    await File.WriteAllBytesAsync(assetPath, asset.Content, ct).ConfigureAwait(false);
                }
            }
        }

        // Bundle manifest
        progress.Report(new ProgressUpdate(
            Stage: "Writing bundle manifest...",
            Detail: "Step 4/4",
            Percent: 0,
            IsIndeterminate: true));
        var manifestPath = Path.Combine(outputFolder, "bundle-manifest.json");
        await File.WriteAllTextAsync(manifestPath,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                mode = packMode ? "pack" : "docx",
                status = "Success",
                input = zipPath,
                outputFolder,
                generatedAt = DateTime.UtcNow.ToString("O"),
                workflowCount = docResult.Value.WorkflowDocuments.Count
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct)
            .ConfigureAwait(false);

        // Build file list for results panel
        var files = new List<GeneratedFileItem>
        {
            new("📋", "overview.docx", docxResult.Value.OverviewFile)
        };
        foreach (var wfFile in docxResult.Value.WorkflowFiles)
        {
            files.Add(new("📄", Path.GetFileName(wfFile), wfFile));
        }
        files.Add(new("📋", "bundle-manifest.json", manifestPath));

        // Archive if pack mode
        string? archivePath = null;
        if (packMode)
        {
            progress.Report(new ProgressUpdate(
                Stage: "Creating ZIP archive...",
                Detail: "Step 5/5",
                Percent: 100,
                IsIndeterminate: true));
            archivePath = outputFolder.TrimEnd(Path.DirectorySeparatorChar,
                                                Path.AltDirectorySeparatorChar) + ".zip";
            if (File.Exists(archivePath)) File.Delete(archivePath);
            ZipFile.CreateFromDirectory(outputFolder, archivePath);
            files.Add(new("🗜", Path.GetFileName(archivePath), archivePath));
        }

        var allWarnings = docResult.Warnings
            .Concat(docxResult.Warnings)
            .Select(FormatWarning)
            .ToList();

        return new GenerationResult(
            Success: true,
            IsBatch: false,
            OutputFolder: outputFolder,
            WorkflowCount: docResult.Value.WorkflowDocuments.Count,
            Files: files,
            BatchSolutions: [],
            ArchivePath: archivePath,
            Warnings: allWarnings,
            Error: null);
    }

    private static async Task<GenerationResult> RunBatchGenerationAsync(
        IReadOnlyList<string> zipPaths,
        string outputFolder,
        bool packMode,
        DocumentNarrativeTone narrativeTone,
        DiagramDetailLevel diagramDetailLevel,
        IProgress<ProgressUpdate> progress,
        CancellationToken ct)
    {
        var reader = new SolutionPackageReader();
        var parser = new WorkflowDefinitionParser();
        var extractionPipeline = new WorkflowExtractionPipeline(reader, parser);
        var workflowBuilder = new DeterministicWorkflowDocumentBuilder();
        var overviewBuilder = new DeterministicOverviewDocumentBuilder();
        var docPipeline = new WorkflowDocumentationPipeline(
            extractionPipeline, workflowBuilder, overviewBuilder);
        var portfolioPipeline = new PortfolioDocumentationPipeline(docPipeline);

        var totalSteps = (zipPaths.Count * 2) + (packMode ? 4 : 3);
        var currentStep = 1;

        progress.Report(new ProgressUpdate(
            Stage: $"Processing {zipPaths.Count} solution ZIP file(s)...",
            Detail: BuildStepDetail(currentStep, totalSteps),
            Percent: CalculatePercent(currentStep, totalSteps),
            IsIndeterminate: false));
        var batchResult = await portfolioPipeline.GenerateAsync(zipPaths, ct).ConfigureAwait(false);

        if (batchResult.Status == ProcessingStatus.Failed || batchResult.Value is null)
        {
            return GenerationResult.Failure(
                batchResult.ErrorMessage ?? "Batch processing failed.",
                outputFolder,
                batchResult.Warnings.Select(FormatWarning).ToList());
        }

        Directory.CreateDirectory(outputFolder);
        var docxWriter = new OpenXmlDocxArtifactWriter(new DeterministicPngDiagramRenderer(diagramDetailLevel), narrativeTone);
        var files = new List<GeneratedFileItem>();
        var batchSolutions = new List<BatchSolutionResultItem>(batchResult.Value.Results.Count);
        var warnings = new List<string>(batchResult.Warnings.Select(FormatWarning));
        var totalWorkflows = 0;

        for (var i = 0; i < batchResult.Value.Results.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = batchResult.Value.Results[i];
            currentStep++;
            progress.Report(new ProgressUpdate(
                Stage: $"Preparing solution {i + 1}/{batchResult.Value.Results.Count}: {Path.GetFileName(item.InputPath)}",
                Detail: BuildStepDetail(currentStep, totalSteps),
                Percent: CalculatePercent(currentStep, totalSteps),
                IsIndeterminate: false));
            if (item.WorkflowDocuments is null || item.OverviewDocument is null)
            {
                batchSolutions.Add(new BatchSolutionResultItem(
                    SolutionName: Path.GetFileNameWithoutExtension(item.InputPath),
                    Status: item.Status.ToString(),
                    WorkflowCount: 0,
                    WarningCount: item.Warnings.Count,
                    Duration: item.Duration,
                    OutputFolder: null,
                    SummaryLine: MainWindowFormatting.BuildBatchSummaryLine(item.Status.ToString(), 0, item.Warnings.Count, item.Duration)));
                continue;
            }

            currentStep++;
            progress.Report(new ProgressUpdate(
                Stage: $"Writing solution {i + 1}/{batchResult.Value.Results.Count}: {item.OverviewDocument.SolutionName}",
                Detail: BuildStepDetail(currentStep, totalSteps),
                Percent: CalculatePercent(currentStep, totalSteps),
                IsIndeterminate: false));
            var solutionFolder = Path.Combine(
                outputFolder,
                ArtifactPathNaming.BuildSolutionFolderName(i + 1, item.OverviewDocument.SolutionName));
            Directory.CreateDirectory(solutionFolder);

            var generation = new DocumentationGenerationResult(
                Package: new SolutionPackage(item.InputPath, solutionFolder, "unknown", Array.Empty<WorkflowDefinition>(), item.Warnings),
                WorkflowDocuments: item.WorkflowDocuments,
                OverviewDocument: item.OverviewDocument,
                Warnings: item.Warnings);

            var writeResult = await docxWriter.WriteAsync(generation, solutionFolder, ct).ConfigureAwait(false);
            warnings.AddRange(writeResult.Warnings.Select(FormatWarning));

            if (writeResult.Value is null)
            {
                batchSolutions.Add(new BatchSolutionResultItem(
                    SolutionName: item.OverviewDocument.SolutionName,
                    Status: writeResult.Status.ToString(),
                    WorkflowCount: item.WorkflowDocuments.Count,
                    WarningCount: item.Warnings.Count + writeResult.Warnings.Count,
                    Duration: item.Duration,
                    OutputFolder: solutionFolder,
                    SummaryLine: MainWindowFormatting.BuildBatchSummaryLine(
                        writeResult.Status.ToString(),
                        item.WorkflowDocuments.Count,
                        item.Warnings.Count + writeResult.Warnings.Count,
                        item.Duration)));
                continue;
            }

            totalWorkflows += item.WorkflowDocuments.Count;
            files.Add(new GeneratedFileItem("📋", $"{item.OverviewDocument.SolutionName} - overview.docx", writeResult.Value.OverviewFile));

            foreach (var wfFile in writeResult.Value.WorkflowFiles)
            {
                files.Add(new GeneratedFileItem("📄", Path.GetFileName(wfFile), wfFile));
            }

            batchSolutions.Add(new BatchSolutionResultItem(
                SolutionName: item.OverviewDocument.SolutionName,
                Status: writeResult.Status.ToString(),
                WorkflowCount: item.WorkflowDocuments.Count,
                WarningCount: item.Warnings.Count + writeResult.Warnings.Count,
                Duration: item.Duration,
                OutputFolder: solutionFolder,
                SummaryLine: MainWindowFormatting.BuildBatchSummaryLine(
                    writeResult.Status.ToString(),
                    item.WorkflowDocuments.Count,
                    item.Warnings.Count + writeResult.Warnings.Count,
                    item.Duration)));
        }

        currentStep++;
        progress.Report(new ProgressUpdate(
            Stage: "Writing portfolio summary artifacts...",
            Detail: BuildStepDetail(currentStep, totalSteps),
            Percent: CalculatePercent(currentStep, totalSteps),
            IsIndeterminate: false));
        var summaryPath = Path.Combine(outputFolder, "portfolio-summary.json");
        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(batchResult.Value, new JsonSerializerOptions
        {
            WriteIndented = true
        }), ct).ConfigureAwait(false);
        files.Add(new GeneratedFileItem("📊", "portfolio-summary.json", summaryPath));

        var portfolioOverview = new OverviewDocumentModel(
            SolutionName: "Portfolio Overview",
            Workflows: batchResult.Value.PortfolioSummary.TopRiskWorkflows,
            GlobalWarnings: batchResult.Warnings,
            DependencyGraph: batchResult.Value.PortfolioSummary.CrossSolutionDependencyGraph);

        currentStep++;
        progress.Report(new ProgressUpdate(
            Stage: "Generating portfolio overview DOCX...",
            Detail: BuildStepDetail(currentStep, totalSteps),
            Percent: CalculatePercent(currentStep, totalSteps),
            IsIndeterminate: false));
        var portfolioDocResult = await docxWriter.WriteAsync(
            new DocumentationGenerationResult(
                Package: new SolutionPackage("batch", outputFolder, "unknown", Array.Empty<WorkflowDefinition>(), batchResult.Warnings),
                WorkflowDocuments: Array.Empty<WorkflowDocumentModel>(),
                OverviewDocument: portfolioOverview,
                Warnings: batchResult.Warnings),
            outputFolder,
            ct).ConfigureAwait(false);

        warnings.AddRange(portfolioDocResult.Warnings.Select(FormatWarning));
        if (portfolioDocResult.Value is not null)
        {
            files.Add(new GeneratedFileItem("📋", "portfolio-overview.docx", portfolioDocResult.Value.OverviewFile));
        }

        currentStep++;
        progress.Report(new ProgressUpdate(
            Stage: "Writing portfolio manifest...",
            Detail: BuildStepDetail(currentStep, totalSteps),
            Percent: CalculatePercent(currentStep, totalSteps),
            IsIndeterminate: false));
        var manifestPath = Path.Combine(outputFolder, "portfolio-manifest.json");
        var solutionArtifacts = batchResult.Value.Results.Select((item, index) =>
        {
            var solutionName = item.OverviewDocument?.SolutionName ?? Path.GetFileNameWithoutExtension(item.InputPath);
            var folder = item.OverviewDocument is null
                ? null
                : Path.Combine(outputFolder, ArtifactPathNaming.BuildSolutionFolderName(index + 1, solutionName));

            return new
            {
                inputPath = item.InputPath,
                solutionName,
                status = item.Status.ToString(),
                durationSeconds = item.Duration.TotalSeconds,
                outputFolder = folder,
                workflowCount = item.WorkflowDocuments?.Count ?? 0,
                warningCount = item.Warnings.Count
            };
        }).ToArray();

        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(new
            {
                mode = "batch",
                status = batchResult.Status.ToString(),
                inputCount = zipPaths.Count,
                generatedAt = DateTime.UtcNow.ToString("O"),
                totalWorkflows,
                solutionCount = batchResult.Value.Results.Count,
                warningCount = warnings.Count,
                summary = batchResult.Value.PortfolioSummary,
                solutions = solutionArtifacts
            }, new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);
        files.Add(new GeneratedFileItem("📋", "portfolio-manifest.json", manifestPath));

        string? archivePath = null;
        if (packMode)
        {
            currentStep++;
            progress.Report(new ProgressUpdate(
                Stage: "Creating batch ZIP archive...",
                Detail: BuildStepDetail(currentStep, totalSteps),
                Percent: CalculatePercent(currentStep, totalSteps),
                IsIndeterminate: false));
            archivePath = outputFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".zip";
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            ZipFile.CreateFromDirectory(outputFolder, archivePath);
            files.Add(new GeneratedFileItem("🗜", Path.GetFileName(archivePath), archivePath));
        }

        return new GenerationResult(
            Success: true,
            IsBatch: true,
            OutputFolder: outputFolder,
            WorkflowCount: totalWorkflows,
            Files: files,
            BatchSolutions: batchSolutions,
            ArchivePath: archivePath,
            Warnings: warnings,
            Error: null);
    }

    private static string FormatWarning(ProcessingWarning w) =>
        string.IsNullOrWhiteSpace(w.Source)
            ? $"[{w.Code}] {w.Message}"
            : $"[{w.Code}] {w.Source}: {w.Message}";

    // ── UI state helpers ──────────────────────────────────────────────────────

    private void SetUiRunning(bool running)
    {
        GenerateButton.IsEnabled = !running && CanGenerate();
        CancelButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        ProgressCard.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (running)
        {
            MainProgress.IsIndeterminate = true;
            MainProgress.Value = 0;
            ProgressDetailText.Text = string.Empty;
            ProgressTimeText.Text = "Elapsed: 00:00";
        }
        else
        {
            ProgressTimeText.Text = string.Empty;
        }
    }

    private void HideResults()
    {
        ResultsCard.Visibility = Visibility.Collapsed;
        ErrorCard.Visibility = Visibility.Collapsed;
        WarningsPanel.Visibility = Visibility.Collapsed;
        BatchResultsPanel.Visibility = Visibility.Collapsed;
        BatchResultsList.ItemsSource = null;
        _warningSummary = [];
        _warningDetails = [];
        _showWarningDetails = false;
    }

    private void ShowError(string message)
    {
        ErrorMessageText.Text = message;
        ErrorCard.Visibility = Visibility.Visible;
        ResultsCard.Visibility = Visibility.Collapsed;
    }

    private void ShowResults(GenerationResult result)
    {
        ProgressCard.Visibility = Visibility.Collapsed;

        if (!result.Success)
        {
            ShowError(result.Error ?? "Unknown error.");
            return;
        }

        _lastOutputFolder = result.OutputFolder;
        _lastArchivePath = result.ArchivePath;

        ResultsTitle.Text = result.ArchivePath is not null
            ? $"✔  {result.WorkflowCount} workflow(s) documented — archive ready"
            : $"✔  {result.WorkflowCount} workflow(s) documented";
        ResultsSummary.Text = result.OutputFolder;

        FileList.ItemsSource = result.Files;

        if (result.IsBatch && result.BatchSolutions.Count > 0)
        {
            BatchResultsList.ItemsSource = result.BatchSolutions;
            BatchResultsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            BatchResultsPanel.Visibility = Visibility.Collapsed;
            BatchResultsList.ItemsSource = null;
        }

        OpenArchiveBtn.Visibility = result.ArchivePath is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (result.Warnings.Count > 0)
        {
            WarningsHeader.Text = $"⚠  {result.Warnings.Count} warning(s)";
            _warningDetails = result.Warnings;
            _warningSummary = MainWindowFormatting.BuildWarningSummary(result.Warnings);
            _showWarningDetails = false;
            WarningsToggleBtn.Content = "Show Details";
            WarningsList.ItemsSource = _warningSummary;
            WarningsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            WarningsPanel.Visibility = Visibility.Collapsed;
        }

        ErrorCard.Visibility = Visibility.Collapsed;
        ResultsCard.Visibility = Visibility.Visible;
    }

    private void WarningsToggle_Click(object sender, RoutedEventArgs e)
    {
        _showWarningDetails = !_showWarningDetails;
        WarningsToggleBtn.Content = _showWarningDetails ? "Show Summary" : "Show Details";
        WarningsList.ItemsSource = _showWarningDetails ? _warningDetails : _warningSummary;
    }

    // ── Open file / folder handlers ───────────────────────────────────────────

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_lastOutputFolder is null || !Directory.Exists(_lastOutputFolder)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_lastOutputFolder}\"",
            UseShellExecute = true
        });
    }

    private void OpenArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_lastArchivePath is null || !File.Exists(_lastArchivePath)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_lastArchivePath}\"",
            UseShellExecute = true
        });
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || string.IsNullOrWhiteSpace(path))
            return;
        if (!File.Exists(path)) return;
        if (_lastOutputFolder is not null && !IsPathWithinDirectory(path, _lastOutputFolder)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_lastOutputFolder is not null && !IsPathWithinDirectory(path, _lastOutputFolder))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
            return;
        }

        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private static bool IsPathWithinDirectory(string path, string directory)
    {
        var dir = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddBatchZips()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select D365 Solution Packages",
            Filter = "Solution ZIP (*.zip)|*.zip|All Files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        AddBatchZipFiles(dlg.FileNames);
    }

    private void AddBatchZipFiles(IEnumerable<string> files)
    {
        var added = false;
        foreach (var file in files.Where(x => x.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            if (_batchZipPaths.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _batchZipPaths.Add(file);
            added = true;
        }

        if (!added)
        {
            return;
        }

        _batchZipPaths.Sort(StringComparer.OrdinalIgnoreCase);
        RefreshBatchList();
        RefreshGenerateButtonState();
        HideResults();
    }

    private void RefreshBatchList()
    {
        BatchZipList.ItemsSource = null;
        BatchZipList.ItemsSource = _batchZipPaths;
        ZipPathBox.Text = _batchZipPaths.Count == 0
            ? "No files selected"
            : $"{_batchZipPaths.Count} ZIP file(s) selected";
        ZipPathBox.Foreground = _batchZipPaths.Count == 0
            ? new SolidColorBrush(Color.FromRgb(156, 163, 175))
            : new SolidColorBrush(Color.FromRgb(17, 24, 39));
    }

    private void HandleDroppedZips(IReadOnlyList<string> paths)
    {
        if (BatchRunRadio.IsChecked == true)
        {
            AddBatchZipFiles(paths);
            return;
        }

        SetZipFile(paths[0]);
    }

    private void RefreshRunModeUi()
    {
        var batch = BatchRunRadio.IsChecked == true;
        ZipInputLabel.Text = batch ? "Batch ZIP Inputs" : "Solution ZIP File";
        BatchInputPanel.Visibility = batch ? Visibility.Visible : Visibility.Collapsed;

        if (!batch)
        {
            if (_batchZipPaths.Count > 0)
            {
                ZipPathBox.Text = _batchZipPaths[0];
                ZipPathBox.Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39));
            }
            else if (string.IsNullOrWhiteSpace(ZipPathBox.Text))
            {
                ZipPathBox.Text = "No file selected";
                ZipPathBox.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            }
        }
        else
        {
            RefreshBatchList();
        }
    }

    private void RefreshGenerateButtonState()
    {
        GenerateButton.IsEnabled = CanGenerate();
    }

    private bool CanGenerate()
    {
        if (BatchRunRadio.IsChecked == true)
        {
            return _batchZipPaths.Count > 0;
        }

        return File.Exists(ZipPathBox.Text);
    }

    private static string BuildStepDetail(int currentStep, int totalSteps)
    {
        return $"Step {Math.Clamp(currentStep, 1, Math.Max(totalSteps, 1))}/{Math.Max(totalSteps, 1)}";
    }

    private static double CalculatePercent(int currentStep, int totalSteps)
    {
        if (totalSteps <= 0)
        {
            return 0;
        }

        return (Math.Clamp(currentStep, 0, totalSteps) * 100d) / totalSteps;
    }


internal sealed record ProgressUpdate(
    string Stage,
    string Detail,
    double Percent,
    bool IsIndeterminate);
}

internal sealed record GenerationResult(
    bool Success,
    bool IsBatch,
    string OutputFolder,
    int WorkflowCount,
    List<GeneratedFileItem> Files,
    List<BatchSolutionResultItem> BatchSolutions,
    string? ArchivePath,
    List<string> Warnings,
    string? Error)
{
    public static GenerationResult Failure(string error, string outputFolder, List<string> warnings)
        => new(false, false, outputFolder, 0, [], [], null, warnings, error);
}

internal sealed record GeneratedFileItem(string Icon, string FileName, string FilePath);

internal sealed record BatchSolutionResultItem(
    string SolutionName,
    string Status,
    int WorkflowCount,
    int WarningCount,
    TimeSpan Duration,
    string? OutputFolder,
    string SummaryLine);

