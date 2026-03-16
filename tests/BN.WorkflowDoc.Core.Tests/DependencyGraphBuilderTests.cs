using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class DependencyGraphBuilderTests
{
    [Fact]
    public void Build_WithCrossWorkflowReference_CreatesWorkflowReferenceEdge()
    {
        var builder = new DependencyGraphBuilder();

        var workflowA = CreateWorkflow(
            "Create Case",
            "createcase",
            [
                new WorkflowDependency("ChildWorkflow", "Case Escalation", "wf-2"),
                new WorkflowDependency("ExternalCall", "ERP API", "dep-1")
            ]);

        var workflowB = CreateWorkflow(
            "Case Escalation",
            "caseescalation",
            Array.Empty<WorkflowDependency>());

        var graph = builder.Build([workflowA, workflowB]);

        Assert.Equal(3, graph.Nodes.Count);
        Assert.Equal(2, graph.Edges.Count);
        Assert.Contains(graph.Edges, e => e.RelationshipType == "WorkflowReference");
        Assert.Contains(graph.Edges, e => e.RelationshipType == "ExternalCall");
        Assert.Contains("cross-workflow", graph.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithNoWorkflows_ReturnsEmptyGraph()
    {
        var builder = new DependencyGraphBuilder();

        var graph = builder.Build(Array.Empty<WorkflowDefinition>());

        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Contains("No workflow dependencies", graph.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkflowDefinition CreateWorkflow(
        string displayName,
        string logicalName,
        IReadOnlyList<WorkflowDependency> dependencies)
    {
        return new WorkflowDefinition(
            WorkflowId: Guid.NewGuid(),
            LogicalName: logicalName,
            DisplayName: displayName,
            Category: "classic",
            Scope: "organization",
            Owner: null,
            ExecutionMode: ExecutionMode.Asynchronous,
            Trigger: new WorkflowTrigger("incident", true, false, false, Array.Empty<string>(), null),
            StageGraph: new WorkflowStageGraph(
                [new WorkflowNode("n1", WorkflowComponentType.Action, "Start", new Dictionary<string, string>())],
                Array.Empty<WorkflowEdge>()),
            RootCondition: null,
            Dependencies: dependencies,
            Warnings: Array.Empty<ProcessingWarning>());
    }
}
