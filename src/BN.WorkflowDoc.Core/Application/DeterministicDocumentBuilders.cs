using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Application;

public sealed class DeterministicWorkflowDocumentBuilder : IWorkflowDocumentBuilder
{
    private readonly IDiagramGraphMapper _diagramGraphMapper;

    public DeterministicWorkflowDocumentBuilder()
        : this(new DeterministicDiagramGraphMapper())
    {
    }

    public DeterministicWorkflowDocumentBuilder(IDiagramGraphMapper diagramGraphMapper)
    {
        _diagramGraphMapper = diagramGraphMapper;
    }

    public Task<ParseResult<WorkflowDocumentModel>> BuildAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagramResult = _diagramGraphMapper.Map(workflow);
        var diagrams = diagramResult.Value ?? Array.Empty<DiagramGraph>();

        var warnings = workflow.Warnings
            .Concat(diagramResult.Warnings)
            .ToArray();

        var sections = new List<TraceableNarrativeSection>
        {
            BuildPurposeSection(workflow),
            BuildTriggerSection(workflow),
            BuildLogicSection(workflow),
            BuildDependencySection(workflow)
        };

        var model = new WorkflowDocumentModel(
            WorkflowName: workflow.DisplayName,
            Purpose: BuildPurposeSummary(workflow),
            Trigger: workflow.Trigger,
            ExecutionMode: workflow.ExecutionMode,
            Sections: sections,
            Steps: BuildStepDetails(workflow),
            Transitions: BuildTransitionDetails(workflow),
            Diagrams: diagrams,
            Warnings: warnings);

