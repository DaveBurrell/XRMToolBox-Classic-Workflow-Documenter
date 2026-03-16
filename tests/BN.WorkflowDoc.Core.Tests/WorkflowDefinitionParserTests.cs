using BN.WorkflowDoc.Core.Domain;
using BN.WorkflowDoc.Core.Parsing;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class WorkflowDefinitionParserTests
{
    [Fact]
    public async Task ParseAsync_ReturnsWorkflowDefinitions_FromCustomizationsXml()
    {
        var extractPath = Path.Combine(Path.GetTempPath(), "BdWorkflowDocTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractPath);

        var xml = """
            <ImportExportXml>
              <Workflows>
                <Workflow WorkflowId="12345678-1234-1234-1234-1234567890ab" Name="Account Follow Up" PrimaryEntity="account" Mode="sync" Category="classic" Scope="organization" />
                <Workflow Name="Fallback Id Workflow" PrimaryEntity="contact" Mode="async" />
              </Workflows>
            </ImportExportXml>
            """;

        await File.WriteAllTextAsync(Path.Combine(extractPath, "customizations.xml"), xml);

        var package = new SolutionPackage(
            SourcePath: "in-memory.zip",
            ExtractedPath: extractPath,
            Version: "1.0.0.0",
            Workflows: Array.Empty<WorkflowDefinition>(),
            Warnings: Array.Empty<ProcessingWarning>());

        var parser = new WorkflowDefinitionParser();
        var result = await parser.ParseAsync(package);

        Assert.Equal(ProcessingStatus.PartialSuccess, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("Account Follow Up", result.Value[0].DisplayName);
        Assert.Equal(ExecutionMode.Synchronous, result.Value[0].ExecutionMode);
        Assert.Contains(result.Warnings, x => x.Code == "WORKFLOW_ID_INVALID");
    }

    [Fact]
    public async Task ParseAsync_ExtractsTriggerFieldsAndStageGraph()
    {
        var extractPath = Path.Combine(Path.GetTempPath(), "BdWorkflowDocTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractPath);

        var xml = """
            <ImportExportXml>
              <Workflows>
                <Workflow WorkflowId="22345678-1234-1234-1234-1234567890ab" Name="Opportunity Lifecycle" PrimaryEntity="opportunity" Mode="async" OnCreate="true" OnUpdate="true" OnDelete="false" AttributeFilter="name,estimatedvalue">
                  <Steps>
                    <Stage Name="Main Stage" />
                    <Condition Name="Value Check" />
                    <Action Name="Set Priority" Type="UpdateRecord" />
                    <Stop Name="Complete" />
                  </Steps>
                </Workflow>
              </Workflows>
            </ImportExportXml>
            """;

        await File.WriteAllTextAsync(Path.Combine(extractPath, "customizations.xml"), xml);

        var package = new SolutionPackage(
            SourcePath: "in-memory.zip",
            ExtractedPath: extractPath,
            Version: "1.0.0.0",
            Workflows: Array.Empty<WorkflowDefinition>(),
            Warnings: Array.Empty<ProcessingWarning>());

        var parser = new WorkflowDefinitionParser();
        var result = await parser.ParseAsync(package);

        Assert.Equal(ProcessingStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        var workflow = Assert.Single(result.Value!);

        Assert.True(workflow.Trigger.OnCreate);
        Assert.True(workflow.Trigger.OnUpdate);
        Assert.False(workflow.Trigger.OnDelete);
        Assert.Equal(2, workflow.Trigger.AttributeFilters.Count);
        Assert.Equal("name", workflow.Trigger.AttributeFilters[0]);

        Assert.Equal(5, workflow.StageGraph.Nodes.Count);
        Assert.Equal(4, workflow.StageGraph.Edges.Count);
        Assert.Equal(WorkflowComponentType.Trigger, workflow.StageGraph.Nodes[0].ComponentType);
        Assert.Equal(WorkflowComponentType.Condition, workflow.StageGraph.Nodes[2].ComponentType);
        Assert.Equal(WorkflowComponentType.Stop, workflow.StageGraph.Nodes[4].ComponentType);
    }

    [Fact]
    public async Task ParseAsync_ExtractsDependencies_FromChildWorkflowExternalCallAndActionReference()
    {
        var extractPath = Path.Combine(Path.GetTempPath(), "BdWorkflowDocTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractPath);

        var xml = """
            <ImportExportXml>
              <Workflows>
                <Workflow WorkflowId="42345678-1234-1234-1234-1234567890ab" Name="Dependency Workflow" PrimaryEntity="account" Mode="async">
                  <Steps>
                    <ChildWorkflow Name="Recalculate Score" WorkflowId="12340000-0000-0000-0000-000000000001" />
                    <ExternalCall Name="Notify ERP" ReferenceId="erp-operation" />
                    <Action Name="Set Parent" ReferenceEntity="contact" />
                  </Steps>
                </Workflow>
              </Workflows>
            </ImportExportXml>
            """;

        await File.WriteAllTextAsync(Path.Combine(extractPath, "customizations.xml"), xml);

        var package = new SolutionPackage(
            SourcePath: "in-memory.zip",
            ExtractedPath: extractPath,
            Version: "1.0.0.0",
            Workflows: Array.Empty<WorkflowDefinition>(),
            Warnings: Array.Empty<ProcessingWarning>());

        var parser = new WorkflowDefinitionParser();
        var result = await parser.ParseAsync(package);

        Assert.Equal(ProcessingStatus.PartialSuccess, result.Status);
        var workflow = Assert.Single(result.Value!);

        Assert.Equal(3, workflow.Dependencies.Count);
        Assert.Contains(workflow.Dependencies, x => x.DependencyType == "ChildWorkflow" && x.Name == "Recalculate Score");
        Assert.Contains(workflow.Dependencies, x => x.DependencyType == "ExternalCall" && x.Name == "Notify ERP");
        Assert.Contains(workflow.Dependencies, x => x.DependencyType == "Reference" && x.Name == "Set Parent:contact");
    }

    [Fact]
    public async Task ParseAsync_CreatesLabeledConditionEdges_ForTrueAndFalseBranches()
    {
        var extractPath = Path.Combine(Path.GetTempPath(), "BdWorkflowDocTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractPath);

        var xml = """
            <ImportExportXml>
              <Workflows>
                <Workflow WorkflowId="62345678-1234-1234-1234-1234567890ab" Name="Branch Workflow" PrimaryEntity="lead" Mode="sync">
                  <Steps>
                    <Condition Name="Is Qualified">
                      <True>
                        <Action Name="Create Opportunity" Type="CreateRecord" />
                      </True>
                      <False>
                        <Stop Name="Exit" />
                      </False>
                    </Condition>
                    <Action Name="Post Branch Action" Type="UpdateRecord" />
                  </Steps>
                </Workflow>
              </Workflows>
            </ImportExportXml>
            """;

        await File.WriteAllTextAsync(Path.Combine(extractPath, "customizations.xml"), xml);

        var package = new SolutionPackage(
            SourcePath: "in-memory.zip",
            ExtractedPath: extractPath,
            Version: "1.0.0.0",
            Workflows: Array.Empty<WorkflowDefinition>(),
            Warnings: Array.Empty<ProcessingWarning>());

        var parser = new WorkflowDefinitionParser();
        var result = await parser.ParseAsync(package);

        Assert.Equal(ProcessingStatus.Success, result.Status);
        var workflow = Assert.Single(result.Value!);

        Assert.Contains(workflow.StageGraph.Edges, x => x.ConditionLabel == "True");
        Assert.Contains(workflow.StageGraph.Edges, x => x.ConditionLabel == "False");
        Assert.Contains(workflow.StageGraph.Nodes, x => x.Label == "Merge");
        Assert.NotNull(workflow.RootCondition);
        Assert.Equal(ConditionOperator.Custom, workflow.RootCondition!.Operator);
        Assert.Equal("Is Qualified", workflow.RootCondition.Left);
    }

    [Fact]
    public async Task ParseAsync_ExtractsNestedRootConditionTree_FromCriteria()
    {
        var extractPath = Path.Combine(Path.GetTempPath(), "BdWorkflowDocTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractPath);

        var xml = """
            <ImportExportXml>
              <Workflows>
                <Workflow WorkflowId="82345678-1234-1234-1234-1234567890ab" Name="Condition Tree Workflow" PrimaryEntity="account" Mode="sync">
                  <Steps>
                    <Filter Operator="And">
                      <Condition Operator="Equals" Left="statuscode" Right="1" />
                      <Condition Operator="GreaterThan" Left="creditlimit" Right="10000" />
                    </Filter>
                    <Action Name="Approve" Type="UpdateRecord" />
                  </Steps>
                </Workflow>
              </Workflows>
            </ImportExportXml>
            """;

        await File.WriteAllTextAsync(Path.Combine(extractPath, "customizations.xml"), xml);

        var package = new SolutionPackage(
            SourcePath: "in-memory.zip",
            ExtractedPath: extractPath,
            Version: "1.0.0.0",
            Workflows: Array.Empty<WorkflowDefinition>(),
            Warnings: Array.Empty<ProcessingWarning>());

        var parser = new WorkflowDefinitionParser();
        var result = await parser.ParseAsync(package);

        Assert.Equal(ProcessingStatus.Success, result.Status);
        var workflow = Assert.Single(result.Value!);
        Assert.NotNull(workflow.RootCondition);

        var root = workflow.RootCondition!;
        Assert.Equal(ConditionOperator.And, root.Operator);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal(ConditionOperator.Equals, root.Children[0].Operator);
        Assert.Equal("statuscode", root.Children[0].Left);
        Assert.Equal("1", root.Children[0].Right);
        Assert.Equal(ConditionOperator.GreaterThan, root.Children[1].Operator);
        Assert.Equal("creditlimit", root.Children[1].Left);
        Assert.Equal("10000", root.Children[1].Right);
    }

    [Fact]
    public async Task ParseAsync_BlocksXamlPathTraversal_AndEmitsWarning()
    {
        var extractPath = Path.Combine(Path.GetTempPath(), "BdWorkflowDocTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractPath);

        // XamlFileName uses a relative traversal that bypasses the leading-slash TrimStart
        var xml = """
            <ImportExportXml>
              <Workflows>
                <Workflow WorkflowId="99345678-1234-1234-1234-1234567890ab"
                          Name="Traversal Workflow"
                          PrimaryEntity="account"
                          Mode="async"
                          XamlFileName="../../../Windows/System32/drivers/etc/hosts" />
              </Workflows>
            </ImportExportXml>
            """;

        await File.WriteAllTextAsync(Path.Combine(extractPath, "customizations.xml"), xml);

        var package = new SolutionPackage(
            SourcePath: "traversal.zip",
            ExtractedPath: extractPath,
            Version: "1.0.0.0",
            Workflows: Array.Empty<WorkflowDefinition>(),
            Warnings: Array.Empty<ProcessingWarning>());

        var parser = new WorkflowDefinitionParser();
        var result = await parser.ParseAsync(package);

        Assert.NotNull(result.Value);
        Assert.Contains(result.Warnings, x => x.Code == "XAML_PATH_TRAVERSAL_BLOCKED");
        // Workflow is still returned (non-blocking); graph is empty since XAML was skipped
        var workflow = Assert.Single(result.Value!);
        Assert.Equal("Traversal Workflow", workflow.DisplayName);
    }
}

