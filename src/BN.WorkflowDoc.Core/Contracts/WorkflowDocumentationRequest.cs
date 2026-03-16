using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Contracts;

/// <summary>
/// Represents a documentation run against an already-loaded workflow selection.
/// </summary>
public sealed record WorkflowDocumentationRequest(
    string SourceName,
    IReadOnlyList<WorkflowDefinition> Workflows,
    IReadOnlyList<ProcessingWarning>? Warnings = null);