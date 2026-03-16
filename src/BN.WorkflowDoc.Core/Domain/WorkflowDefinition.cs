namespace BN.WorkflowDoc.Core.Domain;

public sealed record WorkflowDefinition(
    Guid WorkflowId,
    string LogicalName,
    string DisplayName,
    string Category,
    string Scope,
    string? Owner,
    ExecutionMode ExecutionMode,
    WorkflowTrigger Trigger,
    WorkflowStageGraph StageGraph,
    ConditionNode? RootCondition,
    IReadOnlyList<WorkflowDependency> Dependencies,
    IReadOnlyList<ProcessingWarning> Warnings);

public sealed record WorkflowDependency(
    string DependencyType,
    string Name,
    string? ReferenceId);

