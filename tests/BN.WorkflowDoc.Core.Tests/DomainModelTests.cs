using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class DomainModelTests
{
    [Fact]
    public void WorkflowStageGraph_Empty_HasNoNodesOrEdges()
    {
        Assert.Empty(WorkflowStageGraph.Empty.Nodes);
        Assert.Empty(WorkflowStageGraph.Empty.Edges);
    }

    [Fact]
    public void ParseResult_AllowsPartialSuccessWithWarnings()
    {
        var warning = new ProcessingWarning("UNSUPPORTED_ACTION", "Unsupported action type", "workflow.xml");
        var result = new ParseResult<WorkflowDocumentModel>(
            ProcessingStatus.PartialSuccess,
            new WorkflowDocumentModel(
                "Sample Workflow",
                "Updates status fields",
                new WorkflowTrigger("account", true, false, false, Array.Empty<string>(), null),
                ExecutionMode.Asynchronous,
                Array.Empty<TraceableNarrativeSection>(),
                Array.Empty<WorkflowStepDetail>(),
                Array.Empty<WorkflowTransitionDetail>(),
                Array.Empty<DiagramGraph>(),
                new[] { warning }),
            new[] { warning });

        Assert.Equal(ProcessingStatus.PartialSuccess, result.Status);
        Assert.Single(result.Warnings);
        Assert.NotNull(result.Value);
    }
}

