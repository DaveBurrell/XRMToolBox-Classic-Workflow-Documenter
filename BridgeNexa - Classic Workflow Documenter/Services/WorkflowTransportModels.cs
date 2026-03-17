using System;
using System.Collections.Generic;

namespace BridgeNexa___Classic_Workflow_Documenter.Services;

internal enum ProcessingStatus
{
    Success,
    PartialSuccess,
    Failed
}

internal enum WarningCategory
{
    General,
    Input,
    Parsing,
    Diagram,
    Rendering,
    Documentation,
    Performance,
    Validation
}

internal enum WarningSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

internal enum WorkflowComponentType
{
    Trigger,
    Condition,
    Action,
    Stop,
    ChildWorkflow,
    ExternalCall
}

internal sealed record ParseResult<T>(
    ProcessingStatus Status,
    T? Value,
    IReadOnlyList<ProcessingWarning> Warnings,
    string? ErrorMessage = null)
    where T : class;

internal sealed record ProcessingWarning(
    string Code,
    string Message,
    string? Source,
    bool IsBlocking = false,
    WarningCategory Category = WarningCategory.General,
    WarningSeverity Severity = WarningSeverity.Warning);

internal sealed record WorkflowCatalogItem(
    Guid WorkflowId,
    string DisplayName,
    string Category,
    string PrimaryEntity,
    string ExecutionMode,
    string Scope,
    string? Owner,
    string TriggerSummary,
    string State);

internal sealed record LiveWorkflowDocumentationRequest(
    string SourceName,
    IReadOnlyList<WorkflowDefinitionPayload> Workflows,
    IReadOnlyList<ProcessingWarning>? Warnings = null);

internal sealed record WorkflowDefinitionPayload(
    Guid WorkflowId,
    string LogicalName,
    string DisplayName,
    string Category,
    string Scope,
    string? Owner,
    bool IsOnDemand,
    string ExecutionMode,
    WorkflowTriggerPayload Trigger,
    WorkflowStageGraphPayload StageGraph,
    IReadOnlyList<WorkflowDependencyPayload> Dependencies,
    IReadOnlyList<ProcessingWarning> Warnings);

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
    WorkflowComponentType ComponentType,
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

internal sealed record CliDocxResult(
    string Status,
    string Input,
    string OutputFolder,
    IReadOnlyList<string>? WorkflowDocxFiles,
    string? OverviewDocxFile,
    string? CombinedFullDetailDocxFile,
    IReadOnlyList<CliWarning>? Warnings,
    string? Error,
    string? ManifestFile = null);

internal sealed record CliWarning(
    string Code,
    string Message,
    string? Source,
    bool Blocking,
    string Category,
    string Severity);
