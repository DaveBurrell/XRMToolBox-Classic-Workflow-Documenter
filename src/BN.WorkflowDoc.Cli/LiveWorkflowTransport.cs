using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Cli;

internal sealed record LiveWorkflowDocumentationRequest(
    string SourceName,
    IReadOnlyList<WorkflowDefinitionPayload> Workflows,
    IReadOnlyList<ProcessingWarningPayload>? Warnings = null);

internal sealed record WorkflowDefinitionPayload(
    Guid WorkflowId,
    string LogicalName,
    string DisplayName,
    string Category,
    string Scope,
    string? Owner,
    string ExecutionMode,
    WorkflowTriggerPayload Trigger,
    WorkflowStageGraphPayload StageGraph,
    IReadOnlyList<WorkflowDependencyPayload> Dependencies,
    IReadOnlyList<ProcessingWarningPayload> Warnings);

internal sealed record WorkflowTriggerPayload(
    string PrimaryEntity,
    bool OnCreate,
    bool OnUpdate,
    bool OnDelete,
    IReadOnlyList<string> AttributeFilters,
    string? TriggerDescription);

internal sealed record WorkflowStageGraphPayload(
    IReadOnlyList<WorkflowNodePayload> Nodes,
    IReadOnlyList<WorkflowEdgePayload> Edges);

internal sealed record WorkflowNodePayload(
    string Id,
    string ComponentType,
    string Label,
    IReadOnlyDictionary<string, string> Attributes);

internal sealed record WorkflowEdgePayload(
    string FromNodeId,
    string ToNodeId,
    string? ConditionLabel = null);

internal sealed record WorkflowDependencyPayload(
    string DependencyType,
    string Name,
    string? ReferenceId);

internal sealed record ProcessingWarningPayload(
    string Code,
    string Message,
    string? Source,
    bool IsBlocking = false,
    string Category = nameof(WarningCategory.General),
    string Severity = nameof(WarningSeverity.Warning));

internal static class LiveWorkflowTransportMapper
{
    public static WorkflowDocumentationRequest ToRequest(LiveWorkflowDocumentationRequest request)
    {
        return new WorkflowDocumentationRequest(
            SourceName: request.SourceName,
            Workflows: request.Workflows.Select(ToWorkflowDefinition).ToArray(),
            Warnings: request.Warnings?.Select(ToWarning).ToArray() ?? Array.Empty<ProcessingWarning>());
    }

    private static WorkflowDefinition ToWorkflowDefinition(WorkflowDefinitionPayload payload)
    {
        return new WorkflowDefinition(
            WorkflowId: payload.WorkflowId,
            LogicalName: payload.LogicalName,
            DisplayName: payload.DisplayName,
            Category: payload.Category,
            Scope: payload.Scope,
            Owner: payload.Owner,
            ExecutionMode: ParseExecutionMode(payload.ExecutionMode),
            Trigger: new WorkflowTrigger(
                payload.Trigger.PrimaryEntity,
                payload.Trigger.OnCreate,
                payload.Trigger.OnUpdate,
                payload.Trigger.OnDelete,
                payload.Trigger.AttributeFilters ?? Array.Empty<string>(),
                payload.Trigger.TriggerDescription),
            StageGraph: new WorkflowStageGraph(
                payload.StageGraph.Nodes.Select(node => new WorkflowNode(
                    node.Id,
                    ParseComponentType(node.ComponentType),
                    node.Label,
                    node.Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))).ToArray(),
                payload.StageGraph.Edges.Select(edge => new WorkflowEdge(
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.ConditionLabel)).ToArray()),
            RootCondition: null,
            Dependencies: payload.Dependencies.Select(dependency => new WorkflowDependency(
                dependency.DependencyType,
                dependency.Name,
                dependency.ReferenceId)).ToArray(),
            Warnings: payload.Warnings.Select(ToWarning).ToArray());
    }

    private static ProcessingWarning ToWarning(ProcessingWarningPayload payload)
    {
        return new ProcessingWarning(
            payload.Code,
            payload.Message,
            payload.Source,
            payload.IsBlocking,
            ParseEnum(payload.Category, WarningCategory.General),
            ParseEnum(payload.Severity, WarningSeverity.Warning));
    }

    private static ExecutionMode ParseExecutionMode(string raw)
    {
        return string.Equals(raw, nameof(ExecutionMode.Synchronous), StringComparison.OrdinalIgnoreCase)
            ? ExecutionMode.Synchronous
            : ExecutionMode.Asynchronous;
    }

    private static WorkflowComponentType ParseComponentType(string raw)
    {
        return ParseEnum(raw, WorkflowComponentType.Action);
    }

    private static TEnum ParseEnum<TEnum>(string raw, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(raw, true, out var value)
            ? value
            : fallback;
    }
}
