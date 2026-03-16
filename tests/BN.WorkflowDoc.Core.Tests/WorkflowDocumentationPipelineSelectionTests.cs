using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class WorkflowDocumentationPipelineSelectionTests
{
    [Fact]
    public async Task GenerateAsync_ForSelection_BuildsWorkflowAndOverviewDocuments()
    {
        var workflow = new WorkflowDefinition(
            WorkflowId: Guid.NewGuid(),
            LogicalName: "AccountApproval",
            DisplayName: "Account Approval",
            Category: "Workflow",
            Scope: "Organization",
            Owner: "System",
            ExecutionMode: ExecutionMode.Asynchronous,
            Trigger: new WorkflowTrigger("account", true, true, false, ["statuscode"], "Create or update"),
            StageGraph: new WorkflowStageGraph(
                [
                    new WorkflowNode("trigger", WorkflowComponentType.Trigger, "Trigger", new Dictionary<string, string>()),
                    new WorkflowNode("n1", WorkflowComponentType.Action, "Send Email", new Dictionary<string, string>())
                ],
                [new WorkflowEdge("trigger", "n1")]),
            RootCondition: null,
            Dependencies: Array.Empty<WorkflowDependency>(),
            Warnings: Array.Empty<ProcessingWarning>());

        var sut = new WorkflowDocumentationPipeline(
            new FakeExtractionPipeline(),
            new DeterministicWorkflowDocumentBuilder(),
            new DeterministicOverviewDocumentBuilder());

        var result = await sut.GenerateAsync(new WorkflowDocumentationRequest("Live Selection", [workflow]));

        Assert.Equal(ProcessingStatus.Success, result.Status);
        var value = Assert.IsType<DocumentationGenerationResult>(result.Value);
        Assert.Equal("Live Selection", value.OverviewDocument.SolutionName);
        Assert.Single(value.WorkflowDocuments);
        Assert.Equal("live", value.Package.Version);
        Assert.Equal(string.Empty, value.Package.ExtractedPath);
    }

    [Fact]
    public async Task GenerateAsync_ForEmptySelection_ReturnsFailure()
    {
        var sut = new WorkflowDocumentationPipeline(
            new FakeExtractionPipeline(),
            new DeterministicWorkflowDocumentBuilder(),
            new DeterministicOverviewDocumentBuilder());

        var result = await sut.GenerateAsync(new WorkflowDocumentationRequest("Live Selection", Array.Empty<WorkflowDefinition>()));

        Assert.Equal(ProcessingStatus.Failed, result.Status);
        Assert.Null(result.Value);
        Assert.Contains(result.Warnings, warning => warning.Code == "WORKFLOW_SELECTION_EMPTY");
    }

    private sealed class FakeExtractionPipeline : IWorkflowExtractionPipeline
    {
        public Task<ParseResult<SolutionPackage>> ExtractAsync(string solutionZipPath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}