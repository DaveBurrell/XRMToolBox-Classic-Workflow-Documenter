using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class WorkflowAnalysisEngineTests
{
    [Fact]
    public void Analyze_LowComplexityWorkflow_ReturnsLowRiskScore()
    {
        var engine = new WorkflowAnalysisEngine();
        var workflow = CreateWorkflow(
            mode: ExecutionMode.Asynchronous,
            nodeCount: 4,
            edgeCount: 3,
            dependencies: Array.Empty<WorkflowDependency>());

        var score = engine.Analyze(workflow, Array.Empty<ProcessingWarning>());

        Assert.True(score.OverallScore >= 80);
        Assert.Equal("Low", score.RiskBand);
        Assert.Equal(0, score.Breakdown.WarningDensity);
    }

    [Fact]
    public void Analyze_HighComplexityWithWarnings_ReturnsHighRiskScore()
    {
        var engine = new WorkflowAnalysisEngine();
        var workflow = CreateWorkflow(
            mode: ExecutionMode.Synchronous,
            nodeCount: 22,
            edgeCount: 20,
            dependencies:
            [
                new WorkflowDependency("ExternalCall", "ERP API", "ext-01"),
                new WorkflowDependency("ChildWorkflow", "Escalation", "child-01")
            ]);

        var warnings = new[]
        {
            new ProcessingWarning("W1", "warning", "source", false, WarningCategory.Validation, WarningSeverity.Warning),
            new ProcessingWarning("W2", "error", "source", true, WarningCategory.Parsing, WarningSeverity.Error),
            new ProcessingWarning("W3", "critical", "source", true, WarningCategory.Parsing, WarningSeverity.Critical)
        };

        var score = engine.Analyze(workflow, warnings);

        Assert.True(score.OverallScore < 55);
        Assert.Equal("High", score.RiskBand);
        Assert.True(score.Breakdown.Complexity >= 20);
        Assert.True(score.Breakdown.DependencyImpact >= 10);
        Assert.True(score.Breakdown.WarningDensity >= 8);
    }

    private static WorkflowDefinition CreateWorkflow(
        ExecutionMode mode,
        int nodeCount,
        int edgeCount,
        IReadOnlyList<WorkflowDependency> dependencies)
    {
        var nodes = Enumerable.Range(1, nodeCount)
            .Select(i => new WorkflowNode(
                $"n{i}",
                i % 5 == 0 ? WorkflowComponentType.Condition : WorkflowComponentType.Action,
                $"Node {i}",
                new Dictionary<string, string>()))
            .ToArray();

        var edges = Enumerable.Range(1, edgeCount)
            .Select(i => new WorkflowEdge($"n{Math.Max(1, i)}", $"n{Math.Min(nodeCount, i + 1)}"))
            .ToArray();

        return new WorkflowDefinition(
            WorkflowId: Guid.NewGuid(),
            LogicalName: "sample",
            DisplayName: "Sample Workflow",
            Category: "classic",
            Scope: "organization",
            Owner: null,
            ExecutionMode: mode,
            Trigger: new WorkflowTrigger("account", true, true, false, ["name"], "create and update"),
            StageGraph: new WorkflowStageGraph(nodes, edges),
            RootCondition: null,
            Dependencies: dependencies,
            Warnings: Array.Empty<ProcessingWarning>());
    }
}
