using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

/// <summary>
/// Builds a solution-level dependency graph from workflow definitions.
/// </summary>
public interface IDependencyGraphBuilder
{
    DependencyGraphModel Build(IReadOnlyList<WorkflowDefinition> workflows);
}

public sealed class DependencyGraphBuilder : IDependencyGraphBuilder
{
    public DependencyGraphModel Build(IReadOnlyList<WorkflowDefinition> workflows)
    {
        if (workflows.Count == 0)
        {
            return new DependencyGraphModel(Array.Empty<DependencyGraphNode>(), Array.Empty<DependencyGraphEdge>(), "No workflow dependencies were discovered.");
        }

        var workflowNodeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nodeLabels = new Dictionary<string, (string Label, string Type)>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<DependencyGraphEdge>();
        var dependencyNodeIdLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var crossWorkflowLinks = 0;

        foreach (var workflow in workflows)
        {
            var workflowNodeId = $"wf:{workflow.WorkflowId:N}";
            workflowNodeIds[workflow.DisplayName] = workflowNodeId;
            workflowNodeIds[workflow.LogicalName] = workflowNodeId;
            nodeLabels[workflowNodeId] = (workflow.DisplayName, "Workflow");
        }

        foreach (var workflow in workflows)
        {
            var sourceId = $"wf:{workflow.WorkflowId:N}";

            foreach (var dependency in workflow.Dependencies)
            {
                var targetWorkflowNodeId = ResolveWorkflowReference(dependency, workflowNodeIds);
                if (targetWorkflowNodeId is not null)
                {
                    edges.Add(new DependencyGraphEdge(sourceId, targetWorkflowNodeId, "WorkflowReference", dependency.DependencyType));
                    crossWorkflowLinks++;
                    continue;
                }

                var dependencyKey = $"{dependency.DependencyType}:{dependency.Name}";
                if (!dependencyNodeIdLookup.TryGetValue(dependencyKey, out var dependencyNodeId))
                {
                    dependencyNodeId = $"dep:{dependencyNodeIdLookup.Count + 1:D4}";
                    dependencyNodeIdLookup[dependencyKey] = dependencyNodeId;
                    nodeLabels[dependencyNodeId] = (dependency.Name, dependency.DependencyType);
                }

                edges.Add(new DependencyGraphEdge(sourceId, dependencyNodeId, dependency.DependencyType, dependency.ReferenceId));
            }
        }

        var incomingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var outgoingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            outgoingCounts[edge.SourceNodeId] = outgoingCounts.TryGetValue(edge.SourceNodeId, out var sourceCount) ? sourceCount + 1 : 1;
            incomingCounts[edge.TargetNodeId] = incomingCounts.TryGetValue(edge.TargetNodeId, out var targetCount) ? targetCount + 1 : 1;
        }

        var nodes = nodeLabels
            .Select(kvp => new DependencyGraphNode(
                NodeId: kvp.Key,
                DisplayName: kvp.Value.Label,
                NodeType: kvp.Value.Type,
                IncomingEdges: incomingCounts.TryGetValue(kvp.Key, out var inCount) ? inCount : 0,
                OutgoingEdges: outgoingCounts.TryGetValue(kvp.Key, out var outCount) ? outCount : 0))
            .OrderBy(n => n.NodeType)
            .ThenBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = $"{nodes.Length} node(s), {edges.Count} edge(s), {crossWorkflowLinks} cross-workflow link(s).";
        return new DependencyGraphModel(nodes, edges, summary);
    }

    private static string? ResolveWorkflowReference(
        WorkflowDependency dependency,
        IReadOnlyDictionary<string, string> workflowNodeIds)
    {
        if (!string.Equals(dependency.DependencyType, "ChildWorkflow", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(dependency.Name))
        {
            return null;
        }

        return workflowNodeIds.TryGetValue(dependency.Name, out var workflowNodeId)
            ? workflowNodeId
            : null;
    }
}
