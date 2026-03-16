using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Packaging;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class DocxArtifactWriterTests
{
    [Fact]
    public async Task WriteAsync_CreatesWorkflowAndOverviewDocxFiles()
    {
        var outputFolder = Path.Combine(Path.GetTempPath(), "BdWorkflowDocTests", Guid.NewGuid().ToString("N"));

        var workflowDocument = new WorkflowDocumentModel(
            WorkflowName: "Sample Workflow",
            Purpose: "Purpose text",
            Trigger: new WorkflowTrigger("account", false, true, false, Array.Empty<string>(), null),
            ExecutionMode: ExecutionMode.Asynchronous,
            Sections: new[]
            {
                new TraceableNarrativeSection(
                    "Process Logic",
                    "Logic narrative",
                    new[] { new SourceTrace("Condition", "Workflow/Filter", null) })
            },
            Steps: new[]
            {
                new WorkflowStepDetail(
                    1,
                    "trigger",
                    "Trigger",
                    "Trigger",
                    false,
                    Array.Empty<string>(),
                    new[] { "To Start" },
                    Array.Empty<WorkflowStepAttribute>(),
                    "This Trigger step starts the workflow."),
                new WorkflowStepDetail(
                    2,
                    "n1",
                    "Action",
                    "Start",
                    false,
                    new[] { "From Trigger" },
                    Array.Empty<string>(),
                    new[] { new WorkflowStepAttribute("Operation", "Update") },
                    "This Action step updates the record.")
            },
            Transitions: new[]
            {
                new WorkflowTransitionDetail(1, "trigger", "Trigger", "n1", "Start", null, "Trigger routes to Start.")
            },
            Diagrams: new[]
            {
                new DiagramGraph(
                    DiagramType.Flowchart,
                    Enumerable.Range(1, 55)
                        .Select(i => new DiagramNode($"n{i}", $"Start {i}", "Flow", 200, 60))
                        .ToArray(),
                    Enumerable.Range(1, 54)
                        .Select(i => new DiagramEdge($"n{i}", $"n{i + 1}", null))
                        .ToArray(),
                    "Flowchart caption"),
                new DiagramGraph(
                    DiagramType.Swimlane,
                    new[] { new DiagramNode("n1", "Start", "Trigger Context", 200, 60) },
                    Array.Empty<DiagramEdge>(),
                    "Swimlane caption")
            },
            Warnings: Array.Empty<ProcessingWarning>());

        var overviewDocument = new OverviewDocumentModel(
            SolutionName: "Sample Solution",
            Workflows: new[]
            {
                new OverviewWorkflowCard(
                    "Sample Workflow",
                    "Purpose text",
                    "account (update)",
                    ExecutionMode.Asynchronous,
                    8,
                    Array.Empty<string>(),
                    new[] { "No high-risk indicators detected from extracted metadata." },
                    new WorkflowQualityScore(
                        OverallScore: 82,
                        RiskBand: "Low",
                        Breakdown: new QualityScoreBreakdown(10, 8, 0, 0),
                        Summary: "Quality score 82/100 (Low risk)."),
                    WarningCodes: Array.Empty<string>())
            },
            GlobalWarnings: Array.Empty<ProcessingWarning>(),
            DependencyGraph: new DependencyGraphModel(
                Nodes: new[]
                {
                    new DependencyGraphNode("wf:1", "Sample Workflow", "Workflow", 0, 1),
                    new DependencyGraphNode("dep:1", "ERP API", "ExternalCall", 1, 0)
                },
                Edges: new[]
                {
                    new DependencyGraphEdge("wf:1", "dep:1", "ExternalCall", "ext-1")
                },
                Summary: "2 node(s), 1 edge(s), 0 cross-workflow link(s)."));

        var generation = new DocumentationGenerationResult(
            Package: new SolutionPackage("sample.zip", outputFolder, "1.0.0.0", Array.Empty<WorkflowDefinition>(), Array.Empty<ProcessingWarning>()),
            WorkflowDocuments: new[] { workflowDocument },
            OverviewDocument: overviewDocument,
            Warnings: Array.Empty<ProcessingWarning>());

        var writer = new OpenXmlDocxArtifactWriter();
        var result = await writer.WriteAsync(generation, outputFolder);

        Assert.Equal(ProcessingStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value!.WorkflowFiles);
        Assert.True(File.Exists(result.Value.WorkflowFiles[0]));
        Assert.True(File.Exists(result.Value.OverviewFile));
        Assert.True(new FileInfo(result.Value.WorkflowFiles[0]).Length > 0);
        Assert.True(new FileInfo(result.Value.OverviewFile).Length > 0);

        using var workflowDoc = WordprocessingDocument.Open(result.Value.WorkflowFiles[0], false);
        var text = workflowDoc.MainDocumentPart!.Document.InnerText;
        Assert.Contains("Table of Contents", text);
        Assert.Contains("Diagrams", text);
        Assert.Contains("Flowchart", text);
        Assert.Contains("Swimlane", text);
        Assert.Contains("Fields Read", text);
        Assert.Contains("Fields Set / Updated", text);
        Assert.Contains("Process Flow Steps", text);
        Assert.Contains("Transition Matrix", text);
        Assert.Contains("Full Step Breakdown", text);
        Assert.Contains("Flowchart View 1: Start 1 to Start 24", text);
        Assert.Contains("Step 2: Start", text);
        Assert.Contains("Captured step metadata", text);
        Assert.Contains("Appendix: Warnings", text);
        Assert.True(workflowDoc.MainDocumentPart.ImageParts.Any());
        Assert.True(workflowDoc.MainDocumentPart.ImageParts.Count() > 2);
        var workflowOrientations = workflowDoc.MainDocumentPart.Document.Body!
            .Descendants<SectionProperties>()
            .Select(section => section.GetFirstChild<PageSize>()?.Orient?.Value ?? PageOrientationValues.Portrait)
            .ToArray();
        Assert.Contains(PageOrientationValues.Portrait, workflowOrientations);
        Assert.Contains(PageOrientationValues.Landscape, workflowOrientations);
        var firstTable = workflowDoc.MainDocumentPart.Document.Body.Elements<Table>().FirstOrDefault();
        Assert.NotNull(firstTable);
        Assert.Equal(TableLayoutValues.Fixed, firstTable!.GetFirstChild<TableProperties>()?.GetFirstChild<TableLayout>()?.Type?.Value);

        using var overviewDoc = WordprocessingDocument.Open(result.Value.OverviewFile, false);
        var overviewText = overviewDoc.MainDocumentPart!.Document.InnerText;
        Assert.Contains("Quality Assessment Summary", overviewText);
        Assert.Contains("Dependency Graph Overview", overviewText);
        Assert.Contains("Workflow Risk Matrix", overviewText);
        Assert.Contains("Appendix: Detailed Workflow Step Inventory", overviewText);
        Assert.True(overviewDoc.MainDocumentPart.ImageParts.Any());
        var overviewOrientations = overviewDoc.MainDocumentPart.Document.Body!
            .Descendants<SectionProperties>()
            .Select(section => section.GetFirstChild<PageSize>()?.Orient?.Value ?? PageOrientationValues.Portrait)
            .ToArray();
        Assert.Contains(PageOrientationValues.Portrait, overviewOrientations);
        Assert.Contains(PageOrientationValues.Landscape, overviewOrientations);

        var csvPath = Path.Combine(outputFolder, "workflow-step-inventory.csv");
        Assert.True(File.Exists(csvPath));
        var csvText = await File.ReadAllTextAsync(csvPath);
        Assert.Contains("StepNumber", csvText);
        Assert.Contains("Sample Workflow", csvText);
    }
}

