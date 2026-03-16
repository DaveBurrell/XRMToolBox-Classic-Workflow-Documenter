using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class PortfolioDocumentationPipelineTests
{
    [Fact]
    public async Task GenerateAsync_AggregatesSolutionResults_AndBuildsPortfolioSummary()
    {
        var solutionA = BuildDocumentationResult("Solution A", "Approval", 48, "High");
        var solutionB = BuildDocumentationResult("Solution B", "Follow Up", 84, "Low");

        var warningsA = new[]
        {
            new ProcessingWarning("A1", "warning", "a", false, WarningCategory.Validation, WarningSeverity.Warning)
        };

        var fakePipeline = new FakeWorkflowDocumentationPipeline(new Dictionary<string, ParseResult<DocumentationGenerationResult>>(StringComparer.OrdinalIgnoreCase)
        {
            ["a.zip"] = new ParseResult<DocumentationGenerationResult>(ProcessingStatus.PartialSuccess, solutionA, warningsA),
            ["b.zip"] = new ParseResult<DocumentationGenerationResult>(ProcessingStatus.Success, solutionB, Array.Empty<ProcessingWarning>())
        });

        var sut = new PortfolioDocumentationPipeline(fakePipeline);
        var result = await sut.GenerateAsync(["a.zip", "b.zip"]);

        Assert.Equal(ProcessingStatus.PartialSuccess, result.Status);
        var value = Assert.IsType<BatchDocumentationResult>(result.Value);
        Assert.Equal(2, value.Results.Count);
        Assert.Equal(2, value.PortfolioSummary.Solutions.Count);
        Assert.Single(value.Warnings);
        Assert.NotNull(value.PortfolioSummary.CrossSolutionDependencyGraph);
        Assert.Contains(value.PortfolioSummary.TopRiskWorkflows, x => x.WorkflowName.Contains("Solution A", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_ReturnsFailed_WhenNoInputsProvided()
    {
        var sut = new PortfolioDocumentationPipeline(new FakeWorkflowDocumentationPipeline(new Dictionary<string, ParseResult<DocumentationGenerationResult>>()));

        var result = await sut.GenerateAsync(Array.Empty<string>());

        Assert.Equal(ProcessingStatus.Failed, result.Status);
        Assert.Null(result.Value);
        Assert.Contains(result.Warnings, x => x.Code == "BATCH_INPUT_EMPTY");
    }

    private static DocumentationGenerationResult BuildDocumentationResult(
        string solutionName,
        string workflowName,
        int qualityScore,
        string riskBand)
    {
        var card = new OverviewWorkflowCard(
            WorkflowName: workflowName,
            ProcessCategory: "Workflow",
            IsOnDemand: false,
            Purpose: "Purpose",
            TriggerSummary: "account (update)",
            ExecutionMode: ExecutionMode.Asynchronous,
            ComplexityScore: 10,
            Dependencies: Array.Empty<string>(),
            KeyRisks: Array.Empty<string>(),
            QualityScore: new WorkflowQualityScore(
                qualityScore,
                riskBand,
                new QualityScoreBreakdown(4, 10, 0, 0),
                $"Quality score {qualityScore}"),
            WarningCodes: Array.Empty<string>());

        var graph = new DependencyGraphModel(
            Nodes:
            [
                new DependencyGraphNode("wf:1", workflowName, "Workflow", 0, 1),
                new DependencyGraphNode("dep:1", "ERP API", "ExternalCall", 1, 0)
            ],
            Edges:
            [
                new DependencyGraphEdge("wf:1", "dep:1", "ExternalCall")
            ],
            Summary: "2 nodes");

        return new DocumentationGenerationResult(
            Package: new SolutionPackage($"{solutionName}.zip", "c:/tmp", "1.0", Array.Empty<WorkflowDefinition>(), Array.Empty<ProcessingWarning>()),
            WorkflowDocuments:
            [
                new WorkflowDocumentModel(
                    WorkflowName: workflowName,
                    ProcessCategory: "Workflow",
                    IsOnDemand: false,
                    Purpose: "Purpose",
                    Trigger: new WorkflowTrigger("account", false, true, false, Array.Empty<string>(), null),
                    ExecutionMode: ExecutionMode.Asynchronous,
                    Sections: Array.Empty<TraceableNarrativeSection>(),
                    Steps: Array.Empty<WorkflowStepDetail>(),
                        Transitions: Array.Empty<WorkflowTransitionDetail>(),
                    Diagrams: Array.Empty<DiagramGraph>(),
                    Warnings: Array.Empty<ProcessingWarning>())
            ],
            OverviewDocument: new OverviewDocumentModel(solutionName, [card], Array.Empty<ProcessingWarning>(), graph),
            Warnings: Array.Empty<ProcessingWarning>());
    }

    private sealed class FakeWorkflowDocumentationPipeline : IWorkflowDocumentationPipeline
    {
        private readonly IReadOnlyDictionary<string, ParseResult<DocumentationGenerationResult>> _results;

        public FakeWorkflowDocumentationPipeline(IReadOnlyDictionary<string, ParseResult<DocumentationGenerationResult>> results)
        {
            _results = results;
        }

        public Task<ParseResult<DocumentationGenerationResult>> GenerateAsync(string solutionZipPath, CancellationToken cancellationToken = default)
        {
            if (_results.TryGetValue(solutionZipPath, out var result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(new ParseResult<DocumentationGenerationResult>(
                ProcessingStatus.Failed,
                null,
                [new ProcessingWarning("MISSING", "input missing", solutionZipPath, true, WarningCategory.Input, WarningSeverity.Error)],
                "input missing"));
        }

        public Task<ParseResult<DocumentationGenerationResult>> GenerateAsync(WorkflowDocumentationRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ParseResult<DocumentationGenerationResult>(
                ProcessingStatus.Failed,
                null,
                [new ProcessingWarning("NOT_SUPPORTED", "In-memory requests are not configured in this fake pipeline.", request.SourceName, true, WarningCategory.Input, WarningSeverity.Error)],
                "not supported"));
        }
    }
}
