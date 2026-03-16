using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Contracts;

public sealed record ParseResult<T>(
    ProcessingStatus Status,
    T? Value,
    IReadOnlyList<ProcessingWarning> Warnings,
    string? ErrorMessage = null)
    where T : class;

public interface ISolutionPackageReader
{
    Task<ParseResult<SolutionPackage>> ReadAsync(string solutionZipPath, CancellationToken cancellationToken = default);
}

public interface IWorkflowDefinitionParser
{
    Task<ParseResult<IReadOnlyList<WorkflowDefinition>>> ParseAsync(
        SolutionPackage package,
        CancellationToken cancellationToken = default);
}

