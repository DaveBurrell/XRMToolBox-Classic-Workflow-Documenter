using System.Text;
using System.Xml;
using System.Xml.Linq;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Parsing;

public sealed class WorkflowDefinitionParser : IWorkflowDefinitionParser
{
    public async Task<ParseResult<IReadOnlyList<WorkflowDefinition>>> ParseAsync(
        SolutionPackage package,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<ProcessingWarning>();

        var customizationsPath = Path.Combine(package.ExtractedPath, "customizations.xml");
        if (!File.Exists(customizationsPath))
        {
            warnings.Add(new ProcessingWarning(
                "MISSING_CUSTOMIZATIONS_XML",
                "customizations.xml was not found in extracted solution.",
                customizationsPath,
                true,
                WarningCategory.Parsing,
                WarningSeverity.Error));
            return new ParseResult<IReadOnlyList<WorkflowDefinition>>(ProcessingStatus.Failed, null, warnings, "customizations.xml was not found.");
        }

        try
        {
            await using var stream = File.OpenRead(customizationsPath);
            var xmlSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, Async = true };
            using var xmlReader = XmlReader.Create(stream, xmlSettings);
            var document = await XDocument.LoadAsync(xmlReader, LoadOptions.None, cancellationToken).ConfigureAwait(false);

            var workflows = new List<WorkflowDefinition>();
            var workflowElements = XmlNavigation.DescendantsByLocalName(document, "Workflow");

            foreach (var element in workflowElements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var idText = XmlNavigation.ReadAttributeOrElement(element, "WorkflowId", "Id", "WorkflowID");
                var displayName = XmlNavigation.ReadAttributeOrElement(element, "Name", "DisplayName") ?? "Unnamed Workflow";
                var primaryEntity = XmlNavigation.ReadAttributeOrElement(element, "PrimaryEntity", "Entity") ?? "unknown";
                var modeRaw = XmlNavigation.ReadAttributeOrElement(element, "Mode", "ExecutionMode");
                var category = XmlNavigation.ReadAttributeOrElement(element, "Category") ?? "classic";
                var scope = XmlNavigation.ReadAttributeOrElement(element, "Scope") ?? "organization";
                var owner = XmlNavigation.ReadAttributeOrElement(element, "Owner");

                var workflowId = Guid.TryParse(idText, out var parsedId) ? parsedId : Guid.NewGuid();
                if (!Guid.TryParse(idText, out _))
                {
                    warnings.Add(new ProcessingWarning(
                        "WORKFLOW_ID_INVALID",
                        $"Workflow '{displayName}' has no valid id; generated fallback id.",
                        idText,
                        false,
                        WarningCategory.Validation,
                        WarningSeverity.Warning));
                }

                // D365 CRM: Mode="0" = background (async), Mode="1" = real-time (sync)
                var executionMode = string.Equals(modeRaw, "sync", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(modeRaw, "1", StringComparison.Ordinal)
                    ? ExecutionMode.Synchronous
                    : ExecutionMode.Asynchronous;

                var trigger = ParseTrigger(element, primaryEntity);
                var graph = ParseStageGraph(element, package.ExtractedPath, displayName, warnings);
                var dependencies = ParseDependencies(element);
                var rootCondition = ParseRootCondition(element);

                workflows.Add(new WorkflowDefinition(
                    WorkflowId: workflowId,
                    LogicalName: displayName.Replace(" ", string.Empty, StringComparison.Ordinal),
                    DisplayName: displayName,
                    Category: category,
                    Scope: scope,
                    Owner: owner,
                    ExecutionMode: executionMode,
                        Trigger: trigger,
                        StageGraph: graph,
                        RootCondition: rootCondition,
                        Dependencies: dependencies,
                    Warnings: Array.Empty<ProcessingWarning>()));
            }

            var status = warnings.Count == 0 ? ProcessingStatus.Success : ProcessingStatus.PartialSuccess;
            return new ParseResult<IReadOnlyList<WorkflowDefinition>>(status, workflows, warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new ProcessingWarning(
                "WORKFLOW_PARSE_FAILED",
                ex.Message,
                customizationsPath,
                true,
                WarningCategory.Parsing,
                WarningSeverity.Critical));
            return new ParseResult<IReadOnlyList<WorkflowDefinition>>(ProcessingStatus.Failed, null, warnings, ex.Message);
        }
    }