        var status = warnings.Length == 0 ? ProcessingStatus.Success : ProcessingStatus.PartialSuccess;
        return Task.FromResult(new ParseResult<WorkflowDocumentModel>(status, model, warnings));
    }

    private static TraceableNarrativeSection BuildPurposeSection(WorkflowDefinition workflow)
    {
        var narrative = BuildPurposeSummary(workflow);
        var traces = new[]
        {
            new SourceTrace("Name", "Workflow/@Name", "Workflow display name from customizations metadata."),
            new SourceTrace("PrimaryEntity", "Workflow/@PrimaryEntity", null),
            new SourceTrace("ExecutionMode", "Workflow/@Mode", null)
        };

        return new TraceableNarrativeSection("Purpose", narrative, traces);
    }

    private static TraceableNarrativeSection BuildTriggerSection(WorkflowDefinition workflow)
    {
        var trigger = workflow.Trigger;
        var events = new List<string>(3);
        if (trigger.OnCreate)
        {
            events.Add("create");
        }

        if (trigger.OnUpdate)
        {
            events.Add("update");
        }

        if (trigger.OnDelete)
        {
            events.Add("delete");
        }

        var eventText = events.Count == 0 ? "manual invocation or unspecified event" : string.Join(", ", events);
        var filterText = trigger.AttributeFilters.Count == 0
            ? "No attribute-change filter metadata was found."
            : $"Attribute filters: {string.Join(", ", trigger.AttributeFilters)}.";

        var narrative = $"Trigger entity: {trigger.PrimaryEntity}. Runs on {eventText}. {filterText}";
        var traces = new[]
        {
            new SourceTrace("PrimaryEntity", "Workflow/@PrimaryEntity", null),
            new SourceTrace("TriggerFlags", "Workflow/@OnCreate|@OnUpdate|@OnDelete", null),
            new SourceTrace("AttributeFilter", "Workflow/@AttributeFilter", "Also inspects descendant FilteredAttribute/Attribute nodes.")
        };

        return new TraceableNarrativeSection("Trigger Behavior", narrative, traces);
    }

    private static TraceableNarrativeSection BuildLogicSection(WorkflowDefinition workflow)
    {
        var conditionText = workflow.RootCondition is null
            ? "No explicit condition tree was extracted."
            : $"Condition root: {FormatConditionNode(workflow.RootCondition)}.";

        var nodeCount = workflow.StageGraph.Nodes.Count;
        var edgeCount = workflow.StageGraph.Edges.Count;
        var narrative = $"Graph contains {nodeCount} nodes and {edgeCount} edges. {conditionText}";

        var traces = new[]
        {
            new SourceTrace("StageGraph", "Workflow/Steps/*", "Branch containers are mapped into labeled edges and synthetic merge nodes."),
            new SourceTrace("ConditionTree", "Workflow//Condition|Filter|Criteria", null)
        };

        return new TraceableNarrativeSection("Process Logic", narrative, traces);
    }

    private static TraceableNarrativeSection BuildDependencySection(WorkflowDefinition workflow)
    {
        var narrative = workflow.Dependencies.Count == 0
            ? "No downstream dependencies were identified in parsed metadata."
            : $"Dependencies: {string.Join("; ", workflow.Dependencies.Select(x => $"{x.DependencyType}={x.Name}"))}.";

        var traces = new[]
        {
            new SourceTrace("Dependencies", "Workflow//ChildWorkflow|ExternalCall|Action[@Reference*]", null)
        };

        return new TraceableNarrativeSection("Dependencies", narrative, traces);
    }

    private static string BuildPurposeSummary(WorkflowDefinition workflow)
    {
        var modeText = workflow.ExecutionMode == ExecutionMode.Synchronous ? "synchronous" : "asynchronous";
        return $"{workflow.DisplayName} runs as a {modeText} workflow on {workflow.Trigger.PrimaryEntity} records to execute configured business actions.";
    }

    private static IReadOnlyList<WorkflowStepDetail> BuildStepDetails(WorkflowDefinition workflow)
    {
        if (workflow.StageGraph.Nodes.Count == 0)
        {
            return Array.Empty<WorkflowStepDetail>();
        }

        var incomingLookup = workflow.StageGraph.Edges
            .GroupBy(x => x.ToNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<WorkflowEdge>)x.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var outgoingLookup = workflow.StageGraph.Edges
            .GroupBy(x => x.FromNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<WorkflowEdge>)x.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var nodeLookup = workflow.StageGraph.Nodes
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

        return workflow.StageGraph.Nodes
            .Select((node, index) =>
            {
                incomingLookup.TryGetValue(node.Id, out var incomingEdges);
                outgoingLookup.TryGetValue(node.Id, out var outgoingEdges);

                var incomingPaths = (incomingEdges ?? Array.Empty<WorkflowEdge>())
                    .Select(edge => DescribePath(edge, nodeLookup, incoming: true))
                    .ToArray();
                var outgoingPaths = (outgoingEdges ?? Array.Empty<WorkflowEdge>())
                    .Select(edge => DescribePath(edge, nodeLookup, incoming: false))
                    .ToArray();
                var attributes = node.Attributes
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new WorkflowStepAttribute(x.Key, x.Value))
                    .ToArray();

                return new WorkflowStepDetail(
                    Sequence: index + 1,
                    StepId: node.Id,
                    StepType: node.ComponentType.ToString(),
                    Label: node.Label,
                    IsSynthetic: node.Attributes.TryGetValue("Synthetic", out var syntheticValue)
                        && string.Equals(syntheticValue, "true", StringComparison.OrdinalIgnoreCase),
                    IncomingPaths: incomingPaths,
                    OutgoingPaths: outgoingPaths,
                    Attributes: attributes,
                    Narrative: BuildStepNarrative(node, incomingPaths, outgoingPaths, attributes));
            })
            .ToArray();
    }

    private static IReadOnlyList<WorkflowTransitionDetail> BuildTransitionDetails(WorkflowDefinition workflow)
    {
        if (workflow.StageGraph.Edges.Count == 0)
        {
            return Array.Empty<WorkflowTransitionDetail>();
        }

        var nodeLookup = workflow.StageGraph.Nodes
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

        return workflow.StageGraph.Edges
            .Select((edge, index) =>
            {
                var fromLabel = nodeLookup.TryGetValue(edge.FromNodeId, out var fromNode)
                    ? fromNode.Label
                    : edge.FromNodeId;
                var toLabel = nodeLookup.TryGetValue(edge.ToNodeId, out var toNode)
                    ? toNode.Label
                    : edge.ToNodeId;
                var conditionText = string.IsNullOrWhiteSpace(edge.ConditionLabel)
                    ? "default path"
                    : $"branch '{edge.ConditionLabel}'";

                return new WorkflowTransitionDetail(
                    Sequence: index + 1,
                    FromStepId: edge.FromNodeId,
                    FromStepLabel: fromLabel,
                    ToStepId: edge.ToNodeId,
                    ToStepLabel: toLabel,
                    ConditionLabel: edge.ConditionLabel,
                    Narrative: $"{fromLabel} -> {toLabel} via {conditionText}.");
            })
            .ToArray();
    }

    private static string DescribePath(WorkflowEdge edge, IReadOnlyDictionary<string, WorkflowNode> nodeLookup, bool incoming)
    {
        var relatedNodeId = incoming ? edge.FromNodeId : edge.ToNodeId;
        var direction = incoming ? "From" : "To";
        var targetLabel = nodeLookup.TryGetValue(relatedNodeId, out var relatedNode)
            ? relatedNode.Label
            : relatedNodeId;
        var branchText = string.IsNullOrWhiteSpace(edge.ConditionLabel)
            ? string.Empty
            : $" via branch '{edge.ConditionLabel}'";

        return $"{direction} {targetLabel}{branchText}";
    }

    private static string BuildStepNarrative(
        WorkflowNode node,
        IReadOnlyList<string> incomingPaths,
        IReadOnlyList<string> outgoingPaths,
        IReadOnlyList<WorkflowStepAttribute> attributes)
    {
        var entryText = incomingPaths.Count == 0
            ? "It has no recorded inbound transitions."
            : incomingPaths.Count == 1
                ? $"It receives control from 1 prior path."
                : $"It receives control from {incomingPaths.Count} prior paths.";

        var exitText = outgoingPaths.Count == 0
            ? "It terminates the current execution path."
            : outgoingPaths.Count == 1
                ? "It continues to 1 downstream path."
                : $"It fans out to {outgoingPaths.Count} downstream paths.";

        var attributeText = attributes.Count == 0
            ? "No additional parser attributes were captured for this step."
            : $"Captured metadata fields: {string.Join(", ", attributes.Select(x => x.Name))}.";

        var businessActionText = BuildBusinessActionSummary(node, attributes);

        return $"This {node.ComponentType} step is labeled '{node.Label}'. {entryText} {exitText} {businessActionText} {attributeText}";
    }

    private static string BuildBusinessActionSummary(WorkflowNode node, IReadOnlyList<WorkflowStepAttribute> attributes)
    {
        if (node.ComponentType == WorkflowComponentType.Condition)
        {
            return "It evaluates decision criteria and routes processing based on matching conditions.";
        }

        if (node.ComponentType == WorkflowComponentType.Trigger)
        {
            return "It represents the workflow entry point from Dataverse events.";
        }

        var lookup = attributes
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase);

        var actionType = TryGetValue(lookup, "ActionType", "Type", "MessageName", "Operation");
        var entity = TryGetValue(lookup, "Entity", "EntityName", "PrimaryEntity", "TargetEntity");
        var target = TryGetValue(lookup, "Target", "TargetAttribute", "Attribute", "Field", "Column", "Name");
        var value = TryGetValue(lookup, "Value", "DefaultValue", "Literal", "Constant");

        if (!string.IsNullOrWhiteSpace(actionType) && !string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(value))
        {
            var entityText = string.IsNullOrWhiteSpace(entity) ? "record" : $"{entity} record";
            return $"Business intent: {actionType} updates {entityText} field '{target}' to '{value}'.";
        }

        if (!string.IsNullOrWhiteSpace(actionType) && !string.IsNullOrWhiteSpace(entity))
        {
            return $"Business intent: {actionType} operation against {entity} records.";
        }

        if (!string.IsNullOrWhiteSpace(actionType))
        {
            return $"Business intent: executes {actionType} as part of the workflow automation.";
        }

        return node.ComponentType switch
        {
            WorkflowComponentType.Action => "Business intent: performs a configured Dataverse action.",
            WorkflowComponentType.ChildWorkflow => "Business intent: invokes a child workflow for delegated processing.",
            WorkflowComponentType.ExternalCall => "Business intent: calls an external dependency or integration endpoint.",
            WorkflowComponentType.Stop => "Business intent: stops further processing for this branch.",
            _ => "Business intent: advances workflow processing."
        };
    }

    private static string? TryGetValue(IReadOnlyDictionary<string, string> attributes, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string FormatConditionNode(ConditionNode node)
    {
        if (node.Children.Count == 0)
        {
            var left = string.IsNullOrWhiteSpace(node.Left) ? "<left>" : node.Left;
            var right = string.IsNullOrWhiteSpace(node.Right) ? "<right>" : node.Right;
            return $"{left} {node.Operator} {right}";
        }

        var joinedChildren = string.Join($" {node.Operator} ", node.Children.Select(FormatConditionNode));
        return $"({joinedChildren})";
    }
}

