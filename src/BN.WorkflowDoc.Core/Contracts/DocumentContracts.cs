using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Contracts;

/// <summary>
/// Represents a narrative section with source trace links for explainability.
/// </summary>
public sealed record TraceableNarrativeSection(
    string Title,
    string Narrative,
    IReadOnlyList<SourceTrace> SourceTraces);

/// <summary>
/// Points a narrative statement back to source metadata fields.
/// </summary>
public sealed record SourceTrace(
    string SourceField,
    string SourcePath,
    string? Notes);

/// <summary>
/// One extracted workflow step attribute included in detailed documentation output.
/// </summary>
public sealed record WorkflowStepAttribute(
    string Name,
    string Value);

/// <summary>
/// Detailed step inventory row used to ensure every workflow step is documented.
/// </summary>
public sealed record WorkflowStepDetail(
    int Sequence,
    string StepId,
    string StepType,
    string Label,
    bool IsSynthetic,
    IReadOnlyList<string> IncomingPaths,
    IReadOnlyList<string> OutgoingPaths,
    IReadOnlyList<WorkflowStepAttribute> Attributes,
    string Narrative);

/// <summary>
/// Directed transition between two workflow steps.
/// </summary>
public sealed record WorkflowTransitionDetail(
    int Sequence,
    string FromStepId,
    string FromStepLabel,
    string ToStepId,
    string ToStepLabel,
    string? ConditionLabel,
    string Narrative);

/// <summary>
/// Workflow-level documentation model used for artifact generation.
/// </summary>
public sealed record WorkflowDocumentModel(
    string WorkflowName,
    string ProcessCategory,
    bool IsOnDemand,
    string Purpose,
    WorkflowTrigger Trigger,
    ExecutionMode ExecutionMode,
    IReadOnlyList<TraceableNarrativeSection> Sections,
    IReadOnlyList<WorkflowStepDetail> Steps,
    IReadOnlyList<WorkflowTransitionDetail> Transitions,
    IReadOnlyList<DiagramGraph> Diagrams,
    IReadOnlyList<ProcessingWarning> Warnings);

/// <summary>
/// Summary card used in solution overview documents.
/// </summary>
public sealed record OverviewWorkflowCard(
    string WorkflowName,
    string ProcessCategory,
    bool IsOnDemand,
    string Purpose,
    string TriggerSummary,
    ExecutionMode ExecutionMode,
    int ComplexityScore,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> KeyRisks,
    WorkflowQualityScore? QualityScore = null,
    IReadOnlyList<string>? WarningCodes = null);

/// <summary>
/// Business-readable quality score with deterministic breakdown components.
/// </summary>
public sealed record WorkflowQualityScore(
    int OverallScore,
    string RiskBand,
    QualityScoreBreakdown Breakdown,
    string Summary);

/// <summary>
/// Component values used to compute a workflow quality score.
/// </summary>
public sealed record QualityScoreBreakdown(
    int TriggerSpecificity,
    int Complexity,
    int DependencyImpact,
    int WarningDensity);

/// <summary>
/// Node in a dependency graph model.
/// </summary>
public sealed record DependencyGraphNode(
    string NodeId,
    string DisplayName,
    string NodeType,
    int IncomingEdges,
    int OutgoingEdges);

/// <summary>
/// Edge in a dependency graph model.
/// </summary>
public sealed record DependencyGraphEdge(
    string SourceNodeId,
    string TargetNodeId,
    string RelationshipType,
    string? Notes = null);

/// <summary>
/// Dependency graph model for solution-level or portfolio-level visualization.
/// </summary>
public sealed record DependencyGraphModel(
    IReadOnlyList<DependencyGraphNode> Nodes,
    IReadOnlyList<DependencyGraphEdge> Edges,
    string Summary);

/// <summary>
/// Aggregated summary for one solution in batch/portfolio mode.
/// </summary>
public sealed record PortfolioSolutionSummary(
    string SolutionName,
    int WorkflowCount,
    int AverageQualityScore,
    int HighRiskWorkflowCount,
    IReadOnlyList<ProcessingWarning> Warnings);

/// <summary>
/// Portfolio-level summary generated from multiple solution processing runs.
/// </summary>
public sealed record PortfolioSummaryModel(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<PortfolioSolutionSummary> Solutions,
    IReadOnlyList<OverviewWorkflowCard> TopRiskWorkflows,
    DependencyGraphModel? CrossSolutionDependencyGraph,
    IReadOnlyList<ProcessingWarning> GlobalWarnings);

/// <summary>
/// Per-input result for batch processing operations.
/// </summary>
public sealed record BatchWorkflowResult(
    string InputPath,
    ProcessingStatus Status,
    IReadOnlyList<WorkflowDocumentModel>? WorkflowDocuments,
    OverviewDocumentModel? OverviewDocument,
    IReadOnlyList<ProcessingWarning> Warnings,
    TimeSpan Duration);

/// <summary>
/// Batch execution output and associated portfolio summary.
/// </summary>
public sealed record BatchDocumentationResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<BatchWorkflowResult> Results,
    PortfolioSummaryModel PortfolioSummary,
    IReadOnlyList<ProcessingWarning> Warnings);

/// <summary>
/// Overview document model for one solution.
/// </summary>
public sealed record OverviewDocumentModel(
    string SolutionName,
    IReadOnlyList<OverviewWorkflowCard> Workflows,
    IReadOnlyList<ProcessingWarning> GlobalWarnings,
    DependencyGraphModel? DependencyGraph = null);

/// <summary>
/// Builds workflow-level document models from parsed workflow definitions.
/// </summary>
public interface IWorkflowDocumentBuilder
{
    /// <summary>
    /// Builds a workflow document model for a single workflow definition.
    /// </summary>
    Task<ParseResult<WorkflowDocumentModel>> BuildAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds solution-level overview models from parsed workflows.
/// </summary>
public interface IOverviewDocumentBuilder
{
    /// <summary>
    /// Builds an overview model for a solution and its workflows.
    /// </summary>
    Task<ParseResult<OverviewDocumentModel>> BuildAsync(
        string solutionName,
        IReadOnlyList<WorkflowDefinition> workflows,
        CancellationToken cancellationToken = default);
}