    private static WorkflowTrigger ParseTrigger(XElement workflowElement, string primaryEntity)
    {
        var onCreate = ReadBool(workflowElement, false, "OnCreate", "TriggerOnCreate", "Create", "RunOnCreate");
        var onUpdate = ReadBool(workflowElement, true, "OnUpdate", "TriggerOnUpdate", "Update", "RunOnUpdate");
        var onDelete = ReadBool(workflowElement, false, "OnDelete", "TriggerOnDelete", "Delete", "RunOnDelete");

        var attributeFilters = ReadAttributeFilterList(workflowElement);
        var triggerDescription = BuildTriggerDescription(onCreate, onUpdate, onDelete, attributeFilters);

        return new WorkflowTrigger(primaryEntity, onCreate, onUpdate, onDelete, attributeFilters, triggerDescription);
    }

    private static WorkflowStageGraph ParseStageGraph(
        XElement workflowElement,
        string extractedPath,
        string workflowName,
        List<ProcessingWarning> warnings)
    {
        var rootElements = GetRootGraphElements(workflowElement, extractedPath, workflowName, warnings).ToList();
        if (rootElements.Count == 0)
        {
            return WorkflowStageGraph.Empty;
        }

        var nodes = new List<WorkflowNode>(rootElements.Count + 2);
        var edges = new List<WorkflowEdge>(rootElements.Count + 2);

        const string triggerNodeId = "trigger";
        nodes.Add(new WorkflowNode(
            triggerNodeId,
            WorkflowComponentType.Trigger,
            "Trigger",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

        var nodeCounter = 0;
        ParseSequence(rootElements, triggerNodeId, nodes, edges, warnings, workflowName, ref nodeCounter);

        return new WorkflowStageGraph(nodes, edges);
    }

    private static string ParseSequence(
        IReadOnlyList<XElement> elements,
        string previousNodeId,
        List<WorkflowNode> nodes,
        List<WorkflowEdge> edges,
        List<ProcessingWarning> warnings,
        string workflowName,
        ref int nodeCounter)
    {
        var currentPrevious = previousNodeId;

        foreach (var element in elements)
        {
            if (!IsGraphElement(element))
            {
                continue;
            }

            var nodeId = NextNodeId(ref nodeCounter);
            var aqn = element.Attribute("AssemblyQualifiedName")?.Value;
            var type = MapComponentType(element.Name.LocalName, aqn);
            var label = GetGraphNodeLabel(element);

            if (string.Equals(element.Name.LocalName, "Action", StringComparison.OrdinalIgnoreCase))
            {
                var actionType = XmlNavigation.ReadAttributeOrElement(element, "Type", "ActionType");
                if (string.IsNullOrWhiteSpace(actionType))
                {
                    warnings.Add(new ProcessingWarning(
                        "ACTION_TYPE_UNKNOWN",
                        $"Workflow '{workflowName}' has action node '{label}' without explicit action type.",
                        label,
                        false,
                        WarningCategory.Validation,
                        WarningSeverity.Warning));
                }
            }

            var attributes = element.Attributes()
                .GroupBy(x => x.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase);

            nodes.Add(new WorkflowNode(nodeId, type, label, attributes));

            if (type == WorkflowComponentType.Condition)
            {
                edges.Add(new WorkflowEdge(currentPrevious, nodeId));
                currentPrevious = ParseConditionBranches(
                    element,
                    nodeId,
                    nodes,
                    edges,
                    warnings,
                    workflowName,
                    ref nodeCounter);
                continue;
            }

            edges.Add(new WorkflowEdge(currentPrevious, nodeId));
            currentPrevious = nodeId;
        }

        return currentPrevious;
    }

    private static string ParseConditionBranches(
        XElement conditionElement,
        string conditionNodeId,
        List<WorkflowNode> nodes,
        List<WorkflowEdge> edges,
        List<ProcessingWarning> warnings,
        string workflowName,
        ref int nodeCounter)
    {
        var branches = conditionElement
            .Elements()
            .Where(IsBranchContainer)
            .Select(x => new
            {
                Label = GetBranchLabel(x.Name.LocalName),
                Elements = FlattenToGraphElements(x).ToArray()
            })
            .Where(x => x.Elements.Length > 0)
            .ToList();

        if (branches.Count == 0)
        {
            return conditionNodeId;
        }

        var mergeNodeId = NextNodeId(ref nodeCounter);
        nodes.Add(new WorkflowNode(
            mergeNodeId,
            WorkflowComponentType.Action,
            "Merge",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Synthetic"] = "true"
            }));

        foreach (var branch in branches)
        {
            var branchStartNodeId = NextNodeId(ref nodeCounter);
            var firstElement = branch.Elements[0];
            var branchAqn = firstElement.Attribute("AssemblyQualifiedName")?.Value;
            var branchType = MapComponentType(firstElement.Name.LocalName, branchAqn);
            var branchLabel = GetGraphNodeLabel(firstElement);

            if (string.Equals(firstElement.Name.LocalName, "Action", StringComparison.OrdinalIgnoreCase))
            {
                var actionType = XmlNavigation.ReadAttributeOrElement(firstElement, "Type", "ActionType");
                if (string.IsNullOrWhiteSpace(actionType))
                {
                    warnings.Add(new ProcessingWarning(
                        "ACTION_TYPE_UNKNOWN",
                        $"Workflow '{workflowName}' has action node '{branchLabel}' without explicit action type.",
                        branchLabel,
                        false,
                        WarningCategory.Validation,
                        WarningSeverity.Warning));
                }
            }

            var branchAttributes = firstElement.Attributes()
                .GroupBy(x => x.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase);

            nodes.Add(new WorkflowNode(branchStartNodeId, branchType, branchLabel, branchAttributes));
            edges.Add(new WorkflowEdge(conditionNodeId, branchStartNodeId, branch.Label));

            var branchTail = ParseSequence(
                branch.Elements.Skip(1).ToArray(),
                branchStartNodeId,
                nodes,
                edges,
                warnings,
                workflowName,
                ref nodeCounter);

            edges.Add(new WorkflowEdge(branchTail, mergeNodeId));
        }

        return mergeNodeId;
    }

    private static IEnumerable<XElement> GetRootGraphElements(
        XElement workflowElement,
        string extractedPath,
        string workflowName,
        List<ProcessingWarning> warnings)
    {
        // Try simple structured XML containers first (test fixtures, older formats)
        var container = XmlNavigation.FirstElementByLocalName(workflowElement, "Steps", "Stages", "WorkflowNodes", "Process");

        if (container is not null)
        {
            return FlattenToGraphElements(container);
        }

        // Most unmanaged solutions store workflow logic in external XAML files referenced
        // by <XamlFileName>/Workflows/*.xaml</XamlFileName>.
        var externalXamlElements = TryExtractExternalXamlRootElements(workflowElement, extractedPath, workflowName, warnings);
        if (externalXamlElements is not null)
        {
            return externalXamlElements;
        }

        // Real D365 classic workflows embed WF4 XAML inside a <Xml> child element
        var xamlElements = TryExtractXamlRootElements(workflowElement);
        if (xamlElements is not null)
        {
            return xamlElements;
        }

        return FlattenToGraphElements(workflowElement);
    }

    private static IEnumerable<XElement>? TryExtractXamlRootElements(XElement workflowElement)
    {
        var xmlEl = XmlNavigation.FirstElementByLocalName(workflowElement, "Xml", "XamlWorkflow", "WorkflowXml");

        if (xmlEl is null) return null;

        // Case 1: <Xml> has direct child XML elements (XAML embedded inline)
        if (xmlEl.HasElements)
        {
            return FlattenToGraphElements(FindWorkflowBody(xmlEl.Elements().First()));
        }

        var content = xmlEl.Value?.Trim();
        if (string.IsNullOrWhiteSpace(content)) return null;

        // Case 2: text content is raw XAML XML
        XDocument? xamlDoc = null;
        if (content.StartsWith('<'))
        {
            try { xamlDoc = XDocument.Parse(content); } catch (XmlException) { /* not valid XML */ }
        }

        // Case 3: base64-encoded XAML (try UTF-16 then UTF-8)
        if (xamlDoc is null)
        {
            try
            {
                var bytes = Convert.FromBase64String(content);
                string? decoded = null;
                try
                {
                    decoded = Encoding.Unicode.GetString(bytes);
                    if (!decoded.TrimStart().StartsWith('<')) decoded = null;
                }
                catch (DecoderFallbackException) { decoded = null; }

                if (decoded is null)
                {
                    try
                    {
                        decoded = Encoding.UTF8.GetString(bytes);
                        if (!decoded.TrimStart().StartsWith('<')) decoded = null;
                    }
                    catch (DecoderFallbackException) { decoded = null; }
                }

                if (decoded is not null)
                {
                    try { xamlDoc = XDocument.Parse(decoded); } catch (XmlException) { /* not valid XML */ }
                }
            }
            catch (FormatException) { /* not base64 */ }
        }

        if (xamlDoc?.Root is null) return null;

        return FlattenToGraphElements(FindWorkflowBody(xamlDoc.Root));
    }

    private static IEnumerable<XElement>? TryExtractExternalXamlRootElements(
        XElement workflowElement,
        string extractedPath,
        string workflowName,
        List<ProcessingWarning> warnings)
    {
        var xamlFileName = XmlNavigation.ReadAttributeOrElement(
            workflowElement,
            "XamlFileName",
            "WorkflowXamlFileName",
            "XamlPath");

        if (string.IsNullOrWhiteSpace(xamlFileName))
        {
            return null;
        }

        var relativePath = xamlFileName
            .Trim()
            .TrimStart('/', '\\')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.Combine(extractedPath, relativePath);
        var canonicalExtracted = Path.GetFullPath(extractedPath) + Path.DirectorySeparatorChar;
        var canonicalFull = Path.GetFullPath(fullPath);
        if (!canonicalFull.StartsWith(canonicalExtracted, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new ProcessingWarning(
                "XAML_PATH_TRAVERSAL_BLOCKED",
                $"Workflow '{workflowName}' XAML path '{xamlFileName}' was blocked: path traversal detected.",
                xamlFileName,
                false,
                WarningCategory.Parsing,
                WarningSeverity.Warning));
            return null;
        }

        if (!File.Exists(fullPath))
        {
            warnings.Add(new ProcessingWarning(
                "WORKFLOW_XAML_NOT_FOUND",
                $"Workflow '{workflowName}' references XAML file '{xamlFileName}' that was not found in extracted package.",
                fullPath,
                false,
                WarningCategory.Parsing,
                WarningSeverity.Warning));
            return null;
        }

        try
        {
            var xamlXmlSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            using var xamlXmlReader = XmlReader.Create(fullPath, xamlXmlSettings);
            var xamlDoc = XDocument.Load(xamlXmlReader, LoadOptions.None);
            if (xamlDoc.Root is null)
            {
                return null;
            }

            return FlattenToGraphElements(FindWorkflowBody(xamlDoc.Root));
        }
        catch (Exception ex)
        {
            warnings.Add(new ProcessingWarning(
                "WORKFLOW_XAML_PARSE_FAILED",
                $"Workflow '{workflowName}' XAML file could not be parsed: {ex.Message}",
                fullPath,
                false,
                WarningCategory.Parsing,
                WarningSeverity.Error));
            return null;
        }
    }

    // Navigates past the XAML root <Activity> and <mxswa:Workflow> wrappers to the
    // element that directly contains the process steps.
    private static XElement FindWorkflowBody(XElement root)
    {
        // The innermost "Workflow" element (but not a <Workflows> collection parent)
        var body = root.Descendants().FirstOrDefault(x =>
            string.Equals(x.Name.LocalName, "Workflow", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(x.Parent?.Name.LocalName, "Workflows", StringComparison.OrdinalIgnoreCase));

        return body ?? root;
    }

    // Recursively expands transparent container elements (Sequence, Workflow, Activity)
    // and yields only true graph-node elements.
    private static IEnumerable<XElement> FlattenToGraphElements(XElement container)
    {
        foreach (var child in container.Elements())
        {
            if (IsTransparentContainer(child))
            {
                foreach (var nested in FlattenToGraphElements(child))
                {
                    yield return nested;
                }
            }
            else if (IsGraphElement(child))
            {
                yield return child;
            }
        }
    }

    private static bool IsTransparentContainer(XElement element)
    {
        var n = element.Name.LocalName;
        return string.Equals(n, "Sequence", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "Workflow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, "Activity", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBranchContainer(XElement element)
    {
        return string.Equals(element.Name.LocalName, "True", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "False", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Then", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Else", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "No", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Default", StringComparison.OrdinalIgnoreCase)
            // WF4 XAML property elements used by <If> activities
            || string.Equals(element.Name.LocalName, "If.Then", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "If.Else", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetBranchLabel(string localName)
    {
        if (string.Equals(localName, "True", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "Then", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "Yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "If.Then", StringComparison.OrdinalIgnoreCase))
        {
            return "True";
        }

        if (string.Equals(localName, "False", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "Else", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "No", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "If.Else", StringComparison.OrdinalIgnoreCase))
        {
            return "False";
        }

        return "Default";
    }

    private static string NextNodeId(ref int nodeCounter)
    {
        nodeCounter++;
        return $"n{nodeCounter}";
    }

    private static bool IsGraphElement(XElement element)
    {
        return string.Equals(element.Name.LocalName, "Stage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Step", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Condition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Action", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Assign", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Stop", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "ChildWorkflow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "ExternalCall", StringComparison.OrdinalIgnoreCase)
            // WF4 XAML (Windows Workflow Foundation) activity types used in D365 classic workflows
            || string.Equals(element.Name.LocalName, "ActivityReference", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "If", StringComparison.OrdinalIgnoreCase);
    }

    private static WorkflowComponentType MapComponentType(string localName, string? assemblyQualifiedName = null)
    {
        if (string.Equals(localName, "Condition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "If", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowComponentType.Condition;
        }

        if (string.Equals(localName, "Action", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "Assign", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "Step", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "Stage", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowComponentType.Action;
        }

        if (string.Equals(localName, "Stop", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowComponentType.Stop;
        }

        if (string.Equals(localName, "ChildWorkflow", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowComponentType.ChildWorkflow;
        }

        if (string.Equals(localName, "ExternalCall", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowComponentType.ExternalCall;
        }

        // WF4 ActivityReference — infer type from AssemblyQualifiedName class name
        if (string.Equals(localName, "ActivityReference", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(assemblyQualifiedName))
        {
            if (assemblyQualifiedName.Contains("ChildWorkflow", StringComparison.OrdinalIgnoreCase)
                || assemblyQualifiedName.Contains("StartWorkflow", StringComparison.OrdinalIgnoreCase))
            {
                return WorkflowComponentType.ChildWorkflow;
            }

            if (assemblyQualifiedName.Contains("StopWorkflow", StringComparison.OrdinalIgnoreCase)
                || assemblyQualifiedName.Contains("TerminateWorkflow", StringComparison.OrdinalIgnoreCase))
            {
                return WorkflowComponentType.Stop;
            }

            if (assemblyQualifiedName.Contains("WebService", StringComparison.OrdinalIgnoreCase)
                || assemblyQualifiedName.Contains("CustomActivity", StringComparison.OrdinalIgnoreCase))
            {
                return WorkflowComponentType.ExternalCall;
            }
        }

        return WorkflowComponentType.Action;
    }

    // Resolves a human-readable label for a graph node element.
    // Prefers DisplayName (used by XAML) then Name/Label/Description/Title.
    // For ActivityReference elements, falls back to formatting the class name.
    private static string GetGraphNodeLabel(XElement element)
    {
        var label = XmlNavigation.ReadAttributeOrElement(element, "DisplayName", "Name", "Label", "Description", "Title");
        if (!string.IsNullOrWhiteSpace(label)) return label.Trim();

        if (string.Equals(element.Name.LocalName, "ActivityReference", StringComparison.OrdinalIgnoreCase))
        {
            var aqn = element.Attribute("AssemblyQualifiedName")?.Value ?? string.Empty;
            var className = aqn.Split(',', 2)[0].Split('.').LastOrDefault();
            if (!string.IsNullOrWhiteSpace(className))
            {
                return FormatActivityClassName(className);
            }
        }

        return element.Name.LocalName;
    }

    // Converts a CRM/Xrm activity class name to a readable step label.
    // E.g. "CrmUpdateActivity" -> "Update", "XrmStartChildWorkflowActivity" -> "Start Child Workflow"
    private static string FormatActivityClassName(string className)
    {
        var name = className;
        foreach (var prefix in new[] { "Crm", "Xrm", "Mxswa" })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        if (name.EndsWith("Activity", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^"Activity".Length];
        }

        // Insert spaces between pascal-case words
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                sb.Append(' ');
            }

            sb.Append(name[i]);
        }

        return sb.ToString().Trim();
    }

    private static bool ReadBool(XElement parent, bool defaultValue, params string[] names)
    {
        var text = XmlNavigation.ReadAttributeOrElement(parent, names);
        return ParseBoolOrDefault(text, defaultValue);
    }

    private static bool ParseBoolOrDefault(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var parsedBool))
        {
            return parsedBool;
        }

        if (int.TryParse(value, out var parsedInt))
        {
            return parsedInt != 0;
        }

        return defaultValue;
    }

    private static IReadOnlyList<string> ReadAttributeFilterList(XElement workflowElement)
    {
        var raw = XmlNavigation.ReadAttributeOrElement(workflowElement, "AttributeFilter", "AttributeFilters", "FilteredAttributes");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        var values = XmlNavigation
            .DescendantsByLocalName(workflowElement, "Attribute", "FilteredAttribute")
            .Select(x => x.Value)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values;
    }

    private static string? BuildTriggerDescription(bool onCreate, bool onUpdate, bool onDelete, IReadOnlyList<string> attributeFilters)
    {
        var events = new List<string>(3);
        if (onCreate)
        {
            events.Add("create");
        }

        if (onUpdate)
        {
            events.Add("update");
        }

        if (onDelete)
        {
            events.Add("delete");
        }

        var eventText = events.Count == 0 ? "manual/unknown trigger" : string.Join(", ", events);
        if (attributeFilters.Count == 0)
        {
            return $"Runs on {eventText}.";
        }

        return $"Runs on {eventText}; filtered attributes: {string.Join(", ", attributeFilters)}.";
    }

    private static IReadOnlyList<WorkflowDependency> ParseDependencies(XElement workflowElement)
    {
        var dependencies = new List<WorkflowDependency>();

        foreach (var descendant in workflowElement.Descendants())
        {
            var localName = descendant.Name.LocalName;

            if (string.Equals(localName, "ChildWorkflow", StringComparison.OrdinalIgnoreCase))
            {
                var name = XmlNavigation.ReadAttributeOrElement(descendant, "Name", "WorkflowName", "DisplayName") ?? "Unnamed Child Workflow";
                var referenceId = XmlNavigation.ReadAttributeOrElement(descendant, "Id", "WorkflowId", "ReferenceId");
                dependencies.Add(new WorkflowDependency("ChildWorkflow", name, referenceId));
            }
            else if (string.Equals(localName, "ExternalCall", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(localName, "CustomActivity", StringComparison.OrdinalIgnoreCase))
            {
                var name = XmlNavigation.ReadAttributeOrElement(descendant, "Name", "Operation", "Assembly", "Class") ?? "Unnamed External Call";
                var referenceId = XmlNavigation.ReadAttributeOrElement(descendant, "Id", "ReferenceId", "PluginTypeId");
                dependencies.Add(new WorkflowDependency("ExternalCall", name, referenceId));
            }
            else if (string.Equals(localName, "Action", StringComparison.OrdinalIgnoreCase))
            {
                var referenceTarget = XmlNavigation.ReadAttributeOrElement(descendant, "ReferenceEntity", "Entity", "TargetEntity", "ReferencedWorkflow");
                if (!string.IsNullOrWhiteSpace(referenceTarget))
                {
                    var name = XmlNavigation.ReadAttributeOrElement(descendant, "Name", "Label") ?? "Referenced Action";
                    dependencies.Add(new WorkflowDependency("Reference", $"{name}:{referenceTarget}", null));
                }
            }
        }

        return dependencies
            .GroupBy(x => $"{x.DependencyType}|{x.Name}|{x.ReferenceId}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    private static ConditionNode? ParseRootCondition(XElement workflowElement)
    {
        var topLevelCondition = workflowElement
            .Descendants()
            .Where(IsConditionElement)
            .FirstOrDefault(x => x.Parent is null || !IsConditionElement(x.Parent));

        return topLevelCondition is null
            ? null
            : ParseConditionNode(topLevelCondition);
    }

    private static ConditionNode ParseConditionNode(XElement conditionElement)
    {
        var mappedFromElementName = MapConditionOperator(conditionElement.Name.LocalName);
        var mappedFromAttribute = MapConditionOperator(
            XmlNavigation.ReadAttributeOrElement(conditionElement, "Operator", "Op", "Comparison", "ConditionOperator"));

        var childConditions = conditionElement
            .Elements()
            .Where(IsConditionElement)
            .Select(ParseConditionNode)
            .ToArray();

        if (childConditions.Length > 0)
        {
            var parentOperator = mappedFromAttribute != ConditionOperator.Custom
                ? mappedFromAttribute
                : mappedFromElementName != ConditionOperator.Custom
                    ? mappedFromElementName
                    : ConditionOperator.And;

            return new ConditionNode(parentOperator, null, null, childConditions);
        }

        var leafOperator = mappedFromAttribute != ConditionOperator.Custom
            ? mappedFromAttribute
            : mappedFromElementName;

        var left = XmlNavigation.ReadAttributeOrElement(conditionElement, "Left", "Lhs", "Attribute", "Field", "Column", "Name", "Label");
        var right = XmlNavigation.ReadAttributeOrElement(conditionElement, "Right", "Rhs", "Value", "CompareValue", "To");

        return ConditionNode.Leaf(leafOperator, left, right);
    }

    private static bool IsConditionElement(XElement element)
    {
        return string.Equals(element.Name.LocalName, "Condition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "ConditionExpression", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Criteria", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Filter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "And", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.Name.LocalName, "Or", StringComparison.OrdinalIgnoreCase);
    }

    private static ConditionOperator MapConditionOperator(string? rawOperator)
    {
        if (string.IsNullOrWhiteSpace(rawOperator))
        {
            return ConditionOperator.Custom;
        }

        return rawOperator.Trim().ToLowerInvariant() switch
        {
            "and" => ConditionOperator.And,
            "or" => ConditionOperator.Or,
            "eq" or "equals" => ConditionOperator.Equals,
            "ne" or "notequals" or "not_equal" => ConditionOperator.NotEquals,
            "gt" or "greaterthan" => ConditionOperator.GreaterThan,
            "lt" or "lessthan" => ConditionOperator.LessThan,
            "contains" => ConditionOperator.Contains,
            "beginswith" or "startswith" => ConditionOperator.BeginsWith,
            "endswith" => ConditionOperator.EndsWith,
            "null" or "isnull" => ConditionOperator.IsNull,
            "notnull" or "isnotnull" => ConditionOperator.IsNotNull,
            _ => ConditionOperator.Custom
        };
    }
}

