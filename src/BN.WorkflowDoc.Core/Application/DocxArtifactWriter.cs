using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using DocumentFormat.OpenXml;
using SixLaborsImage = SixLabors.ImageSharp.Image;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BN.WorkflowDoc.Core.Application;

/// <summary>
/// Output descriptor for generated DOCX artifacts.
/// </summary>
public sealed record DocxArtifactResult(
    string OutputFolder,
    IReadOnlyList<string> WorkflowFiles,
    string? OverviewFile,
    IReadOnlyList<ProcessingWarning> Warnings,
    string? CombinedFullDetailFile = null);

/// <summary>
/// Options that control which DOCX artifacts are written.
/// </summary>
public sealed record DocxArtifactWriteOptions(
    bool IncludePerWorkflowDocuments = true,
    bool IncludeOverviewDocument = true,
    bool IncludeCombinedFullDetailDocument = false);

/// <summary>
/// Writes workflow and overview documentation models into DOCX artifacts.
/// </summary>
public interface IDocxArtifactWriter
{
    /// <summary>
    /// Writes all generated documentation artifacts to the given output folder.
    /// </summary>
    Task<ParseResult<DocxArtifactResult>> WriteAsync(
        DocumentationGenerationResult generationResult,
        string outputFolder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes generated documentation artifacts using explicit output options.
    /// </summary>
    Task<ParseResult<DocxArtifactResult>> WriteAsync(
        DocumentationGenerationResult generationResult,
        string outputFolder,
        DocxArtifactWriteOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Controls narrative style for generated text sections.
/// </summary>
public enum DocumentNarrativeTone
{
    Business,
    Technical
}

internal enum DocumentPageLayout
{
    Portrait,
    Landscape
}

public sealed class OpenXmlDocxArtifactWriter : IDocxArtifactWriter
{
    private readonly IDiagramRenderer _diagramRenderer;
    private readonly DocumentNarrativeTone _narrativeTone;
    private readonly IDependencyGraphDiagramMapper _dependencyGraphDiagramMapper;

    public OpenXmlDocxArtifactWriter()
        : this(new DeterministicPngDiagramRenderer(), DocumentNarrativeTone.Business, new DependencyGraphDiagramMapper())
    {
    }

    public OpenXmlDocxArtifactWriter(IDiagramRenderer diagramRenderer)
        : this(diagramRenderer, DocumentNarrativeTone.Business, new DependencyGraphDiagramMapper())
    {
    }

    public OpenXmlDocxArtifactWriter(IDiagramRenderer diagramRenderer, DocumentNarrativeTone narrativeTone)
        : this(diagramRenderer, narrativeTone, new DependencyGraphDiagramMapper())
    {
    }

    public OpenXmlDocxArtifactWriter(
        IDiagramRenderer diagramRenderer,
        DocumentNarrativeTone narrativeTone,
        IDependencyGraphDiagramMapper dependencyGraphDiagramMapper)
    {
        _diagramRenderer = diagramRenderer;
        _narrativeTone = narrativeTone;
        _dependencyGraphDiagramMapper = dependencyGraphDiagramMapper;
    }

    public async Task<ParseResult<DocxArtifactResult>> WriteAsync(
        DocumentationGenerationResult generationResult,
        string outputFolder,
        CancellationToken cancellationToken = default)
    {
        return await WriteAsync(generationResult, outputFolder, new DocxArtifactWriteOptions(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ParseResult<DocxArtifactResult>> WriteAsync(
        DocumentationGenerationResult generationResult,
        string outputFolder,
        DocxArtifactWriteOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<ProcessingWarning>(generationResult.Warnings);
        var workflowFiles = new List<string>(generationResult.WorkflowDocuments.Count);
        var renderedWorkflows = new List<(WorkflowDocumentModel Model, IReadOnlyList<RenderedDiagramAsset> Assets)>(generationResult.WorkflowDocuments.Count);

        try
        {
            Directory.CreateDirectory(outputFolder);

            for (var i = 0; i < generationResult.WorkflowDocuments.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var workflowModel = generationResult.WorkflowDocuments[i];

                var renderedDiagrams = await _diagramRenderer
                    .RenderAsync(workflowModel.Diagrams, cancellationToken)
                    .ConfigureAwait(false);

                warnings.AddRange(renderedDiagrams.Warnings);
                var assets = renderedDiagrams.Value ?? Array.Empty<RenderedDiagramAsset>();
                renderedWorkflows.Add((workflowModel, assets));

                if (options.IncludePerWorkflowDocuments)
                {
                    var path = Path.Combine(
                        outputFolder,
                        ArtifactPathNaming.BuildWorkflowDocumentFileName(i + 1, workflowModel.WorkflowName, ".docx"));

                    WriteWorkflowDoc(path, workflowModel, assets, _narrativeTone);
                    workflowFiles.Add(path);
                }
            }

            string? overviewPath = null;
            RenderedDiagramAsset? dependencyGraphAsset = null;
            if (options.IncludeOverviewDocument)
            {
                overviewPath = Path.Combine(outputFolder, "overview.docx");

                if (generationResult.OverviewDocument.DependencyGraph?.Nodes.Count > 0)
                {
                    var dependencyDiagram = _dependencyGraphDiagramMapper.Map(generationResult.OverviewDocument.DependencyGraph);
                    var dependencyRenderResult = await _diagramRenderer
                        .RenderAsync([dependencyDiagram], cancellationToken)
                        .ConfigureAwait(false);

                    warnings.AddRange(dependencyRenderResult.Warnings);
                    dependencyGraphAsset = dependencyRenderResult.Value?.FirstOrDefault();
                }

                WriteOverviewDoc(
                    overviewPath,
                    generationResult.OverviewDocument,
                    generationResult.WorkflowDocuments,
                    _narrativeTone,
                    dependencyGraphAsset);
            }

            string? combinedFile = null;
            if (options.IncludeCombinedFullDetailDocument)
            {
                combinedFile = Path.Combine(outputFolder, "combined-full-detail.docx");
                WriteCombinedWorkflowDoc(combinedFile, generationResult.OverviewDocument.SolutionName, renderedWorkflows, _narrativeTone);
            }

            WriteStepInventoryCsv(outputFolder, generationResult.WorkflowDocuments);

            var value = new DocxArtifactResult(outputFolder, workflowFiles, overviewPath, warnings, combinedFile);
            var status = warnings.Count == 0 ? ProcessingStatus.Success : ProcessingStatus.PartialSuccess;
            return new ParseResult<DocxArtifactResult>(status, value, warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new ProcessingWarning(
                "DOCX_WRITE_FAILED",
                ex.Message,
                outputFolder,
                true,
                WarningCategory.Documentation,
                WarningSeverity.Critical));
            return new ParseResult<DocxArtifactResult>(ProcessingStatus.Failed, null, warnings, ex.Message);
        }
    }

    private static void WriteWorkflowDoc(
        string path,
        WorkflowDocumentModel model,
        IReadOnlyList<RenderedDiagramAsset> renderedDiagrams,
        DocumentNarrativeTone narrativeTone,
        bool includeTableOfContents = true,
        bool includeCoverBlock = true)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        EnsureStyles(main);
        var body = main.Document.Body!;
        var currentLayout = DocumentPageLayout.Portrait;

        if (includeCoverBlock)
        {
            // Cover / title block
            AppendTitle(body, model.WorkflowName);
            AppendParagraph(body, "Dynamics 365 Process Documentation", style: "Subtitle");
            AppendParagraph(body, $"Generated: {DateTime.UtcNow:dd MMMM yyyy HH:mm} UTC", style: "Caption");
            AppendBlankLine(body);
        }

        if (includeTableOfContents)
        {
            AppendHeading(body, "Table of Contents");
            AppendTocPlaceholder(body);
            AppendSectionDivider(body);
        }

        AppendHeading(body, "Process Summary");
        AppendParagraph(body, model.Purpose);
        AppendStyledTable(
            body,
            ["Property", "Value"],
            new[]
            {
            new[] { "Process Category", model.ProcessCategory },
                new[] { "Primary Entity", model.Trigger.PrimaryEntity },
                new[] { "Execution Mode", model.ExecutionMode.ToString() },
            new[] { "On-Demand Available", model.IsOnDemand ? "Yes" : "No" },
                new[] { "Events", BuildEventSummary(model.Trigger) },
                new[]
                {
                    "Attribute Filters",
                    model.Trigger.AttributeFilters.Count == 0
                        ? "None"
                        : string.Join(", ", model.Trigger.AttributeFilters)
                },
                new[] { "Diagram Count", model.Diagrams.Count.ToString() }
            });

        AppendHeading(body, "Process Overview");
        AppendParagraph(body, BuildWorkflowOverviewNarrative(model, narrativeTone));

        AppendHeading(body, "Purpose");
        AppendParagraph(body, model.Purpose);

        if (string.Equals(model.ProcessCategory, "Workflow", StringComparison.OrdinalIgnoreCase))
        {
            AppendHeading(body, "Trigger Matrix");
            AppendStyledTable(
                body,
                ["Trigger", "Enabled"],
                new[]
                {
                    new[] { "On Create", model.Trigger.OnCreate ? "Yes" : "No" },
                    new[] { "On Update", model.Trigger.OnUpdate ? "Yes" : "No" },
                    new[] { "On Delete", model.Trigger.OnDelete ? "Yes" : "No" }
                });
        }
        else
        {
            AppendHeading(body, "Invocation Profile");
            AppendParagraph(body, BuildInvocationProfileNarrative(model));
        }

        AppendHeading(body, "Execution Mode");
        AppendParagraph(body, BuildExecutionModeNarrative(model));

        var fieldReads = BuildFieldReadRows(model);
        var fieldWrites = BuildFieldWriteRows(model);

        AppendHeading(body, "Fields Read");
        if (fieldReads.Count == 0)
        {
            AppendParagraph(body, "No explicit field-read metadata was extracted.");
        }
        else
        {
            currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Landscape);
            AppendStyledTable(
                body,
                ["Step", "Entity", "Attribute", "Source"],
                fieldReads,
                [18, 16, 18, 48]);
        }

        AppendHeading(body, "Fields Set / Updated");
        if (fieldWrites.Count == 0)
        {
            AppendParagraph(body, "No explicit field-update metadata was extracted.");
        }
        else
        {
            AppendStyledTable(
                body,
                ["Step", "Entity", "Attribute", "Value", "Target"],
                fieldWrites,
                [16, 14, 16, 30, 24]);
        }

        AppendHeading(body, "Process Flow Steps");
        if (model.Steps.Count == 0)
        {
            AppendParagraph(body, "No process steps were extracted.");
        }
        else
        {
            AppendStyledTable(
                body,
                ["#", "Step Description"],
                model.Steps.Select(step => new[]
                {
                    step.Sequence.ToString(),
                    $"[{step.StepType}] {step.Label}: {step.Narrative}"
                }),
                [8, 92]);
        }

        AppendHeading(body, "Transition Matrix");
        if (model.Transitions.Count == 0)
        {
            AppendParagraph(body, "No transitions were extracted.");
        }
        else
        {
            AppendStyledTable(
                body,
                ["#", "From", "To", "Condition", "Narrative"],
                model.Transitions.Select(transition => new[]
                {
                    transition.Sequence.ToString(),
                    transition.FromStepLabel,
                    transition.ToStepLabel,
                    string.IsNullOrWhiteSpace(transition.ConditionLabel) ? "(default)" : transition.ConditionLabel!,
                    transition.Narrative
                }),
                [8, 20, 20, 18, 34]);
        }

        currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Portrait);

        AppendHeading(body, "Full Step Breakdown");
        AppendParagraph(
            body,
            model.Steps.Count == 0
                ? "No executable workflow steps were extracted for this workflow."
                : $"The extracted workflow graph contains {model.Steps.Count} documented step(s). Each step below lists its role, transitions, and captured metadata.");

        if (model.Steps.Count > 0)
        {
            currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Landscape);
            AppendStyledTable(
                body,
                ["#", "Step", "Type", "Incoming", "Outgoing", "Synthetic"],
                model.Steps.Select(step => new[]
                {
                    step.Sequence.ToString(),
                    step.Label,
                    step.StepType,
                    step.IncomingPaths.Count.ToString(),
                    step.OutgoingPaths.Count.ToString(),
                    step.IsSynthetic ? "Yes" : "No"
                }),
                [8, 28, 18, 16, 16, 14]);

            currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Portrait);

            foreach (var step in model.Steps)
            {
                AppendHeading(body, $"Step {step.Sequence}: {step.Label}", level: 2);
                AppendParagraph(body, step.Narrative);
                AppendKeyValueTable(
                    body,
                    new[]
                    {
                        ("Step ID", step.StepId),
                        ("Step Type", step.StepType),
                        ("Synthetic", step.IsSynthetic ? "Yes" : "No"),
                        ("Incoming Paths", step.IncomingPaths.Count == 0 ? "None" : string.Join("; ", step.IncomingPaths)),
                        ("Outgoing Paths", step.OutgoingPaths.Count == 0 ? "None" : string.Join("; ", step.OutgoingPaths))
                    });

                if (step.Attributes.Count > 0)
                {
                    AppendParagraph(body, "Captured step metadata:", style: "Emphasis");
                    AppendStyledTable(
                        body,
                        ["Attribute", "Value"],
                        step.Attributes.Select(attribute => new[] { attribute.Name, attribute.Value }),
                        [30, 70]);
                }
                else
                {
                    AppendParagraph(body, "Captured step metadata: none.");
                }
            }
        }

        AppendHeading(body, "Narrative Sections");
        foreach (var section in model.Sections)
        {
            AppendHeading(body, section.Title, level: 2);
            AppendParagraph(body, section.Narrative);

            if (section.SourceTraces.Count > 0)
            {
                AppendParagraph(body, "Source traces:", style: "Emphasis");
                AppendSourceTraceTable(body, section.SourceTraces);
            }
        }

        AppendHeading(body, "Diagrams");
        if (model.Diagrams.Count == 0)
        {
            AppendParagraph(body, "No diagram graphs are available for this workflow.");
        }
        else
        {
            currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Landscape);
            foreach (var diagram in model.Diagrams)
            {
                AppendHeading(body, diagram.Type.ToString(), level: 2);
                AppendStyledTable(
                    body,
                    ["Diagram Property", "Value"],
                    new[]
                    {
                        new[] { "Caption", diagram.Caption },
                        new[] { "Nodes", diagram.Nodes.Count.ToString() },
                        new[] { "Edges", diagram.Edges.Count.ToString() }
                    },
                    [24, 76]);

                var matchingAssets = renderedDiagrams
                    .Where(x => x.Type == diagram.Type)
                    .ToArray();
                var pagedDiagrams = DeterministicDiagramPaging.Split(diagram);

                if (matchingAssets.Length > 0)
                {
                    AppendParagraph(
                        body,
                        matchingAssets.Length == 1
                            ? "Rendered Artifact:"
                            : $"Rendered Artifact Views: {matchingAssets.Length} segment(s) generated for readability.",
                        style: "Emphasis");

                    for (var assetIndex = 0; assetIndex < matchingAssets.Length; assetIndex++)
                    {
                        if (matchingAssets.Length > 1)
                        {
                            if (assetIndex > 0)
                            {
                                AppendPageBreak(body);
                            }

                            AppendHeading(
                                body,
                                BuildDiagramViewHeading(diagram.Type, pagedDiagrams, assetIndex, matchingAssets.Length),
                                level: 3);
                        }

                        AddImage(main, body, matchingAssets[assetIndex]);
                    }
                }
            }
        }

        AppendHeading(body, "Appendix: Warnings");
        if (model.Warnings.Count == 0)
        {
            AppendParagraph(body, "None");
        }
        else
        {
            AppendWarningTable(body, model.Warnings);
        }

        ApplyDocumentLayout(body, currentLayout);
        main.Document.Save();
    }

