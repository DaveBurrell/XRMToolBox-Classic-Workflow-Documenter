using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

/// <summary>
/// Converts dependency graph contracts into a renderable diagram graph.
/// </summary>
public interface IDependencyGraphDiagramMapper
{
    DiagramGraph Map(DependencyGraphModel dependencyGraph);
}

public sealed class DependencyGraphDiagramMapper : IDependencyGraphDiagramMapper
{
    public DiagramGraph Map(DependencyGraphModel dependencyGraph)
    {
        if (dependencyGraph.Nodes.Count == 0)
        {
            return new DiagramGraph(
                DiagramType.Flowchart,
                Array.Empty<DiagramNode>(),
                Array.Empty<DiagramEdge>(),
                "No dependency relationships were discovered.");
        }

        var nodes = dependencyGraph.Nodes
            .Select(node => new DiagramNode(
                Id: node.NodeId,
                Label: node.DisplayName,
                Lane: node.NodeType,
                Width: 220,
                Height: 70))
            .ToArray();

        var edges = dependencyGraph.Edges
            .Select(edge => new DiagramEdge(
                FromNodeId: edge.SourceNodeId,
                ToNodeId: edge.TargetNodeId,
                Label: edge.RelationshipType))
            .ToArray();

        return new DiagramGraph(
            DiagramType.Flowchart,
            nodes,
            edges,
            dependencyGraph.Summary);
    }
}
