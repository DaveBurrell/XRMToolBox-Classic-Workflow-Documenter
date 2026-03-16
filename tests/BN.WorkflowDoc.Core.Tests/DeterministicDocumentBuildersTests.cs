using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class DeterministicDocumentBuildersTests
{
    [Fact]
    public async Task WorkflowDocumentBuilder_BuildsExpectedSectionsAndTraces()
    {
        var workflow = CreateWorkflow(
            name: "Account Follow Up",
            mode: ExecutionMode.Asynchronous,
            includeExternalDependency: true,
            includeConditionTree: true);

        var builder = new DeterministicWorkflowDocumentBuilder();
        var result = await builder.BuildAsync(workflow);

        Assert.Equal(ProcessingStatus.Success, result.Status);
        var model = Assert.IsType<WorkflowDocumentModel>(result.Value);
        Assert.Equal("Account Follow Up", model.WorkflowName);
        Assert.Equal(4, model.Sections.Count);
        Assert.Equal(2, model.Diagrams.Count);
        Assert.Contains(model.Diagrams, d => d.Type == DiagramType.Flowchart);
        Assert.Contains(model.Diagrams, d => d.Type == DiagramType.Swimlane);
        Assert.Contains(model.Sections, s => s.Title == "Purpose");
        Assert.Contains(model.Sections, s => s.Title == "Trigger Behavior");
        Assert.Contains(model.Sections, s => s.Title == "Process Logic");
        Assert.Contains(model.Sections, s => s.Title == "Dependencies");
        Assert.NotEmpty(model.Steps);
        Assert.NotEmpty(model.Transitions);

        var logicSection = Assert.Single(model.Sections, s => s.Title == "Process Logic");
        Assert.Contains("Condition root:", logicSection.Narrative);
        Assert.NotEmpty(logicSection.SourceTraces);
        Assert.Contains(model.Steps, x => x.Narrative.Contains("Business intent:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OverviewDocumentBuilder_ComputesComplexityAndRisks()
    {
        var workflow = CreateWorkflow(
            name: "Sync Heavy Workflow",
            mode: ExecutionMode.Synchronous,
            includeExternalDependency: true,
            includeConditionTree: false,
            stageNodeCount: 13,
            stageEdgeCount: 12);

        var builder = new DeterministicOverviewDocumentBuilder();
        var result = await builder.BuildAsync("Sample Solution", new[] { workflow });

        Assert.Equal(ProcessingStatus.Success, result.Status);
        var model = Assert.IsType<OverviewDocumentModel>(result.Value);
        var card = Assert.Single(model.Workflows);
        var graph = Assert.IsType<DependencyGraphModel>(model.DependencyGraph);

        Assert.True(card.ComplexityScore > 20);
        var quality = Assert.IsType<WorkflowQualityScore>(card.QualityScore);
        Assert.True(quality.OverallScore is >= 0 and <= 100);
        Assert.Equal("High", quality.RiskBand);
        Assert.Contains(card.KeyRisks, x => x.Contains("transaction latency", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(card.KeyRisks, x => x.Contains("External call dependency", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(card.KeyRisks, x => x.Contains("No extracted condition tree", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(card.WarningCodes ?? Array.Empty<string>());
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Single(graph.Edges);
    }

    private static WorkflowDefinition CreateWorkflow(
        string name,
        ExecutionMode mode,
        bool includeExternalDependency,
        bool includeConditionTree,
        int stageNodeCount = 6,
        int stageEdgeCount = 5)
    {
        var nodes = Enumerable.Range(1, stageNodeCount)
            .Select(i => new WorkflowNode($"n{i}", WorkflowComponentType.Action, $"Node {i}", new Dictionary<string, string>()))
            .ToArray();
        var edges = Enumerable.Range(1, stageEdgeCount)
            .Select(i => new WorkflowEdge(i == 1 ? "trigger" : $"n{i - 1}", $"n{Math.Min(i, stageNodeCount)}"))
            .ToArray();

        var dependencies = includeExternalDependency
            ? new[] { new WorkflowDependency("ExternalCall", "Notify ERP", "ext-1") }
            : Array.Empty<WorkflowDependency>();

        var rootCondition = includeConditionTree
            ? new ConditionNode(
                ConditionOperator.And,
                null,
                null,
                new[]
                {
                    ConditionNode.Leaf(ConditionOperator.Equals, "statuscode", "1"),
                    ConditionNode.Leaf(ConditionOperator.GreaterThan, "creditlimit", "10000")
                })
            : null;

        return new WorkflowDefinition(
            WorkflowId: Guid.NewGuid(),
            LogicalName: name.Replace(" ", string.Empty, StringComparison.Ordinal),
            DisplayName: name,
            Category: "classic",
            Scope: "organization",
            Owner: null,
            IsOnDemand: false,
            ExecutionMode: mode,
            Trigger: new WorkflowTrigger("account", false, true, false, Array.Empty<string>(), null),
            StageGraph: new WorkflowStageGraph(nodes, edges),
            RootCondition: rootCondition,
            Dependencies: dependencies,
            Warnings: Array.Empty<ProcessingWarning>());
    }
}

