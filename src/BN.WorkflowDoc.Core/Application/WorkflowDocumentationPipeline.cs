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

        var warnings = new List<ProcessingWarning>(extractionResult.Warnings);
        var workflowCount = extractionResult.Value.Workflows.Count;

        var workflowDocuments = new WorkflowDocumentModel?[workflowCount];
        var warningQueue = new ConcurrentQueue<ProcessingWarning>();
        var failureQueue = new ConcurrentQueue<string>();

        var workerCount = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
        workerCount = Math.Min(workerCount, Math.Max(1, workflowCount));

        using var semaphore = new SemaphoreSlim(workerCount, workerCount);
        var buildTasks = extractionResult.Value.Workflows.Select((workflow, index) =>
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
            .BuildAsync(Path.GetFileNameWithoutExtension(solutionZipPath), extractionResult.Value.Workflows, cancellationToken)
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

        var status = CalculateStatus(extractionResult.Status, warnings);
        stopwatch.Stop();

        var performance = new PipelinePerformanceMetrics(
            Duration: stopwatch.Elapsed,
            StartWorkingSetBytes: startWorkingSet,
            EndWorkingSetBytes: Process.GetCurrentProcess().WorkingSet64,
            WorkflowCount: workflowCount,
            ParallelWorkersUsed: workerCount);

        var value = new DocumentationGenerationResult(
            Package: extractionResult.Value,
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

