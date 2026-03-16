using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

public sealed class DeterministicDiagramGraphMapper : IDiagramGraphMapper
{
    public ParseResult<IReadOnlyList<DiagramGraph>> Map(WorkflowDefinition workflow)
    {
        if (workflow.StageGraph.Nodes.Count == 0)
        {
            var placeholderFlowchart = new[]
            {
                new DiagramNode(
                    "placeholder",
                    "No executable workflow steps were defined in metadata/XAML.",
                    "Flow",
                    420,
                    120,
                    WorkflowComponentType.Action,
                    ["No extracted steps", "Review workflow metadata and XAML source"]) 
            };

            var placeholderSwimlane = new[]
            {
                new DiagramNode(
                    "placeholder",
                    "No executable workflow steps were defined in metadata/XAML.",
                    "Decision Logic",
                    420,
                    120,
                    WorkflowComponentType.Condition,
                    ["No extracted steps", "Review workflow metadata and XAML source"]) 
            };

            var placeholders = new[]
            {
                new DiagramGraph(
                    DiagramType.Flowchart,
                    placeholderFlowchart,
                    Array.Empty<DiagramEdge>(),
                    "Flowchart placeholder: workflow definition contains no executable steps."),
                new DiagramGraph(
                    DiagramType.Swimlane,
                    placeholderSwimlane,
                    Array.Empty<DiagramEdge>(),
                    "Swimlane placeholder: workflow definition contains no executable steps.")
            };

            return new ParseResult<IReadOnlyList<DiagramGraph>>(ProcessingStatus.Success, placeholders, Array.Empty<ProcessingWarning>());
        }

        var flowchartNodes = workflow.StageGraph.Nodes
            .Select(n => new DiagramNode(
                Id: n.Id,
                Label: n.Label,
                Lane: "Flow",
                Width: 250,
                Height: CalculateNodeHeight(n),
                ComponentType: n.ComponentType,
                DetailLines: BuildDetailLines(n)))
            .ToArray();

        var flowchartEdges = workflow.StageGraph.Edges
            .Select(e => new DiagramEdge(e.FromNodeId, e.ToNodeId, e.ConditionLabel))
            .ToArray();

        var swimlaneNodes = workflow.StageGraph.Nodes
            .Select(n => new DiagramNode(
                Id: n.Id,
                Label: n.Label,
                Lane: MapLane(n.ComponentType),
                Width: 260,
                Height: CalculateNodeHeight(n),
                ComponentType: n.ComponentType,
                DetailLines: BuildDetailLines(n)))
            .ToArray();

        var swimlaneEdges = workflow.StageGraph.Edges
            .Select(e => new DiagramEdge(e.FromNodeId, e.ToNodeId, e.ConditionLabel))
            .ToArray();

        var diagrams = new[]
        {
            new DiagramGraph(
                DiagramType.Flowchart,
                flowchartNodes,
                flowchartEdges,
                "Flowchart of trigger-to-step progression with decision branches."),
            new DiagramGraph(
                DiagramType.Swimlane,
                swimlaneNodes,
                swimlaneEdges,
                "Swimlane view separating trigger, logic, actions, and integrations.")
        };

            return new ParseResult<IReadOnlyList<DiagramGraph>>(ProcessingStatus.Success, diagrams, Array.Empty<ProcessingWarning>());
    }

    private static int CalculateNodeHeight(WorkflowNode node)
    {
        var detailLines = BuildDetailLines(node);
        var baseHeight = detailLines.Count switch
        {
            0 => 86,
            1 => 104,
            _ => 122
        };

        if (node.ComponentType == WorkflowComponentType.Condition)
        {
            return baseHeight + 18;
        }

        if (node.ComponentType == WorkflowComponentType.Stop)
        {
            return baseHeight + 8;
        }

        return baseHeight;
    }

