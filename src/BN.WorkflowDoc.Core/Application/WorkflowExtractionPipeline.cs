using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

public interface IWorkflowExtractionPipeline
{
    Task<ParseResult<SolutionPackage>> ExtractAsync(string solutionZipPath, CancellationToken cancellationToken = default);
}

public sealed class WorkflowExtractionPipeline : IWorkflowExtractionPipeline
{
    private readonly ISolutionPackageReader _solutionPackageReader;
    private readonly IWorkflowDefinitionParser _workflowDefinitionParser;

    public WorkflowExtractionPipeline(ISolutionPackageReader solutionPackageReader, IWorkflowDefinitionParser workflowDefinitionParser)
    {
        _solutionPackageReader = solutionPackageReader;
        _workflowDefinitionParser = workflowDefinitionParser;
    }

    public async Task<ParseResult<SolutionPackage>> ExtractAsync(string solutionZipPath, CancellationToken cancellationToken = default)
    {
        var readResult = await _solutionPackageReader.ReadAsync(solutionZipPath, cancellationToken).ConfigureAwait(false);
        if (readResult.Value is null)
        {
            return new ParseResult<SolutionPackage>(
                readResult.Status,
                null,
                readResult.Warnings,
                readResult.ErrorMessage ?? "Failed to read solution package.");
        }

        var extractedPath = readResult.Value.ExtractedPath;
        try
        {
            var parseResult = await _workflowDefinitionParser.ParseAsync(readResult.Value, cancellationToken).ConfigureAwait(false);

            var warnings = readResult.Warnings.Concat(parseResult.Warnings).ToArray();

            if (parseResult.Value is null)
            {
                var failedStatus = readResult.Status == ProcessingStatus.Failed || parseResult.Status == ProcessingStatus.Failed
                    ? ProcessingStatus.Failed
                    : ProcessingStatus.PartialSuccess;

                return new ParseResult<SolutionPackage>(
                    failedStatus,
                    readResult.Value with { Warnings = warnings },
                    warnings,
                    parseResult.ErrorMessage ?? "Failed to parse workflow definitions.");
            }

            var status = GetCombinedStatus(readResult.Status, parseResult.Status);
            var package = readResult.Value with
            {
                Workflows = parseResult.Value,
                Warnings = warnings
            };

            return new ParseResult<SolutionPackage>(status, package, warnings);
        }
        finally
        {
            TryDeleteDirectory(extractedPath);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch { /* best-effort; OS will reclaim on reboot */ }
    }

    private static ProcessingStatus GetCombinedStatus(ProcessingStatus readStatus, ProcessingStatus parseStatus)
    {
        if (readStatus == ProcessingStatus.Failed || parseStatus == ProcessingStatus.Failed)
        {
            return ProcessingStatus.Failed;
        }

        if (readStatus == ProcessingStatus.PartialSuccess || parseStatus == ProcessingStatus.PartialSuccess)
        {
            return ProcessingStatus.PartialSuccess;
        }

        return ProcessingStatus.Success;
    }
}