public sealed class DeterministicOverviewDocumentBuilder : IOverviewDocumentBuilder
{
    private readonly IWorkflowAnalysisEngine _analysisEngine;
    private readonly IDependencyGraphBuilder _dependencyGraphBuilder;

    public DeterministicOverviewDocumentBuilder()
        : this(new WorkflowAnalysisEngine(), new DependencyGraphBuilder())
    {
    }

    public DeterministicOverviewDocumentBuilder(IWorkflowAnalysisEngine analysisEngine)
        : this(analysisEngine, new DependencyGraphBuilder())
    {
    }

    public DeterministicOverviewDocumentBuilder(
        IWorkflowAnalysisEngine analysisEngine,
        IDependencyGraphBuilder dependencyGraphBuilder)
    {
        _analysisEngine = analysisEngine;
        _dependencyGraphBuilder = dependencyGraphBuilder;
    }

    public Task<ParseResult<OverviewDocumentModel>> BuildAsync(
        string solutionName,
        IReadOnlyList<WorkflowDefinition> workflows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cards = workflows.Select(BuildCard).ToArray();
        var warnings = workflows.SelectMany(x => x.Warnings).ToArray();
        var dependencyGraph = _dependencyGraphBuilder.Build(workflows);

        var model = new OverviewDocumentModel(
            SolutionName: string.IsNullOrWhiteSpace(solutionName) ? "Unnamed Solution" : solutionName,
            Workflows: cards,
            GlobalWarnings: warnings,
            DependencyGraph: dependencyGraph);

        var status = warnings.Length == 0 ? ProcessingStatus.Success : ProcessingStatus.PartialSuccess;
        return Task.FromResult(new ParseResult<OverviewDocumentModel>(status, model, warnings));
    }