    private static IReadOnlyList<string> BuildDetailLines(WorkflowNode node)
    {
        var details = new List<string>
        {
            DescribeComponent(node.ComponentType)
        };

        foreach (var line in BuildBusinessAttributeLines(node))
        {
            if (details.Contains(line, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            details.Add(line);
            if (details.Count == 3)
            {
                break;
            }
        }

        return details;
    }

    private static string DescribeComponent(WorkflowComponentType type)
    {
        return type switch
        {
            WorkflowComponentType.Trigger => "Starts when the workflow trigger fires",
            WorkflowComponentType.Condition => "Evaluates a business rule or branch",
            WorkflowComponentType.Action => "Performs a Dataverse workflow action",
            WorkflowComponentType.ChildWorkflow => "Invokes a child workflow process",
            WorkflowComponentType.ExternalCall => "Calls an external system or integration",
            WorkflowComponentType.Stop => "Ends processing for this branch",
            _ => "Processes workflow logic"
        };
    }

    private static string NormalizeAttributeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Attribute";
        }

        return value
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static IReadOnlyList<string> BuildBusinessAttributeLines(WorkflowNode node)
    {
        return node.ComponentType switch
        {
            WorkflowComponentType.Trigger => BuildTriggerDetails(node),
            WorkflowComponentType.Condition => BuildConditionDetails(node),
            WorkflowComponentType.Action => BuildActionDetails(node),
            WorkflowComponentType.ChildWorkflow => BuildChildWorkflowDetails(node),
            WorkflowComponentType.ExternalCall => BuildExternalCallDetails(node),
            WorkflowComponentType.Stop => BuildStopDetails(node),
            _ => BuildGenericDetails(node)
        };
    }

    private static IReadOnlyList<string> BuildTriggerDetails(WorkflowNode node)
    {
        var lines = new List<string>();
        AddLineIfFound(lines, node.Attributes, "Entity", "entity", "primaryentity", "entityname", "targetentity");
        AddLineIfFound(lines, node.Attributes, "Event", "event", "message", "operation", "triggertype");
        AddLineIfFound(lines, node.Attributes, "Scope", "scope", "mode", "executionmode");
        return lines;
    }

    private static IReadOnlyList<string> BuildConditionDetails(WorkflowNode node)
    {
        var lines = new List<string>();
        AddLineIfFound(lines, node.Attributes, "Rule", "condition", "expression", "operator", "comparison");
        AddLineIfFound(lines, node.Attributes, "Field", "attribute", "field", "attributename", "left");
        AddLineIfFound(lines, node.Attributes, "Value", "value", "right", "operand", "compareto");
        return lines;
    }

    private static IReadOnlyList<string> BuildActionDetails(WorkflowNode node)
    {
        var lines = new List<string>();
        AddLineIfFound(lines, node.Attributes, "Action", "type", "actiontype", "operation", "name");
        AddLineIfFound(lines, node.Attributes, "Entity", "entity", "entityname", "targetentity");
        AddLineIfFound(lines, node.Attributes, "Field", "attribute", "field", "attributename", "targetattribute");
        return lines;
    }

    private static IReadOnlyList<string> BuildChildWorkflowDetails(WorkflowNode node)
    {
        var lines = new List<string>();
        AddLineIfFound(lines, node.Attributes, "Child", "childworkflow", "workflowname", "name");
        AddLineIfFound(lines, node.Attributes, "Workflow Id", "workflowid", "processid", "id");
        AddLineIfFound(lines, node.Attributes, "Mode", "mode", "executionmode", "scope");
        return lines;
    }

    private static IReadOnlyList<string> BuildExternalCallDetails(WorkflowNode node)
    {
        var lines = new List<string>();
        AddLineIfFound(lines, node.Attributes, "Endpoint", "endpoint", "url", "service", "serviceurl", "address");
        AddLineIfFound(lines, node.Attributes, "Operation", "operation", "method", "message", "name");
        AddLineIfFound(lines, node.Attributes, "Target", "entity", "targetentity", "system");
        return lines;
    }

    private static IReadOnlyList<string> BuildStopDetails(WorkflowNode node)
    {
        var lines = new List<string>();
        AddLineIfFound(lines, node.Attributes, "Reason", "reason", "message", "description", "status");
        AddLineIfFound(lines, node.Attributes, "Status", "state", "status", "result");
        return lines;
    }

    private static IReadOnlyList<string> BuildGenericDetails(WorkflowNode node)
    {
        var lines = new List<string>();
        foreach (var entry in node.Attributes)
        {
            if (lines.Count == 3)
            {
                break;
            }

            if (IsNoiseAttribute(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
            {
                continue;
            }

            lines.Add($"{NormalizeAttributeName(entry.Key)}: {Collapse(entry.Value, 44)}");
        }

        return lines;
    }

    private static void AddLineIfFound(List<string> lines, IReadOnlyDictionary<string, string> attributes, string title, params string[] keys)
    {
        if (lines.Count == 3)
        {
            return;
        }

        if (TryGetAttributeValue(attributes, keys, out var value))
        {
            lines.Add($"{title}: {Collapse(value, 44)}");
        }
    }

    private static bool TryGetAttributeValue(IReadOnlyDictionary<string, string> attributes, IReadOnlyList<string> keys, out string value)
    {
        foreach (var key in keys)
        {
            if (attributes.TryGetValue(key, out var exactValue) && !string.IsNullOrWhiteSpace(exactValue))
            {
                value = exactValue;
                return true;
            }
        }

        foreach (var pair in attributes)
        {
            if (string.IsNullOrWhiteSpace(pair.Value) || IsNoiseAttribute(pair.Key))
            {
                continue;
            }

            foreach (var key in keys)
            {
                if (pair.Key.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool IsNoiseAttribute(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        ReadOnlySpan<string> ignored = [
            "id",
            "ref",
            "key",
            "x",
            "y",
            "width",
            "height",
            "assemblyqualifiedname",
            "namespace",
            "class"
        ];

        foreach (var token in ignored)
        {
            if (key.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Collapse(string value, int maxLength)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static string MapLane(WorkflowComponentType type)
    {
        return type switch
        {
            WorkflowComponentType.Trigger => "Trigger Context",
            WorkflowComponentType.Condition => "Decision Logic",
            WorkflowComponentType.Action => "Actions",
            WorkflowComponentType.ChildWorkflow => "Child Workflows",
            WorkflowComponentType.ExternalCall => "External Calls",
            WorkflowComponentType.Stop => "Terminal States",
            _ => "Actions"
        };
    }
}

