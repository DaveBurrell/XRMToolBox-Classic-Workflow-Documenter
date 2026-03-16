namespace BN.WorkflowDoc.Core.Domain;

public enum DiagramType
{
    Flowchart,
    Swimlane
}

public sealed record DiagramGraph(
    DiagramType Type,
    IReadOnlyList<DiagramNode> Nodes,
    IReadOnlyList<DiagramEdge> Edges,
    string Caption);

public sealed record DiagramNode(
    string Id,
    string Label,
    string Lane,
    double Width,
    double Height,
    WorkflowComponentType ComponentType = WorkflowComponentType.Action,
    IReadOnlyList<string>? DetailLines = null);

public sealed record DiagramEdge(
    string FromNodeId,
    string ToNodeId,
    string? Label);

