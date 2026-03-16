namespace BN.WorkflowDoc.Core.Domain;

public sealed record SolutionPackage(
    string SourcePath,
    string ExtractedPath,
    string Version,
    IReadOnlyList<WorkflowDefinition> Workflows,
    IReadOnlyList<ProcessingWarning> Warnings);

public sealed record ProcessingWarning(
    string Code,
    string Message,
    string? Source,
    bool IsBlocking = false,
    WarningCategory Category = WarningCategory.General,
    WarningSeverity Severity = WarningSeverity.Warning);

public enum WarningCategory
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

public enum WarningSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

