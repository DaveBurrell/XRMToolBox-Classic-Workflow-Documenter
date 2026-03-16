namespace BN.WorkflowDoc.Core.Domain;

public sealed record WorkflowStageGraph(
    IReadOnlyList<WorkflowNode> Nodes,
    IReadOnlyList<WorkflowEdge> Edges)
{
    public static WorkflowStageGraph Empty { get; } = new(Array.Empty<WorkflowNode>(), Array.Empty<WorkflowEdge>());
}

public sealed record WorkflowNode(
    string Id,
    WorkflowComponentType ComponentType,
    string Label,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record WorkflowEdge(
    string FromNodeId,
    string ToNodeId,
    string? ConditionLabel = null);

