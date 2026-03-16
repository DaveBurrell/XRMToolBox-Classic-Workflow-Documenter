using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

/// <summary>
/// Runs documentation generation for multiple solution inputs and produces an aggregated portfolio summary.
/// </summary>
public interface IPortfolioDocumentationPipeline
{
    Task<ParseResult<BatchDocumentationResult>> GenerateAsync(
        IReadOnlyList<string> solutionZipPaths,
        CancellationToken cancellationToken = default);
}

public sealed class PortfolioDocumentationPipeline : IPortfolioDocumentationPipeline
{
    private readonly IWorkflowDocumentationPipeline _workflowDocumentationPipeline;

    public PortfolioDocumentationPipeline(IWorkflowDocumentationPipeline workflowDocumentationPipeline)
    {
        _workflowDocumentationPipeline = workflowDocumentationPipeline;
    }

    public async Task<ParseResult<BatchDocumentationResult>> GenerateAsync(
        IReadOnlyList<string> solutionZipPaths,
        CancellationToken cancellationToken = default)
    {
        if (solutionZipPaths.Count == 0)
        {
            var warning = new ProcessingWarning(
                "BATCH_INPUT_EMPTY",
                "At least one solution ZIP path is required for batch processing.",
                null,
                true,
                WarningCategory.Input,
                WarningSeverity.Error);

            return new ParseResult<BatchDocumentationResult>(
                ProcessingStatus.Failed,
                null,
                [warning],
                warning.Message);
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var results = new List<BatchWorkflowResult>(solutionZipPaths.Count);

        foreach (var inputPath in solutionZipPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = DateTimeOffset.UtcNow;

            var result = await _workflowDocumentationPipeline
                .GenerateAsync(inputPath, cancellationToken)
                .ConfigureAwait(false);

            var duration = DateTimeOffset.UtcNow - started;
            results.Add(new BatchWorkflowResult(
                InputPath: inputPath,
                Status: result.Status,
                WorkflowDocuments: result.Value?.WorkflowDocuments,
                OverviewDocument: result.Value?.OverviewDocument,
                Warnings: result.Warnings,
                Duration: duration));
        }

        var completedAtUtc = DateTimeOffset.UtcNow;
        var aggregatedWarnings = results.SelectMany(x => x.Warnings).ToArray();
        var portfolioSummary = BuildPortfolioSummary(results, aggregatedWarnings, completedAtUtc);

        var batchResult = new BatchDocumentationResult(
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            Results: results,
            PortfolioSummary: portfolioSummary,
            Warnings: aggregatedWarnings);

        var status = DetermineStatus(results, aggregatedWarnings);
        return new ParseResult<BatchDocumentationResult>(status, batchResult, aggregatedWarnings);
    }

    private static PortfolioSummaryModel BuildPortfolioSummary(
        IReadOnlyList<BatchWorkflowResult> results,
        IReadOnlyList<ProcessingWarning> warnings,
        DateTimeOffset generatedAtUtc)
    {
        var solutionSummaries = results.Select(BuildSolutionSummary).ToArray();
        var topRiskWorkflows = BuildTopRiskWorkflowCards(results);
        var crossSolutionGraph = BuildCrossSolutionDependencyGraph(results);

        return new PortfolioSummaryModel(
            GeneratedAtUtc: generatedAtUtc,
            Solutions: solutionSummaries,
            TopRiskWorkflows: topRiskWorkflows,
            CrossSolutionDependencyGraph: crossSolutionGraph,
            GlobalWarnings: warnings);
    }

    private static PortfolioSolutionSummary BuildSolutionSummary(BatchWorkflowResult result)
    {
        var overview = result.OverviewDocument;
        var cards = overview?.Workflows ?? Array.Empty<OverviewWorkflowCard>();
        var avgQuality = cards.Count == 0
            ? 0
            : (int)Math.Round(cards
                .Where(x => x.QualityScore is not null)
                .Select(x => x.QualityScore!.OverallScore)
                .DefaultIfEmpty()
                .Average());

        var highRiskCount = cards.Count(x =>
            string.Equals(x.QualityScore?.RiskBand, "High", StringComparison.OrdinalIgnoreCase));

        var fallbackName = Path.GetFileNameWithoutExtension(result.InputPath);
        var solutionName = string.IsNullOrWhiteSpace(overview?.SolutionName)
            ? (string.IsNullOrWhiteSpace(fallbackName) ? "Unnamed Solution" : fallbackName)
            : overview!.SolutionName;

        return new PortfolioSolutionSummary(
            SolutionName: solutionName,
            WorkflowCount: cards.Count,
            AverageQualityScore: avgQuality,
            HighRiskWorkflowCount: highRiskCount,
            Warnings: result.Warnings);
    }

    private static IReadOnlyList<OverviewWorkflowCard> BuildTopRiskWorkflowCards(IReadOnlyList<BatchWorkflowResult> results)
    {
        return results
            .Where(x => x.OverviewDocument is not null)
            .SelectMany(x => x.OverviewDocument!.Workflows.Select(card =>
            {
                var solutionName = x.OverviewDocument!.SolutionName;
                var prefixedName = $"{solutionName} :: {card.WorkflowName}";
                return card with { WorkflowName = prefixedName };
            }))
            .OrderBy(x => x.QualityScore?.OverallScore ?? int.MaxValue)
            .ThenByDescending(x => x.ComplexityScore)
            .Take(15)
            .ToArray();
    }

    private static DependencyGraphModel? BuildCrossSolutionDependencyGraph(IReadOnlyList<BatchWorkflowResult> results)
    {
        var nodes = new List<DependencyGraphNode>();
        var edges = new List<DependencyGraphEdge>();

        foreach (var result in results)
        {
            if (result.OverviewDocument?.DependencyGraph is null)
            {
                continue;
            }

            var solution = result.OverviewDocument.SolutionName;
            var solutionPrefix = SanitizeToken(solution);
            var nodeIdMap = result.OverviewDocument.DependencyGraph.Nodes.ToDictionary(
                x => x.NodeId,
                x => $"{solutionPrefix}:{x.NodeId}",
                StringComparer.OrdinalIgnoreCase);

            foreach (var node in result.OverviewDocument.DependencyGraph.Nodes)
            {
                nodes.Add(new DependencyGraphNode(
                    NodeId: nodeIdMap[node.NodeId],
                    DisplayName: $"{solution} :: {node.DisplayName}",
                    NodeType: node.NodeType,
                    IncomingEdges: node.IncomingEdges,
                    OutgoingEdges: node.OutgoingEdges));
            }

            foreach (var edge in result.OverviewDocument.DependencyGraph.Edges)
            {
                if (!nodeIdMap.TryGetValue(edge.SourceNodeId, out var source))
                {
                    continue;
                }

                if (!nodeIdMap.TryGetValue(edge.TargetNodeId, out var target))
                {
                    continue;
                }

                edges.Add(new DependencyGraphEdge(source, target, edge.RelationshipType, edge.Notes));
            }
        }

        if (nodes.Count == 0)
        {
            return null;
        }

        return new DependencyGraphModel(
            Nodes: nodes,
            Edges: edges,
            Summary: $"Portfolio graph includes {nodes.Count} node(s) and {edges.Count} edge(s) across {results.Count} solution(s).");
    }

    private static ProcessingStatus DetermineStatus(
        IReadOnlyList<BatchWorkflowResult> results,
        IReadOnlyList<ProcessingWarning> warnings)
    {
        if (results.Count == 0)
        {
            return ProcessingStatus.Failed;
        }

        if (results.All(x => x.Status == ProcessingStatus.Failed))
        {
            return ProcessingStatus.Failed;
        }

        if (warnings.Count > 0 || results.Any(x => x.Status != ProcessingStatus.Success))
        {
            return ProcessingStatus.PartialSuccess;
        }

        return ProcessingStatus.Success;
    }

    private static string SanitizeToken(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "solution";
        }

        var chars = input
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray();

        var value = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(value) ? "solution" : value;
    }
}