    private static void WriteCombinedWorkflowDoc(
        string path,
        string solutionName,
        IReadOnlyList<(WorkflowDocumentModel Model, IReadOnlyList<RenderedDiagramAsset> Assets)> renderedWorkflows,
        DocumentNarrativeTone narrativeTone)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        EnsureStyles(main);
        var body = main.Document.Body!;

        AppendTitle(body, "Dynamics 365 Combined Process Documentation");
        AppendParagraph(body, string.IsNullOrWhiteSpace(solutionName) ? "Workflow Selection" : solutionName, style: "Subtitle");
        AppendParagraph(body, $"Included Processes: {renderedWorkflows.Count}", style: "Caption");
        AppendParagraph(body, $"Generated: {DateTime.UtcNow:dd MMMM yyyy HH:mm} UTC", style: "Caption");
        AppendHeading(body, "Table of Contents");
        AppendTocPlaceholder(body);
        AppendSectionDivider(body);

        var tempFiles = new List<string>(renderedWorkflows.Count);
        try
        {
            for (var i = 0; i < renderedWorkflows.Count; i++)
            {
                var (workflowModel, assets) = renderedWorkflows[i];

                if (i > 0)
                {
                    AppendPageBreak(body);
                }

                AppendHeading(body, $"Process {i + 1} of {renderedWorkflows.Count}: {workflowModel.WorkflowName}", level: 1);
                AppendParagraph(
                    body,
                    $"Category: {workflowModel.ProcessCategory} | Mode: {workflowModel.ExecutionMode} | On-demand: {(workflowModel.IsOnDemand ? "Yes" : "No")}",
                    style: "Caption");
                AppendSectionDivider(body);

                var tempPath = Path.Combine(Path.GetTempPath(), $"bn-workflowdoc-combined-{Guid.NewGuid():N}.docx");
                WriteWorkflowDoc(
                    tempPath,
                    workflowModel,
                    assets,
                    narrativeTone,
                    includeTableOfContents: false,
                    includeCoverBlock: false);
                tempFiles.Add(tempPath);

                var altChunkId = $"chunk{i + 1:D4}";
                var altPart = main.AddAlternativeFormatImportPart(AlternativeFormatImportPartType.WordprocessingML, altChunkId);
                using (var stream = File.OpenRead(tempPath))
                {
                    altPart.FeedData(stream);
                }

                body.Append(new AltChunk { Id = altChunkId });
            }
        }
        finally
        {
            foreach (var tempPath in tempFiles)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }

