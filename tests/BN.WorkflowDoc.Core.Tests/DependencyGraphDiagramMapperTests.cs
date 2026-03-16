using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class DependencyGraphDiagramMapperTests
{
    [Fact]
    public void Map_WithDependencyGraph_ProducesFlowchartDiagram()
    {
        var mapper = new DependencyGraphDiagramMapper();
        var model = new DependencyGraphModel(
            Nodes:
            [
                new DependencyGraphNode("wf:1", "Order Approval", "Workflow", 0, 1),
                new DependencyGraphNode("dep:1", "ERP API", "ExternalCall", 1, 0)
            ],
            Edges:
            [
                new DependencyGraphEdge("wf:1", "dep:1", "ExternalCall")
            ],
            Summary: "2 node(s), 1 edge(s), 0 cross-workflow link(s).");

        var diagram = mapper.Map(model);

        Assert.Equal(DiagramType.Flowchart, diagram.Type);
        Assert.Equal(2, diagram.Nodes.Count);
        Assert.Single(diagram.Edges);
        Assert.Equal("ExternalCall", diagram.Edges[0].Label);
        Assert.Equal(model.Summary, diagram.Caption);
    }

    [Fact]
    public void Map_WithEmptyGraph_ProducesPlaceholderDiagram()
    {
        var mapper = new DependencyGraphDiagramMapper();
        var model = new DependencyGraphModel(
            Array.Empty<DependencyGraphNode>(),
            Array.Empty<DependencyGraphEdge>(),
            "No workflow dependencies were discovered.");

        var diagram = mapper.Map(model);

        Assert.Empty(diagram.Nodes);
        Assert.Empty(diagram.Edges);
        Assert.Contains("No dependency relationships", diagram.Caption, StringComparison.OrdinalIgnoreCase);
    }
}