    private OverviewWorkflowCard BuildCard(WorkflowDefinition workflow)
    {
        var triggerEvents = new List<string>(3);
        if (workflow.Trigger.OnCreate)
        {
            triggerEvents.Add("create");
        }

        if (workflow.Trigger.OnUpdate)
        {
            triggerEvents.Add("update");
        }

        if (workflow.Trigger.OnDelete)
        {
            triggerEvents.Add("delete");
        }

        var triggerSummary = triggerEvents.Count == 0
            ? $"{workflow.Trigger.PrimaryEntity} (manual/unknown)"
            : $"{workflow.Trigger.PrimaryEntity} ({string.Join(", ", triggerEvents)})";

        var purpose = $"Automates {workflow.Trigger.PrimaryEntity} handling for {workflow.DisplayName}.";
        var dependencyNames = workflow.Dependencies.Select(x => $"{x.DependencyType}:{x.Name}").ToArray();
        var keyRisks = BuildKeyRisks(workflow);
        var complexityScore = CalculateComplexity(workflow);
        var qualityScore = _analysisEngine.Analyze(workflow, workflow.Warnings);
        var warningCodes = workflow.Warnings.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new OverviewWorkflowCard(
            WorkflowName: workflow.DisplayName,
            Purpose: purpose,
            TriggerSummary: triggerSummary,
            ExecutionMode: workflow.ExecutionMode,
            ComplexityScore: complexityScore,
            Dependencies: dependencyNames,
            KeyRisks: keyRisks,
            QualityScore: qualityScore,
            WarningCodes: warningCodes);
    }

    private static string[] BuildKeyRisks(WorkflowDefinition workflow)
    {
        var risks = new List<string>();

        if (workflow.ExecutionMode == ExecutionMode.Synchronous && workflow.StageGraph.Nodes.Count >= 12)
        {
            risks.Add("Large synchronous workflow may impact transaction latency.");
        }

        if (workflow.Dependencies.Any(x => x.DependencyType == "ExternalCall"))
        {
            risks.Add("External call dependency can introduce availability and timeout risks.");
        }

        if (workflow.RootCondition is null)
        {
            risks.Add("No extracted condition tree; decision intent may be under-documented.");
        }

        if (risks.Count == 0)
        {
            risks.Add("No high-risk indicators detected from extracted metadata.");
        }

        return risks.ToArray();
    }

    private static int CalculateComplexity(WorkflowDefinition workflow)
    {
        var conditionWeight = CountConditionNodes(workflow.RootCondition);
        var dependencyWeight = workflow.Dependencies.Count * 2;
        return workflow.StageGraph.Nodes.Count + workflow.StageGraph.Edges.Count + conditionWeight + dependencyWeight;
    }

    private static int CountConditionNodes(ConditionNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        return 1 + node.Children.Sum(CountConditionNodes);
    }
}

