using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class WorkflowDocumentationPipelineTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsWorkflowAndOverviewModels_WhenAllStagesSucceed()
    {
        var workflow = CreateWorkflow("Account Follow Up");
        var package = new SolutionPackage("x.zip", "c:\\tmp", "1.0", new[] { workflow }, Array.Empty<ProcessingWarning>());

        var extraction = new ParseResult<SolutionPackage>(ProcessingStatus.Success, package, Array.Empty<ProcessingWarning>());
        var workflowModel = new WorkflowDocumentModel(
            "Account Follow Up",
            "Workflow",
            false,
            "Purpose",
            workflow.Trigger,
            workflow.ExecutionMode,
            Array.Empty<TraceableNarrativeSection>(),
            Array.Empty<WorkflowStepDetail>(),
            Array.Empty<WorkflowTransitionDetail>(),
            Array.Empty<DiagramGraph>(),
            Array.Empty<ProcessingWarning>());

        var workflowDoc = new ParseResult<WorkflowDocumentModel>(ProcessingStatus.Success, workflowModel, Array.Empty<ProcessingWarning>());
        var overviewDoc = new ParseResult<OverviewDocumentModel>(
            ProcessingStatus.Success,
            new OverviewDocumentModel("x", new[]
            {
                new OverviewWorkflowCard("Account Follow Up", "Workflow", false, "Purpose", "account (update)", ExecutionMode.Asynchronous, 5, Array.Empty<string>(), Array.Empty<string>())
            }, Array.Empty<ProcessingWarning>()),
            Array.Empty<ProcessingWarning>());

        var sut = new WorkflowDocumentationPipeline(
            new FakeExtractionPipeline(extraction),
            new FakeWorkflowDocumentBuilder(workflowDoc),
            new FakeOverviewBuilder(overviewDoc));

        var result = await sut.GenerateAsync("x.zip");

        Assert.Equal(ProcessingStatus.Success, result.Status);
        var value = Assert.IsType<DocumentationGenerationResult>(result.Value);
        Assert.Single(value.WorkflowDocuments);
        Assert.Single(value.OverviewDocument.Workflows);
        var perf = Assert.IsType<PipelinePerformanceMetrics>(value.Performance);
        Assert.True(perf.Duration >= TimeSpan.Zero);
        Assert.True(perf.ParallelWorkersUsed >= 1);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsFailed_WhenWorkflowDocumentBuildFails()
    {
        var workflow = CreateWorkflow("Account Follow Up");
        var package = new SolutionPackage("x.zip", "c:\\tmp", "1.0", new[] { workflow }, Array.Empty<ProcessingWarning>());

        var extraction = new ParseResult<SolutionPackage>(ProcessingStatus.Success, package, Array.Empty<ProcessingWarning>());
        var workflowDoc = new ParseResult<WorkflowDocumentModel>(
            ProcessingStatus.Failed,
            null,
            new[] { new ProcessingWarning("DOC_FAIL", "Document generation failed.", "workflow") },
            "Document generation failed.");

        var overviewDoc = new ParseResult<OverviewDocumentModel>(
            ProcessingStatus.Success,
            new OverviewDocumentModel("x", Array.Empty<OverviewWorkflowCard>(), Array.Empty<ProcessingWarning>()),
            Array.Empty<ProcessingWarning>());

        var sut = new WorkflowDocumentationPipeline(
            new FakeExtractionPipeline(extraction),
            new FakeWorkflowDocumentBuilder(workflowDoc),
            new FakeOverviewBuilder(overviewDoc));

        var result = await sut.GenerateAsync("x.zip");

        Assert.Equal(ProcessingStatus.Failed, result.Status);
        Assert.Null(result.Value);
    }

    private static WorkflowDefinition CreateWorkflow(string name)
    {
        return new WorkflowDefinition(
            WorkflowId: Guid.NewGuid(),
            LogicalName: name.Replace(" ", string.Empty, StringComparison.Ordinal),
            DisplayName: name,
            Category: "classic",
            Scope: "organization",
            Owner: null,
            IsOnDemand: false,
            ExecutionMode: ExecutionMode.Asynchronous,
            Trigger: new WorkflowTrigger("account", false, true, false, Array.Empty<string>(), null),
            StageGraph: WorkflowStageGraph.Empty,
            RootCondition: null,
            Dependencies: Array.Empty<WorkflowDependency>(),
            Warnings: Array.Empty<ProcessingWarning>());
    }

    private sealed class FakeExtractionPipeline : IWorkflowExtractionPipeline
    {
        private readonly ParseResult<SolutionPackage> _result;

        public FakeExtractionPipeline(ParseResult<SolutionPackage> result) => _result = result;

        public Task<ParseResult<SolutionPackage>> ExtractAsync(string solutionZipPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class FakeWorkflowDocumentBuilder : IWorkflowDocumentBuilder
    {
        private readonly ParseResult<WorkflowDocumentModel> _result;

        public FakeWorkflowDocumentBuilder(ParseResult<WorkflowDocumentModel> result) => _result = result;

        public Task<ParseResult<WorkflowDocumentModel>> BuildAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class FakeOverviewBuilder : IOverviewDocumentBuilder
    {
        private readonly ParseResult<OverviewDocumentModel> _result;

        public FakeOverviewBuilder(ParseResult<OverviewDocumentModel> result) => _result = result;

        public Task<ParseResult<OverviewDocumentModel>> BuildAsync(string solutionName, IReadOnlyList<WorkflowDefinition> workflows, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}

