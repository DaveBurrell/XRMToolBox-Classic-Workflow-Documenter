using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BN.WorkflowDoc.Core.Application;

/// <summary>
/// End-to-end single-solution documentation generation result.
/// </summary>
public sealed record DocumentationGenerationResult(
    SolutionPackage Package,
    IReadOnlyList<WorkflowDocumentModel> WorkflowDocuments,
    OverviewDocumentModel OverviewDocument,
    IReadOnlyList<ProcessingWarning> Warnings,
    PipelinePerformanceMetrics? Performance = null);

/// <summary>
/// Captures lightweight timing and memory telemetry for one pipeline run.
/// </summary>
public sealed record PipelinePerformanceMetrics(
    TimeSpan Duration,
    long StartWorkingSetBytes,
    long EndWorkingSetBytes,
    int WorkflowCount,
    int ParallelWorkersUsed);

/// <summary>
/// Orchestrates extraction, model building, and overview generation for one solution ZIP.
/// </summary>
public interface IWorkflowDocumentationPipeline
{
    /// <summary>
    /// Generates workflow-level and overview documentation models for one solution package.
    /// </summary>
    Task<ParseResult<DocumentationGenerationResult>> GenerateAsync(
        string solutionZipPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates workflow-level and overview documentation models for an in-memory workflow selection.
    /// </summary>
    Task<ParseResult<DocumentationGenerationResult>> GenerateAsync(
        WorkflowDocumentationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowDocumentationPipeline : IWorkflowDocumentationPipeline
{
    private readonly IWorkflowExtractionPipeline _extractionPipeline;
    private readonly IWorkflowDocumentBuilder _workflowDocumentBuilder;
    private readonly IOverviewDocumentBuilder _overviewDocumentBuilder;

    public WorkflowDocumentationPipeline(
        IWorkflowExtractionPipeline extractionPipeline,
        IWorkflowDocumentBuilder workflowDocumentBuilder,
        IOverviewDocumentBuilder overviewDocumentBuilder)
    {
        _extractionPipeline = extractionPipeline;
        _workflowDocumentBuilder = workflowDocumentBuilder;
        _overviewDocumentBuilder = overviewDocumentBuilder;
    }

    public async Task<ParseResult<DocumentationGenerationResult>> GenerateAsync(
        string solutionZipPath,
        CancellationToken cancellationToken = default)
    {
        var startWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var stopwatch = Stopwatch.StartNew();

        var extractionResult = await _extractionPipeline.ExtractAsync(solutionZipPath, cancellationToken).ConfigureAwait(false);
        if (extractionResult.Value is null)
        {
            return new ParseResult<DocumentationGenerationResult>(
                extractionResult.Status,
                null,
                extractionResult.Warnings,
                extractionResult.ErrorMessage ?? "Failed to extract workflows.");
        }

        return await GenerateFromWorkflowsAsync(
            sourceName: Path.GetFileNameWithoutExtension(solutionZipPath),
            sourcePath: solutionZipPath,
            version: extractionResult.Value.Version,
            workflows: extractionResult.Value.Workflows,
            initialWarnings: extractionResult.Warnings,
            initialStatus: extractionResult.Status,
            startWorkingSet: startWorkingSet,
            stopwatch: stopwatch,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task<ParseResult<DocumentationGenerationResult>> GenerateAsync(
        WorkflowDocumentationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var startWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var stopwatch = Stopwatch.StartNew();

        return GenerateFromWorkflowsAsync(
            sourceName: string.IsNullOrWhiteSpace(request.SourceName) ? "Workflow Selection" : request.SourceName,
            sourcePath: request.SourceName,
            version: "live",
            workflows: request.Workflows ?? Array.Empty<WorkflowDefinition>(),
            initialWarnings: request.Warnings ?? Array.Empty<ProcessingWarning>(),
            initialStatus: ProcessingStatus.Success,
            startWorkingSet: startWorkingSet,
            stopwatch: stopwatch,
            cancellationToken: cancellationToken);
    }

    private async Task<ParseResult<DocumentationGenerationResult>> GenerateFromWorkflowsAsync(
        string sourceName,
        string sourcePath,
        string version,
        IReadOnlyList<WorkflowDefinition> workflows,
        IReadOnlyList<ProcessingWarning> initialWarnings,
        ProcessingStatus initialStatus,
        long startWorkingSet,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (workflows.Count == 0)
        {
            stopwatch.Stop();

            var selectionWarnings = new List<ProcessingWarning>(initialWarnings)
            {
                new(
                    "WORKFLOW_SELECTION_EMPTY",
                    "No workflows were provided for documentation generation.",
                    sourceName,
                    true,
                    WarningCategory.Input,
                    WarningSeverity.Error)
            };

            return new ParseResult<DocumentationGenerationResult>(
                ProcessingStatus.Failed,
                null,
                selectionWarnings,
                "No workflows were provided for documentation generation.");
        }

        var warnings = new List<ProcessingWarning>(initialWarnings);
        var workflowCount = workflows.Count;

        var workflowDocuments = new WorkflowDocumentModel?[workflowCount];
        var warningQueue = new ConcurrentQueue<ProcessingWarning>();
        var failureQueue = new ConcurrentQueue<string>();

        var workerCount = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
        workerCount = Math.Min(workerCount, Math.Max(1, workflowCount));

        using var semaphore = new SemaphoreSlim(workerCount, workerCount);
        var buildTasks = workflows.Select((workflow, index) =>
            BuildWorkflowDocumentAsync(
                workflow,
                index,
                semaphore,
                workflowDocuments,
                warningQueue,
                failureQueue,
                cancellationToken));

        await Task.WhenAll(buildTasks).ConfigureAwait(false);

        while (warningQueue.TryDequeue(out var warning))
        {
            warnings.Add(warning);
        }

        if (failureQueue.TryDequeue(out var firstFailure))
        {
            return new ParseResult<DocumentationGenerationResult>(
                ProcessingStatus.Failed,
                null,
                warnings,
                firstFailure);
        }

        var orderedWorkflowDocuments = workflowDocuments
            .Where(x => x is not null)
            .Select(x => x!)
            .ToArray();

        if (orderedWorkflowDocuments.Length != workflowCount)
        {
            return new ParseResult<DocumentationGenerationResult>(
                ProcessingStatus.Failed,
                null,
                warnings,
                "One or more workflow documents were not produced during parallel generation.");
        }

        var overviewResult = await _overviewDocumentBuilder
            .BuildAsync(sourceName, workflows, cancellationToken)
            .ConfigureAwait(false);
        warnings.AddRange(overviewResult.Warnings);

        if (overviewResult.Value is null)
        {
            return new ParseResult<DocumentationGenerationResult>(
                ProcessingStatus.Failed,
                null,
                warnings,
                overviewResult.ErrorMessage ?? "Failed to build overview document.");
        }

        var status = CalculateStatus(initialStatus, warnings);
        stopwatch.Stop();

        var performance = new PipelinePerformanceMetrics(
            Duration: stopwatch.Elapsed,
            StartWorkingSetBytes: startWorkingSet,
            EndWorkingSetBytes: Process.GetCurrentProcess().WorkingSet64,
            WorkflowCount: workflowCount,
            ParallelWorkersUsed: workerCount);

        var value = new DocumentationGenerationResult(
            Package: new SolutionPackage(sourcePath, string.Empty, version, workflows, warnings),
            WorkflowDocuments: orderedWorkflowDocuments,
            OverviewDocument: overviewResult.Value,
            Warnings: warnings,
            Performance: performance);

        return new ParseResult<DocumentationGenerationResult>(status, value, warnings);
    }

    private async Task BuildWorkflowDocumentAsync(
        WorkflowDefinition workflow,
        int index,
        SemaphoreSlim semaphore,
        WorkflowDocumentModel?[] workflowDocuments,
        ConcurrentQueue<ProcessingWarning> warningQueue,
        ConcurrentQueue<string> failureQueue,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var workflowDocResult = await _workflowDocumentBuilder.BuildAsync(workflow, cancellationToken).ConfigureAwait(false);
            foreach (var warning in workflowDocResult.Warnings)
            {
                warningQueue.Enqueue(warning);
            }

            if (workflowDocResult.Value is null)
            {
                failureQueue.Enqueue(workflowDocResult.ErrorMessage ?? $"Failed to build workflow document for '{workflow.DisplayName}'.");
                return;
            }

            workflowDocuments[index] = workflowDocResult.Value;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static ProcessingStatus CalculateStatus(ProcessingStatus extractionStatus, IReadOnlyList<ProcessingWarning> warnings)
    {
        if (extractionStatus == ProcessingStatus.Failed)
        {
            return ProcessingStatus.Failed;
        }

        return warnings.Count == 0 ? extractionStatus : ProcessingStatus.PartialSuccess;
    }
}