        main.Document.Save();
    }

    private static void WriteOverviewDoc(
        string path,
        OverviewDocumentModel model,
        IReadOnlyList<WorkflowDocumentModel> workflowDocuments,
        DocumentNarrativeTone narrativeTone,
        RenderedDiagramAsset? dependencyGraphAsset)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body());
        EnsureStyles(main);
        var body = main.Document.Body!;
        var currentLayout = DocumentPageLayout.Portrait;

        // Cover page inspired by enterprise documentation layouts
        AppendTitle(body, "Dynamics 365 Solution");
        AppendParagraph(body, "Workflow Process Documentation", style: "Heading1");
        AppendParagraph(body, model.SolutionName, style: "Subtitle");
        AppendParagraph(body, $"Total Processes: {model.Workflows.Count}", style: "Caption");
        AppendParagraph(body, $"Generated: {DateTime.UtcNow:dd MMMM yyyy}", style: "Caption");
        AppendSectionDivider(body);

        AppendHeading(body, "Executive Summary");
        AppendParagraph(
            body,
            $"This document provides detailed documentation of all {model.Workflows.Count} workflow processes in the {model.SolutionName} solution. " +
            "Each process includes purpose, trigger profile, complexity/risk indicators, and diagram artifacts where available.");

        var syncCount = model.Workflows.Count(x => x.ExecutionMode == ExecutionMode.Synchronous);
        var asyncCount = model.Workflows.Count - syncCount;
        var highComplexity = model.Workflows.Count(x => x.ComplexityScore >= 10);
        var withRisks = model.Workflows.Count(x => x.KeyRisks.Count > 0 && !HasNoRiskPlaceholder(x.KeyRisks));
        var avgQualityScore = model.Workflows.Count == 0
            ? 0
            : (int)Math.Round(model.Workflows
                .Where(x => x.QualityScore is not null)
                .Select(x => x.QualityScore!.OverallScore)
                .DefaultIfEmpty()
                .Average());
        var highRiskQuality = model.Workflows.Count(x =>
            string.Equals(x.QualityScore?.RiskBand, "High", StringComparison.OrdinalIgnoreCase));

        AppendHeading(body, "Solution Statistics");
        AppendStyledTable(
            body,
            ["Metric", "Count"],
            new[]
            {
                new[] { "Total Processes", model.Workflows.Count.ToString() },
                new[] { "Real-time (Synchronous)", syncCount.ToString() },
                new[] { "Background (Asynchronous)", asyncCount.ToString() },
                new[] { "High Complexity (score >= 10)", highComplexity.ToString() },
                new[] { "Processes with Key Risks", withRisks.ToString() },
                new[] { "Global Warnings", model.GlobalWarnings.Count.ToString() }
            },
            [68, 32]);

        AppendHeading(body, "Quality Assessment Summary");
        AppendStyledTable(
            body,
            ["Metric", "Value"],
            new[]
            {
                new[] { "Average Quality Score", avgQualityScore.ToString() },
                new[] { "High Quality Risk Band", highRiskQuality.ToString() },
                new[] { "Workflows With Any Warning Code", model.Workflows.Count(x => (x.WarningCodes?.Count ?? 0) > 0).ToString() },
                new[] { "Quality Scoring Coverage", model.Workflows.Count(x => x.QualityScore is not null).ToString() }
            },
            [68, 32]);

        AppendHeading(body, "Dependency Graph Overview");
        if (model.DependencyGraph is null || model.DependencyGraph.Nodes.Count == 0)
        {
            AppendParagraph(body, "No solution-level dependencies were detected.");
        }
        else
        {
            currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Landscape);
            AppendParagraph(body, model.DependencyGraph.Summary);
            if (dependencyGraphAsset is not null)
            {
                AddImage(main, body, dependencyGraphAsset);
            }

            AppendStyledTable(
                body,
                ["Node", "Type", "Incoming", "Outgoing"],
                model.DependencyGraph.Nodes.Select(node => new[]
                {
                    node.DisplayName,
                    node.NodeType,
                    node.IncomingEdges.ToString(),
                    node.OutgoingEdges.ToString()
                }),
                [40, 20, 20, 20]);

            AppendStyledTable(
                body,
                ["From", "To", "Relationship", "Notes"],
                model.DependencyGraph.Edges.Select(edge => new[]
                {
                    ResolveNodeLabel(model.DependencyGraph, edge.SourceNodeId),
                    ResolveNodeLabel(model.DependencyGraph, edge.TargetNodeId),
                    edge.RelationshipType,
                    edge.Notes ?? string.Empty
                }),
                [24, 24, 18, 34]);
        }

        currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Portrait);

        AppendHeading(body, "Table of Contents");
        AppendTocPlaceholder(body);
        AppendSectionDivider(body);

        AppendHeading(body, "Process Index");
        currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Landscape);
        AppendStyledTable(
            body,
            ["#", "Process Name", "Trigger", "Mode", "Complexity", "Dependencies"],
            model.Workflows.Select((card, idx) => new[]
            {
                (idx + 1).ToString(),
                card.WorkflowName,
                card.TriggerSummary,
                card.ExecutionMode.ToString(),
                card.ComplexityScore.ToString(),
                card.Dependencies.Count.ToString()
            }),
            [6, 26, 28, 12, 12, 16]);

        AppendHeading(body, "Workflow Risk Matrix");
        AppendStyledTable(
            body,
            ["Workflow", "Mode", "Complexity", "Quality", "Risk Band", "Warnings", "Dependencies"],
            model.Workflows.Select(card => new[]
            {
                card.WorkflowName,
                card.ExecutionMode.ToString(),
                card.ComplexityScore.ToString(),
                card.QualityScore?.OverallScore.ToString() ?? "n/a",
                card.QualityScore?.RiskBand ?? "n/a",
                (card.WarningCodes?.Count ?? 0).ToString(),
                card.Dependencies.Count.ToString()
            }),
            [24, 12, 11, 11, 16, 10, 16]);

        currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Portrait);

        AppendHeading(body, "Workflow Cards");

        foreach (var card in model.Workflows)
        {
            AppendHeading(body, card.WorkflowName, level: 2);
            AppendStyledTable(
                body,
                ["Property", "Value"],
                new[]
                {
                    new[] { "Plain-English Overview", BuildOverviewCardNarrative(card, narrativeTone) },
                    new[] { "Purpose", card.Purpose },
                    new[] { "Trigger", card.TriggerSummary },
                    new[] { "Execution Mode", card.ExecutionMode.ToString() },
                    new[] { "Complexity Score", card.ComplexityScore.ToString() },
                    new[] { "Quality Score", card.QualityScore?.OverallScore.ToString() ?? "n/a" },
                    new[] { "Risk Band", card.QualityScore?.RiskBand ?? "n/a" },
                    new[] { "Quality Summary", card.QualityScore?.Summary ?? "n/a" }
                },
                [28, 72]);

            if ((card.WarningCodes?.Count ?? 0) > 0)
            {
                AppendParagraph(body, "Warning Codes: " + string.Join(", ", card.WarningCodes!), style: "Caption");
            }

            AppendParagraph(body, "Dependencies:", style: "Emphasis");
            if (card.Dependencies.Count == 0)
            {
                AppendParagraph(body, "- None");
            }
            else
            {
                foreach (var dep in card.Dependencies)
                {
                    AppendParagraph(body, $"- {dep}");
                }
            }

            AppendParagraph(body, "Key Risks:", style: "Emphasis");
            if (card.KeyRisks.Count == 0)
            {
                AppendParagraph(body, "- None");
            }
            else
            {
                foreach (var risk in card.KeyRisks)
                {
                    AppendParagraph(body, $"- {risk}");
                }
            }
        }

        AppendHeading(body, "Appendix: Detailed Workflow Step Inventory");
        if (workflowDocuments.Count == 0 || workflowDocuments.All(x => x.Steps.Count == 0))
        {
            AppendParagraph(body, "No workflow step inventory data is available.");
        }
        else
        {
            currentLayout = SwitchToLayout(body, currentLayout, DocumentPageLayout.Landscape);
            foreach (var workflow in workflowDocuments)
            {
                AppendHeading(body, workflow.WorkflowName, level: 2);

                if (workflow.Steps.Count == 0)
                {
                    AppendParagraph(body, "No extracted steps.");
                    continue;
                }

                AppendStyledTable(
                    body,
                    ["#", "Step", "Type", "Incoming", "Outgoing", "Synthetic"],
                    workflow.Steps.Select(step => new[]
                    {
                        step.Sequence.ToString(),
                        step.Label,
                        step.StepType,
                        step.IncomingPaths.Count.ToString(),
                        step.OutgoingPaths.Count.ToString(),
                        step.IsSynthetic ? "Yes" : "No"
                    }),
                    [8, 28, 18, 16, 16, 14]);
            }
        }

        AppendHeading(body, "Appendix: Global Warnings");
        if (model.GlobalWarnings.Count == 0)
        {
            AppendParagraph(body, "None");
        }
        else
        {
            AppendWarningTable(body, model.GlobalWarnings);
        }

        ApplyDocumentLayout(body, currentLayout);
        main.Document.Save();
    }

    private static void AppendTocPlaceholder(Body body)
    {
        var tocField = new SimpleField
        {
            Instruction = @"TOC \o ""1-2"" \h \z \u"
        };
        tocField.Append(new Run(new Text("Right-click and update field to generate the table of contents.")));
        body.Append(new Paragraph(tocField));
    }

    private static void AppendKeyValueTable(Body body, IEnumerable<(string Key, string Value)> rows)
    {
        var dataRows = rows.Select(row => new[] { row.Key, row.Value }).ToArray();
        var columnWidths = BuildColumnWidths(dataRows.Length == 0 ? ["Key", "Value"] : ["Key", "Value"], dataRows, new[] { 28, 72 });
        var table = CreateTable(columnWidths);
        foreach (var row in dataRows)
        {
            table.Append(CreateTableRow(row, columnWidths));
        }

        body.Append(table);
    }

    private static void AppendStyledTable(Body body, IReadOnlyList<string> headers, IEnumerable<string[]> rows, IReadOnlyList<int>? preferredPercentages = null)
    {
        var dataRows = rows.ToArray();
        var columnWidths = BuildColumnWidths(headers, dataRows, preferredPercentages);
        var table = CreateTable(columnWidths);
        table.Append(CreateHeaderRow(headers, columnWidths));
        foreach (var row in dataRows)
        {
            table.Append(CreateTableRow(row, columnWidths));
        }

        body.Append(table);
    }

    private static void AppendSourceTraceTable(Body body, IReadOnlyList<SourceTrace> traces)
    {
        var rows = traces.Select(trace => new[] { trace.SourceField, trace.SourcePath, trace.Notes ?? string.Empty }).ToArray();
        var headers = new[] { "Source Field", "Source Path", "Notes" };
        var columnWidths = BuildColumnWidths(headers, rows, new[] { 20, 34, 46 });
        var table = CreateTable(columnWidths);
        table.Append(CreateHeaderRow(headers, columnWidths));
        foreach (var trace in traces)
        {
            table.Append(CreateTableRow([trace.SourceField, trace.SourcePath, trace.Notes ?? string.Empty], columnWidths));
        }

        body.Append(table);
    }

    private static void AppendWarningTable(Body body, IReadOnlyList<ProcessingWarning> warnings)
    {
        var headers = new[] { "Code", "Message", "Source", "Category", "Severity", "Blocking" };
        var rows = warnings.Select(warning => new[]
        {
            warning.Code,
            warning.Message,
            warning.Source ?? string.Empty,
            warning.Category.ToString(),
            warning.Severity.ToString(),
            warning.IsBlocking.ToString()
        }).ToArray();
        var columnWidths = BuildColumnWidths(headers, rows, new[] { 12, 34, 18, 14, 12, 10 });
        var table = CreateTable(columnWidths);
        table.Append(CreateHeaderRow(headers, columnWidths));
        foreach (var warning in warnings)
        {
            table.Append(CreateTableRow([
                warning.Code,
                warning.Message,
                warning.Source ?? string.Empty,
                warning.Category.ToString(),
                warning.Severity.ToString(),
                warning.IsBlocking.ToString()], columnWidths));
        }

        body.Append(table);
    }

    private static Table CreateTable(IReadOnlyList<int> columnWidths)
    {
        var table = new Table(
            new TableProperties(
                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableCellMarginDefault(
                    new TopMargin { Width = "40", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "40", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = 40, Type = TableWidthValues.Dxa },
                    new TableCellRightMargin { Width = 40, Type = TableWidthValues.Dxa }),
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 8, Color = "D9E1F2" },
                    new BottomBorder { Val = BorderValues.Single, Size = 8, Color = "D9E1F2" },
                    new LeftBorder { Val = BorderValues.Single, Size = 8, Color = "D9E1F2" },
                    new RightBorder { Val = BorderValues.Single, Size = 8, Color = "D9E1F2" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6, Color = "E7ECF7" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 6, Color = "E7ECF7" }),
                new TableLook
                {
                    FirstRow = OnOffValue.FromBoolean(true),
                    LastRow = OnOffValue.FromBoolean(false),
                    FirstColumn = OnOffValue.FromBoolean(false),
                    LastColumn = OnOffValue.FromBoolean(false),
                    NoHorizontalBand = OnOffValue.FromBoolean(false),
                    NoVerticalBand = OnOffValue.FromBoolean(true)
                }));

        table.Append(new TableGrid(columnWidths.Select(width => new GridColumn { Width = ((width * 144d) / 100d).ToString("0") })));
        return table;
    }

    private static TableRow CreateHeaderRow(IReadOnlyList<string> cells, IReadOnlyList<int> columnWidths)
    {
        var row = new TableRow();
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            row.Append(new TableCell(
                new TableCellProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Fill = "EEF3FB", Color = "auto" },
                    new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = columnWidths[i].ToString() }),
                new Paragraph(
                    new ParagraphProperties(new SpacingBetweenLines { Before = "10", After = "10" }),
                    new Run(
                        new RunProperties(new Bold(), new Color { Val = "1F4E79" }, new FontSize { Val = "18" }, new FontSizeComplexScript { Val = "18" }),
                        new Text(cell ?? string.Empty))),
                new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = columnWidths[i].ToString() })));
        }

        return row;
    }

    private static TableRow CreateTableRow(IReadOnlyList<string> cells, IReadOnlyList<int> columnWidths)
    {
        var row = new TableRow();
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            row.Append(new TableCell(
                new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = columnWidths[Math.Min(i, columnWidths.Count - 1)].ToString() }),
                new Paragraph(
                    new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }),
                    new Run(new RunProperties(new FontSize { Val = "18" }, new FontSizeComplexScript { Val = "18" }), new Text(cell ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }))));
        }

        return row;
    }

    private static IReadOnlyList<int> BuildColumnWidths(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, IReadOnlyList<int>? preferredPercentages = null)
    {
        if (preferredPercentages is not null && preferredPercentages.Count == headers.Count)
        {
            return NormalizePercentages(preferredPercentages);
        }

        var widths = new int[headers.Count];
        for (var i = 0; i < headers.Count; i++)
        {
            var maxLength = headers[i]?.Length ?? 8;
            foreach (var row in rows)
            {
                if (i >= row.Length)
                {
                    continue;
                }

                maxLength = Math.Max(maxLength, Math.Min(48, row[i]?.Length ?? 0));
            }

            widths[i] = Math.Clamp(maxLength, 8, 48);
        }

        var total = widths.Sum();
        if (total <= 0)
        {
            return Enumerable.Repeat(5000 / Math.Max(headers.Count, 1), Math.Max(headers.Count, 1)).ToArray();
        }

        var scaled = widths
            .Select(width => Math.Max(300, (int)Math.Round((width * 5000d) / total)))
            .ToArray();

        return NormalizePercentages(scaled);
    }

    private static IReadOnlyList<int> NormalizePercentages(IReadOnlyList<int> widths)
    {
        var normalized = widths.ToArray();
        var total = normalized.Sum();
        if (total == 5000)
        {
            return normalized;
        }

        if (total <= 0)
        {
            return Enumerable.Repeat(5000 / Math.Max(normalized.Length, 1), Math.Max(normalized.Length, 1)).ToArray();
        }

        for (var i = 0; i < normalized.Length; i++)
        {
            normalized[i] = (int)Math.Round((normalized[i] * 5000d) / total);
        }

        var delta = 5000 - normalized.Sum();
        normalized[^1] += delta;
        return normalized;
    }

    private static void AppendTitle(Body body, string text)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Title" }),
            new Run(new Text(text)));
        body.Append(paragraph);
    }

    private static void AppendHeading(Body body, string text, int level = 1)
    {
        var style = level switch
        {
            1 => "Heading1",
            2 => "Heading2",
            _ => "Heading3"
        };
        var paragraph = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = style }),
            new Run(new Text(text)));
        body.Append(paragraph);
    }

    private static void AppendParagraph(Body body, string text, string style = "BodyText")
    {
        body.Append(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = style }),
            new Run(new Text(text))));
    }

    private static void AppendBlankLine(Body body)
    {
        body.Append(new Paragraph(new Run(new Text(" "))));
    }

    private static void AppendSectionDivider(Body body)
    {
        body.Append(new Paragraph(
            new ParagraphProperties(
                new ParagraphBorders(
                    new BottomBorder
                    {
                        Val = BorderValues.Single,
                        Size = 8,
                        Color = "D9E1F2",
                        Space = 1
                    }))));
        AppendBlankLine(body);
    }

    private static void AppendPageBreak(Body body)
    {
        body.Append(new Paragraph(
            new Run(new Break { Type = BreakValues.Page })));
    }

    private static DocumentPageLayout SwitchToLayout(Body body, DocumentPageLayout currentLayout, DocumentPageLayout targetLayout)
    {
        if (currentLayout == targetLayout)
        {
            return currentLayout;
        }

        body.Append(new Paragraph(
            new ParagraphProperties(
                BuildSectionProperties(currentLayout, SectionMarkValues.NextPage)),
            new Run(new Break { Type = BreakValues.Page })));

        return targetLayout;
    }

    private static void ApplyDocumentLayout(Body body, DocumentPageLayout currentLayout)
    {
        var sectionProperties = body.Elements<SectionProperties>().LastOrDefault();
        sectionProperties?.Remove();

        body.Append(BuildSectionProperties(currentLayout));
    }

    private static SectionProperties BuildSectionProperties(DocumentPageLayout layout, SectionMarkValues? sectionMark = null)
    {
        var pageSize = layout == DocumentPageLayout.Landscape
            ? new PageSize { Width = 15840U, Height = 12240U, Orient = PageOrientationValues.Landscape }
            : new PageSize { Width = 12240U, Height = 15840U, Orient = PageOrientationValues.Portrait };

        var properties = new SectionProperties();
        if (sectionMark is not null)
        {
            properties.Append(new SectionType { Val = sectionMark.Value });
        }

        properties.Append(pageSize);
        properties.Append(new PageMargin
        {
            Top = 720,
            Right = 540,
            Bottom = 720,
            Left = 540,
            Header = 360,
            Footer = 360,
            Gutter = 0
        });

        return properties;
    }

    private static string BuildDiagramViewHeading(
        DiagramType diagramType,
        IReadOnlyList<DiagramGraph> pagedDiagrams,
        int assetIndex,
        int totalAssets)
    {
        var caption = assetIndex < pagedDiagrams.Count
            ? pagedDiagrams[assetIndex].Caption
            : $"View {assetIndex + 1} of {totalAssets}";
        var title = ExtractSegmentTitle(caption);
        return string.IsNullOrWhiteSpace(title)
            ? $"{diagramType} View {assetIndex + 1} of {totalAssets}"
            : $"{diagramType} View {assetIndex + 1}: {title}";
    }

    private static string ExtractSegmentTitle(string caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return string.Empty;
        }

        var openParen = caption.LastIndexOf('(');
        var closeParen = caption.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen)
        {
            return string.Empty;
        }

        var inner = caption[(openParen + 1)..closeParen];
        var separator = inner.IndexOf(';');
        return separator < 0 ? inner.Trim() : inner[..separator].Trim();
    }

    private static void AddImage(MainDocumentPart main, Body body, RenderedDiagramAsset asset)
    {
        var imagePart = main.AddImagePart(asset.ContentType);
        using (var stream = new MemoryStream(asset.Content))
        {
            imagePart.FeedData(stream);
        }

        var relationshipId = main.GetIdOfPart(imagePart);

    var (cx, cy) = CalculateImageExtent(asset);

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent
                {
                    LeftEdge = 0L,
                    TopEdge = 0L,
                    RightEdge = 0L,
                    BottomEdge = 0L
                },
                new DW.DocProperties { Id = 1U, Name = asset.FileName },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = asset.FileName },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });

        body.Append(new Paragraph(new Run(drawing)));
        body.Append(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Caption" }),
            new Run(new Text(asset.Caption))));
    }

    private static (long Cx, long Cy) CalculateImageExtent(RenderedDiagramAsset asset)
    {
        const long fallbackCx = 5943600L;
        const long fallbackCy = 3200400L;
        const long maxCx = 5943600L;
        const long maxCy = 6858000L;
        const double emusPerPixel = 9525d;

        try
        {
            var info = SixLaborsImage.Identify(asset.Content);
            if (info is null || info.Width <= 0 || info.Height <= 0)
            {
                return (fallbackCx, fallbackCy);
            }

            var rawCx = info.Width * emusPerPixel;
            var rawCy = info.Height * emusPerPixel;
            var scale = Math.Min(maxCx / rawCx, maxCy / rawCy);
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                return (fallbackCx, fallbackCy);
            }

            var cx = (long)Math.Round(rawCx * scale);
            var cy = (long)Math.Round(rawCy * scale);
            return (Math.Max(cx, 1L), Math.Max(cy, 1L));
        }
        catch
        {
            return (fallbackCx, fallbackCy);
        }
    }

    private static string BuildEventSummary(WorkflowTrigger trigger)
    {
        var events = new List<string>(3);
        if (trigger.OnCreate)
        {
            events.Add("Create");
        }

        if (trigger.OnUpdate)
        {
            events.Add("Update");
        }

        if (trigger.OnDelete)
        {
            events.Add("Delete");
        }

        return events.Count == 0 ? "Manual/Unknown" : string.Join(", ", events);
    }

    private static string BuildWorkflowOverviewNarrative(WorkflowDocumentModel model, DocumentNarrativeTone narrativeTone)
    {
        var isWorkflow = string.Equals(model.ProcessCategory, "Workflow", StringComparison.OrdinalIgnoreCase);
        var isDialog = string.Equals(model.ProcessCategory, "Dialog", StringComparison.OrdinalIgnoreCase);
        var isAction = string.Equals(model.ProcessCategory, "Action", StringComparison.OrdinalIgnoreCase);

        if (narrativeTone == DocumentNarrativeTone.Technical)
        {
            var modeText = model.ExecutionMode == ExecutionMode.Synchronous
                ? "synchronously"
                : "asynchronously";

            var whenText = BuildBusinessTriggerSummary(model.Trigger);
            var filterText = model.Trigger.AttributeFilters.Count == 0
                ? "No attribute-level update filters were detected."
                : $"Attribute filters: {string.Join(", ", model.Trigger.AttributeFilters)}.";

            var diagramText = model.Diagrams.Count == 0
                ? "No diagram metadata was available."
                : $"{model.Diagrams.Count} diagram artifact(s) are included.";

                 if (isAction)
                 {
                  return $"Action process executes {modeText} for entity '{model.Trigger.PrimaryEntity}'. " +
                      $"Invocation model is action/API driven (not standard create/update/delete trigger flags). On-demand available: {(model.IsOnDemand ? "yes" : "no")}. {diagramText}";
                 }

                 if (isDialog)
                 {
                  return $"Dialog process executes {modeText} for entity '{model.Trigger.PrimaryEntity}'. " +
                      $"Invocation model is user-driven interactive execution. On-demand available: {(model.IsOnDemand ? "yes" : "no")}. {diagramText}";
                 }

                 return $"Workflow executes {modeText} for entity '{model.Trigger.PrimaryEntity}' and triggers {whenText}. " +
                     $"{filterText} On-demand available: {(model.IsOnDemand ? "yes" : "no")}. {diagramText}";
        }

        var modeTextBusiness = model.ExecutionMode == ExecutionMode.Synchronous
            ? "immediately"
            : "in the background";

        var whenTextBusiness = BuildBusinessTriggerSummary(model.Trigger);

        var filterTextBusiness = model.Trigger.AttributeFilters.Count == 0
            ? string.Empty
            : $" It focuses on these key fields: {string.Join(", ", model.Trigger.AttributeFilters)}.";

        var diagramTextBusiness = model.Diagrams.Count == 0
            ? string.Empty
            : " A process map is included for quick review.";

         if (isAction)
         {
             return $"This action process supports {model.Trigger.PrimaryEntity} operations and runs {modeTextBusiness}. " +
                 $"It is called by other processes or API operations and is {(model.IsOnDemand ? "available" : "not available")} for on-demand execution.{diagramTextBusiness}";
         }

         if (isDialog)
         {
             return $"This dialog process guides users through {model.Trigger.PrimaryEntity} steps and runs {modeTextBusiness}. " +
                 $"It is {(model.IsOnDemand ? "available" : "not available")} for on-demand interactive execution.{diagramTextBusiness}";
         }

         return $"This workflow automates key steps for {model.Trigger.PrimaryEntity} records. " +
             $"It runs {modeTextBusiness} {whenTextBusiness}, helping teams apply the process consistently and reduce manual effort. " +
             $"On-demand execution is {(model.IsOnDemand ? "available" : "not available")}.{filterTextBusiness}{diagramTextBusiness}";
    }

    private static string BuildOverviewCardNarrative(OverviewWorkflowCard card, DocumentNarrativeTone narrativeTone)
    {
        var categoryText = string.IsNullOrWhiteSpace(card.ProcessCategory) ? "Workflow" : card.ProcessCategory;
        if (narrativeTone == DocumentNarrativeTone.Technical)
        {
            var modeTextTechnical = card.ExecutionMode == ExecutionMode.Synchronous
                ? "synchronous (real-time)"
                : "asynchronous (background)";

            var riskTextTechnical = HasNoRiskPlaceholder(card.KeyRisks) || card.KeyRisks.Count == 0
                ? "No explicit risk indicators were flagged by metadata heuristics."
                : $"Risk indicators flagged: {card.KeyRisks.Count}.";

                 return $"Category: {categoryText}. Trigger profile: {card.TriggerSummary}. Mode: {modeTextTechnical}. " +
                     $"On-demand available: {(card.IsOnDemand ? "yes" : "no")}. Dependencies: {card.Dependencies.Count}. Complexity score: {card.ComplexityScore}. {riskTextTechnical}";
        }

        var modeTextBusiness = card.ExecutionMode == ExecutionMode.Synchronous
            ? "as soon as the trigger occurs"
            : "after the trigger in the background";

        var dependencyText = card.Dependencies.Count == 0
            ? "It runs independently."
            : $"It relies on {card.Dependencies.Count} linked process item(s).";

        var riskText = HasNoRiskPlaceholder(card.KeyRisks) || card.KeyRisks.Count == 0
            ? "No major risk indicators were identified."
            : "Potential risk indicators were identified and should be reviewed.";

        return $"This {categoryText.ToLowerInvariant()} starts when {card.TriggerSummary} and runs {modeTextBusiness}. " +
             $"On-demand execution is {(card.IsOnDemand ? "available" : "not available")}. {dependencyText} {riskText}";
    }

    private static string BuildInvocationProfileNarrative(WorkflowDocumentModel model)
    {
        if (string.Equals(model.ProcessCategory, "Action", StringComparison.OrdinalIgnoreCase))
        {
            return $"Action invocation is process/API driven. Direct event triggers (create/update/delete) are not primary metadata for actions. Context entity: {model.Trigger.PrimaryEntity}. On-demand available: {(model.IsOnDemand ? "Yes" : "No")}.";
        }

        if (string.Equals(model.ProcessCategory, "Dialog", StringComparison.OrdinalIgnoreCase))
        {
            return $"Dialog invocation is user-driven interactive execution. Context entity: {model.Trigger.PrimaryEntity}. On-demand available: {(model.IsOnDemand ? "Yes" : "No")}.";
        }

        return $"Workflow invocation follows event triggers for {model.Trigger.PrimaryEntity} records. On-demand available: {(model.IsOnDemand ? "Yes" : "No")}.";
    }

    private static string BuildExecutionModeNarrative(WorkflowDocumentModel model)
    {
        var modeText = model.ExecutionMode == ExecutionMode.Synchronous
            ? "synchronous (real-time)"
            : "asynchronous (background)";

        if (string.Equals(model.ProcessCategory, "Action", StringComparison.OrdinalIgnoreCase))
        {
            return $"This action runs in {modeText} mode. Invocation typically occurs from process calls or API execution contexts.";
        }

        if (string.Equals(model.ProcessCategory, "Dialog", StringComparison.OrdinalIgnoreCase))
        {
            return $"This dialog runs in {modeText} mode within a user-guided interaction path.";
        }

        return model.ExecutionMode == ExecutionMode.Synchronous
            ? "This is a real-time workflow. Actions execute in the transaction context."
            : "This is a background workflow. Actions execute asynchronously after the triggering event.";
    }

    private static string BuildBusinessTriggerSummary(WorkflowTrigger trigger)
    {
        var events = new List<string>(3);
        if (trigger.OnCreate)
        {
            events.Add("when a new record is created");
        }

        if (trigger.OnUpdate)
        {
            events.Add("when a record is updated");
        }

        if (trigger.OnDelete)
        {
            events.Add("when a record is deleted");
        }

        if (events.Count == 0)
        {
            return "when started manually or by another process";
        }

        if (events.Count == 1)
        {
            return events[0];
        }

        return string.Join(", ", events.Take(events.Count - 1)) + " and " + events[^1];
    }

    private static bool HasNoRiskPlaceholder(IReadOnlyList<string> risks)
    {
        return risks.Count == 1
            && risks[0].Contains("No high-risk indicators", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveNodeLabel(DependencyGraphModel model, string nodeId)
    {
        var match = model.Nodes.FirstOrDefault(x => string.Equals(x.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        return match?.DisplayName ?? nodeId;
    }

    private static List<string[]> BuildFieldReadRows(WorkflowDocumentModel model)
    {
        var rows = new List<string[]>();

        if (model.Trigger.AttributeFilters.Count > 0)
        {
            foreach (var filter in model.Trigger.AttributeFilters)
            {
                rows.Add([
                    "Trigger",
                    model.Trigger.PrimaryEntity,
                    filter,
                    "Trigger attribute filter"]);
            }
        }

        foreach (var step in model.Steps)
        {
            foreach (var attribute in step.Attributes)
            {
                if (!LooksLikeReadAttribute(attribute.Name, attribute.Value))
                {
                    continue;
                }

                rows.Add([
                    $"{step.Sequence}. {step.Label}",
                    ResolveEntityName(model, step),
                    attribute.Name,
                    attribute.Value]);
            }
        }

        return rows;
    }

    private static List<string[]> BuildFieldWriteRows(WorkflowDocumentModel model)
    {
        var rows = new List<string[]>();

        foreach (var step in model.Steps)
        {
            foreach (var attribute in step.Attributes)
            {
                if (!LooksLikeWriteAttribute(attribute.Name, attribute.Value))
                {
                    continue;
                }

                var target = step.Attributes
                    .FirstOrDefault(x => string.Equals(x.Name, "Target", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Name, "TargetAttribute", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Name, "Field", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Name, "Attribute", StringComparison.OrdinalIgnoreCase))
                    ?.Value ?? attribute.Name;

                rows.Add([
                    $"{step.Sequence}. {step.Label}",
                    ResolveEntityName(model, step),
                    attribute.Name,
                    attribute.Value,
                    target]);
            }
        }

        return rows;
    }

    private static bool LooksLikeReadAttribute(string name, string value)
    {
        var text = $"{name} {value}";
        return text.Contains("input", StringComparison.OrdinalIgnoreCase)
            || text.Contains("source", StringComparison.OrdinalIgnoreCase)
            || text.Contains("primaryentity", StringComparison.OrdinalIgnoreCase)
            || text.Contains("lookup", StringComparison.OrdinalIgnoreCase)
            || text.Contains("attribute", StringComparison.OrdinalIgnoreCase)
            || text.Contains("field", StringComparison.OrdinalIgnoreCase)
            || text.Contains("column", StringComparison.OrdinalIgnoreCase)
            || text.Contains("condition", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWriteAttribute(string name, string value)
    {
        var text = $"{name} {value}";
        return text.Contains("target", StringComparison.OrdinalIgnoreCase)
            || text.Contains("set", StringComparison.OrdinalIgnoreCase)
            || text.Contains("update", StringComparison.OrdinalIgnoreCase)
            || text.Contains("create", StringComparison.OrdinalIgnoreCase)
            || text.Contains("value", StringComparison.OrdinalIgnoreCase)
            || text.Contains("owner", StringComparison.OrdinalIgnoreCase)
            || text.Contains("regarding", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveEntityName(WorkflowDocumentModel model, WorkflowStepDetail step)
    {
        foreach (var candidate in step.Attributes)
        {
            if (string.Equals(candidate.Name, "Entity", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Name, "EntityName", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Name, "PrimaryEntity", StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Name, "TargetEntity", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value;
            }
        }

        return model.Trigger.PrimaryEntity;
    }

    private static void WriteStepInventoryCsv(string outputFolder, IReadOnlyList<WorkflowDocumentModel> workflowDocuments)
    {
        var csvPath = Path.Combine(outputFolder, "workflow-step-inventory.csv");
        using var writer = new StreamWriter(csvPath, false);
        writer.WriteLine("Workflow,StepNumber,StepId,StepType,StepLabel,Synthetic,IncomingCount,OutgoingCount,IncomingPaths,OutgoingPaths,Attributes");

        foreach (var workflow in workflowDocuments)
        {
            foreach (var step in workflow.Steps)
            {
                var incoming = string.Join(" | ", step.IncomingPaths);
                var outgoing = string.Join(" | ", step.OutgoingPaths);
                var attributes = string.Join(" | ", step.Attributes.Select(a => $"{a.Name}={a.Value}"));

                writer.WriteLine(string.Join(",",
                    EscapeCsv(workflow.WorkflowName),
                    step.Sequence,
                    EscapeCsv(step.StepId),
                    EscapeCsv(step.StepType),
                    EscapeCsv(step.Label),
                    step.IsSynthetic,
                    step.IncomingPaths.Count,
                    step.OutgoingPaths.Count,
                    EscapeCsv(incoming),
                    EscapeCsv(outgoing),
                    EscapeCsv(attributes)));
            }
        }
    }

    private static string EscapeCsv(string? value)
    {
        var normalized = value ?? string.Empty;
        if (!normalized.Contains(',') && !normalized.Contains('"') && !normalized.Contains('\n') && !normalized.Contains('\r'))
        {
            return normalized;
        }

        return $"\"{normalized.Replace("\"", "\"\"")}\"";
    }

    private static void EnsureStyles(MainDocumentPart main)
    {
        if (main.StyleDefinitionsPart?.Styles is not null)
        {
            return;
        }

        var stylesPart = main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(
                    new RunProperties(
                        new RunFonts
                        {
                            Ascii = "Arial",
                            HighAnsi = "Arial",
                            ComplexScript = "Arial",
                            EastAsia = "Arial"
                        },
                        new FontSize { Val = "22" },
                        new FontSizeComplexScript { Val = "22" })),
                new ParagraphPropertiesDefault(new ParagraphProperties())),

            new Style(
                new StyleName { Val = "Title" },
                new NextParagraphStyle { Val = "BodyText" },
                new PrimaryStyle(),
                new StyleRunProperties(
                    new Bold(),
                    new Color { Val = "1F4E79" },
                    new FontSize { Val = "52" },
                    new FontSizeComplexScript { Val = "52" }))
            { Type = StyleValues.Paragraph, StyleId = "Title" },

            new Style(
                new StyleName { Val = "Heading 1" },
                new BasedOn { Val = "BodyText" },
                new NextParagraphStyle { Val = "BodyText" },
                new PrimaryStyle(),
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "320", After = "180" },
                    new OutlineLevel { Val = 0 }),
                new StyleRunProperties(
                    new Bold(),
                    new Color { Val = "1F4E79" },
                    new FontSize { Val = "36" },
                    new FontSizeComplexScript { Val = "36" }))
            { Type = StyleValues.Paragraph, StyleId = "Heading1" },

            new Style(
                new StyleName { Val = "Heading 2" },
                new BasedOn { Val = "BodyText" },
                new NextParagraphStyle { Val = "BodyText" },
                new PrimaryStyle(),
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "220", After = "120" },
                    new OutlineLevel { Val = 1 }),
                new StyleRunProperties(
                    new Bold(),
                    new Color { Val = "2E75B6" },
                    new FontSize { Val = "28" },
                    new FontSizeComplexScript { Val = "28" }))
            { Type = StyleValues.Paragraph, StyleId = "Heading2" },

            new Style(
                new StyleName { Val = "Heading 3" },
                new BasedOn { Val = "BodyText" },
                new NextParagraphStyle { Val = "BodyText" },
                new PrimaryStyle(),
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "180", After = "100" },
                    new OutlineLevel { Val = 2 }),
                new StyleRunProperties(
                    new Bold(),
                    new Color { Val = "1F4E79" },
                    new FontSize { Val = "24" },
                    new FontSizeComplexScript { Val = "24" }))
            { Type = StyleValues.Paragraph, StyleId = "Heading3" },

            new Style(
                new StyleName { Val = "Body Text" },
                new PrimaryStyle(),
                new ParagraphProperties(new SpacingBetweenLines { After = "120", Line = "300", LineRule = LineSpacingRuleValues.Auto }),
                new StyleRunProperties(
                    new Color { Val = "202020" },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" }))
            { Type = StyleValues.Paragraph, StyleId = "BodyText", Default = OnOffValue.FromBoolean(true) },

            new Style(
                new StyleName { Val = "Subtitle" },
                new BasedOn { Val = "BodyText" },
                new StyleRunProperties(
                    new Color { Val = "3F6FA3" },
                    new FontSize { Val = "28" },
                    new FontSizeComplexScript { Val = "28" }))
            { Type = StyleValues.Paragraph, StyleId = "Subtitle" },

            new Style(
                new StyleName { Val = "Caption" },
                new BasedOn { Val = "BodyText" },
                new StyleRunProperties(
                    new Color { Val = "6F6F6F" },
                    new Italic(),
                    new FontSize { Val = "20" },
                    new FontSizeComplexScript { Val = "20" }))
            { Type = StyleValues.Paragraph, StyleId = "Caption" },

            new Style(
                new StyleName { Val = "Emphasis" },
                new BasedOn { Val = "BodyText" },
                new StyleRunProperties(
                    new Bold(),
                    new Color { Val = "1F4E79" }))
            { Type = StyleValues.Paragraph, StyleId = "Emphasis" });

        stylesPart.Styles.Save();
    }

}

