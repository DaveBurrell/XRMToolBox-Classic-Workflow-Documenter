using System.Xml;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace BN.WorkflowDoc.XrmToolBox.Services;

internal interface IDataverseWorkflowProvider
{
    Task<ParseResult<IReadOnlyList<WorkflowCatalogItem>>> GetCatalogAsync(
        IOrganizationService service,
        CancellationToken cancellationToken = default);

    Task<ParseResult<IReadOnlyList<WorkflowDefinitionPayload>>> GetDefinitionsAsync(
        IOrganizationService service,
        IReadOnlyList<Guid> workflowIds,
        CancellationToken cancellationToken = default);
}

internal sealed class DataverseWorkflowProvider : IDataverseWorkflowProvider
{
    private static readonly int[] SupportedCategories = [0, 1, 3];

    public Task<ParseResult<IReadOnlyList<WorkflowCatalogItem>>> GetCatalogAsync(
        IOrganizationService service,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var warnings = new List<ProcessingWarning>();

            try
            {
                var rows = RetrieveAll(service, BuildCatalogQuery(), cancellationToken);
                var items = rows
                    .Select(entity => MapCatalogItem(entity))
                    .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new ParseResult<IReadOnlyList<WorkflowCatalogItem>>(ProcessingStatus.Success, items, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add(new ProcessingWarning(
                    "DATAVERSE_WORKFLOW_CATALOG_FAILED",
                    ex.Message,
                    "workflow",
                    true,
                    WarningCategory.Input,
                    WarningSeverity.Error));

                return new ParseResult<IReadOnlyList<WorkflowCatalogItem>>(ProcessingStatus.Failed, null, warnings, ex.Message);
            }
        }, cancellationToken);
    }

    public Task<ParseResult<IReadOnlyList<WorkflowDefinitionPayload>>> GetDefinitionsAsync(
        IOrganizationService service,
        IReadOnlyList<Guid> workflowIds,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var warnings = new List<ProcessingWarning>();

            if (workflowIds.Count == 0)
            {
                warnings.Add(new ProcessingWarning(
                    "DATAVERSE_WORKFLOW_SELECTION_EMPTY",
                    "No Dataverse workflows were selected for export.",
                    "workflow",
                    true,
                    WarningCategory.Input,
                    WarningSeverity.Error));

                return new ParseResult<IReadOnlyList<WorkflowDefinitionPayload>>(ProcessingStatus.Failed, null, warnings, "No workflows selected.");
            }

            try
            {
                var definitions = new List<WorkflowDefinitionPayload>(workflowIds.Count);
                foreach (var batch in Batch(workflowIds, 50))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rows = RetrieveAll(service, BuildDefinitionQuery(batch), cancellationToken);
                    definitions.AddRange(rows.Select(entity => MapDefinition(entity, warnings)));
                }

                var status = warnings.Any(warning => warning.IsBlocking)
                    ? ProcessingStatus.PartialSuccess
                    : ProcessingStatus.Success;

                return new ParseResult<IReadOnlyList<WorkflowDefinitionPayload>>(status, definitions, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add(new ProcessingWarning(
                    "DATAVERSE_WORKFLOW_LOAD_FAILED",
                    ex.Message,
                    "workflow",
                    true,
                    WarningCategory.Input,
                    WarningSeverity.Error));

                return new ParseResult<IReadOnlyList<WorkflowDefinitionPayload>>(ProcessingStatus.Failed, null, warnings, ex.Message);
            }
        }, cancellationToken);
    }

    private static QueryExpression BuildCatalogQuery()
    {
        return new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid", "name", "category", "primaryentity", "mode", "scope", "ownerid", "ondemand", "triggeroncreate", "triggerondelete", "triggeronupdateattributelist", "statecode"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("category", ConditionOperator.In, SupportedCategories.Cast<object>().ToArray())
                }
            },
            Orders =
            {
                new OrderExpression("name", OrderType.Ascending)
            },
            PageInfo = new PagingInfo
            {
                Count = 250,
                PageNumber = 1
            }
        };
    }

    private static QueryExpression BuildDefinitionQuery(IReadOnlyList<Guid> workflowIds)
    {
        return new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid", "name", "category", "primaryentity", "mode", "scope", "ownerid", "ondemand", "triggeroncreate", "triggerondelete", "triggeronupdateattributelist", "xaml", "uidata", "statecode"),
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("workflowid", ConditionOperator.In, workflowIds.Cast<object>().ToArray())
                }
            },
            PageInfo = new PagingInfo
            {
                Count = 250,
                PageNumber = 1
            }
        };
    }

    private static IReadOnlyList<Entity> RetrieveAll(IOrganizationService service, QueryExpression query, CancellationToken cancellationToken)
    {
        var results = new List<Entity>();
        EntityCollection page;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            page = service.RetrieveMultiple(query);
            results.AddRange(page.Entities);

            if (!page.MoreRecords)
            {
                break;
            }

            query.PageInfo.PageNumber += 1;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
        while (true);

        return results;
    }

    private static WorkflowCatalogItem MapCatalogItem(Entity entity)
    {
        var name = entity.GetAttributeValue<string>("name") ?? "Unnamed Workflow";
        var category = MapCategory(entity.GetAttributeValue<OptionSetValue>("category")?.Value);
        var primaryEntity = entity.GetAttributeValue<string>("primaryentity") ?? "unknown";
        var executionMode = MapExecutionMode(entity.GetAttributeValue<OptionSetValue>("mode")?.Value);
        var scope = MapScope(entity.GetAttributeValue<OptionSetValue>("scope")?.Value);
        var owner = entity.GetAttributeValue<EntityReference>("ownerid")?.Name;
        var state = MapState(entity.GetAttributeValue<OptionSetValue>("statecode")?.Value);
        var trigger = BuildTrigger(entity, primaryEntity);

        return new WorkflowCatalogItem(
            entity.Id,
            name,
            category,
            primaryEntity,
            executionMode,
            scope,
            owner,
            trigger.TriggerDescription ?? trigger.PrimaryEntity,
            state);
    }

    private static WorkflowDefinitionPayload MapDefinition(Entity entity, List<ProcessingWarning> warnings)
    {
        var name = entity.GetAttributeValue<string>("name") ?? "Unnamed Workflow";
        var primaryEntity = entity.GetAttributeValue<string>("primaryentity") ?? "unknown";
        var xaml = entity.GetAttributeValue<string>("xaml") ?? entity.GetAttributeValue<string>("uidata");
        var workflowWarnings = new List<ProcessingWarning>();
        var graph = BuildStageGraph(xaml, name, workflowWarnings);

        if (string.IsNullOrWhiteSpace(xaml))
        {
            workflowWarnings.Add(new ProcessingWarning(
                "DATAVERSE_WORKFLOW_DEFINITION_MISSING",
                $"Workflow '{name}' does not expose XAML or UI data in the current response. The generated document will rely on metadata only.",
                entity.Id.ToString(),
                false,
                WarningCategory.Parsing,
                WarningSeverity.Warning));
        }

        warnings.AddRange(workflowWarnings);

        return new WorkflowDefinitionPayload(
            WorkflowId: entity.Id,
            LogicalName: SanitizeLogicalName(name),
            DisplayName: name,
            Category: MapCategory(entity.GetAttributeValue<OptionSetValue>("category")?.Value),
            Scope: MapScope(entity.GetAttributeValue<OptionSetValue>("scope")?.Value),
            Owner: entity.GetAttributeValue<EntityReference>("ownerid")?.Name,
            IsOnDemand: entity.GetAttributeValue<bool?>("ondemand") ?? false,
            ExecutionMode: MapExecutionMode(entity.GetAttributeValue<OptionSetValue>("mode")?.Value),
            Trigger: BuildTrigger(entity, primaryEntity),
            StageGraph: graph,
            Dependencies: ParseDependencies(xaml),
            Warnings: workflowWarnings);
    }

    private static WorkflowTriggerPayload BuildTrigger(Entity entity, string primaryEntity)
    {
        var onCreate = entity.GetAttributeValue<bool?>("triggeroncreate") ?? false;
        var onDelete = entity.GetAttributeValue<bool?>("triggerondelete") ?? false;
        var updateAttributes = SplitList(entity.GetAttributeValue<string>("triggeronupdateattributelist"));
        var onUpdate = updateAttributes.Count > 0;

        var parts = new List<string>();
        if (onCreate)
        {
            parts.Add("create");
        }

        if (onUpdate)
        {
            parts.Add(updateAttributes.Count == 0 ? "update" : $"update ({string.Join(", ", updateAttributes)})");
        }

        if (onDelete)
        {
            parts.Add("delete");
        }

        return new WorkflowTriggerPayload(
            primaryEntity,
            onCreate,
            onUpdate,
            onDelete,
            updateAttributes,
            parts.Count == 0 ? $"{primaryEntity} manual or unsupported trigger metadata" : string.Join("; ", parts));
    }

    private static WorkflowStageGraphPayload BuildStageGraph(string? xaml, string workflowName, List<ProcessingWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(xaml))
        {
            return new WorkflowStageGraphPayload(Array.Empty<WorkflowNodePayload>(), Array.Empty<WorkflowEdgePayload>());
        }

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            using var reader = XmlReader.Create(new StringReader(xaml), settings);
            var document = XDocument.Load(reader, LoadOptions.None);

            var nodes = new List<WorkflowNodePayload>
            {
                new("trigger", WorkflowComponentType.Trigger, "Trigger", new Dictionary<string, string>())
            };
            var edges = new List<WorkflowEdgePayload>();
            var previousId = "trigger";
            var index = 0;

            foreach (var element in document.Descendants().Where(IsBusinessElement).Take(60))
            {
                var nodeId = $"live-{++index:D3}";
                var label = ReadLabel(element);
                var type = MapComponentType(element.Name.LocalName);
                var attributes = element.Attributes()
                    .GroupBy(attribute => attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

                nodes.Add(new WorkflowNodePayload(nodeId, type, label, attributes));
                edges.Add(new WorkflowEdgePayload(previousId, nodeId));
                previousId = nodeId;
            }

            if (index == 0)
            {
                warnings.Add(new ProcessingWarning(
                    "DATAVERSE_WORKFLOW_GRAPH_EMPTY",
                    $"Workflow '{workflowName}' did not expose recognizable XAML activities. The document will contain metadata but no detailed step graph.",
                    workflowName,
                    false,
                    WarningCategory.Parsing,
                    WarningSeverity.Warning));
            }

            return new WorkflowStageGraphPayload(nodes, edges);
        }
        catch (Exception ex)
        {
            warnings.Add(new ProcessingWarning(
                "DATAVERSE_WORKFLOW_GRAPH_PARSE_FAILED",
                $"Workflow '{workflowName}' XAML could not be parsed: {ex.Message}",
                workflowName,
                false,
                WarningCategory.Parsing,
                WarningSeverity.Warning));

            return new WorkflowStageGraphPayload(Array.Empty<WorkflowNodePayload>(), Array.Empty<WorkflowEdgePayload>());
        }
    }

    private static IReadOnlyList<WorkflowDependencyPayload> ParseDependencies(string? xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml))
        {
            return Array.Empty<WorkflowDependencyPayload>();
        }

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            using var reader = XmlReader.Create(new StringReader(xaml), settings);
            var document = XDocument.Load(reader, LoadOptions.None);

            return document.Descendants()
                .Select(element => element.Attribute("DisplayName")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(value => value!.IndexOf("child", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("workflow", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0
                    || value.IndexOf("service", StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(10)
                .Select(value => new WorkflowDependencyPayload("ReferencedActivity", value!, null))
                .ToArray();
        }
        catch
        {
            return Array.Empty<WorkflowDependencyPayload>();
        }
    }

    private static bool IsBusinessElement(XElement element)
    {
        var name = element.Name.LocalName;
        if (string.Equals(name, "Sequence", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Statements", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Flowchart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Stage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Workflow", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return element.Attribute("DisplayName") is not null
            || string.Equals(name, "If", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "FlowDecision", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Assign", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Persist", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "SetState", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "CreateEntity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "UpdateEntity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "DeleteEntity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "SendEmail", StringComparison.OrdinalIgnoreCase);
    }

    private static WorkflowComponentType MapComponentType(string localName)
    {
        if (string.Equals(localName, "If", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "FlowDecision", StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowComponentType.Condition;
        }

        if (localName.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkflowComponentType.Stop;
        }

        if (localName.IndexOf("child", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkflowComponentType.ChildWorkflow;
        }

        if (localName.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0
            || localName.IndexOf("service", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkflowComponentType.ExternalCall;
        }

        return WorkflowComponentType.Action;
    }

    private static string ReadLabel(XElement element)
    {
        return element.Attribute("DisplayName")?.Value
            ?? element.Attribute("Name")?.Value
            ?? element.Name.LocalName;
    }

    private static IReadOnlyList<string> SplitList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        var value = raw!;
        return value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<IReadOnlyList<Guid>> Batch(IReadOnlyList<Guid> ids, int batchSize)
    {
        for (var i = 0; i < ids.Count; i += batchSize)
        {
            yield return ids.Skip(i).Take(batchSize).ToArray();
        }
    }

    private static string MapCategory(int? category)
    {
        return category switch
        {
            0 => "Workflow",
            1 => "Dialog",
            3 => "Action",
            int value => $"Category {value}",
            _ => "Unknown"
        };
    }

    private static string MapScope(int? scope)
    {
        return scope switch
        {
            1 => "User",
            2 => "Business Unit",
            3 => "Parent Child Business Unit",
            4 => "Organization",
            int value => $"Scope {value}",
            _ => "Unknown"
        };
    }

    private static string MapState(int? state)
    {
        return state switch
        {
            0 => "Draft",
            1 => "Activated",
            int value => $"State {value}",
            _ => "Unknown"
        };
    }

    private static string MapExecutionMode(int? mode)
    {
        return mode == 1 ? "Synchronous" : "Asynchronous";
    }

    private static string SanitizeLogicalName(string name)
    {
        var chars = name.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? "Workflow" : new string(chars);
    }
}
