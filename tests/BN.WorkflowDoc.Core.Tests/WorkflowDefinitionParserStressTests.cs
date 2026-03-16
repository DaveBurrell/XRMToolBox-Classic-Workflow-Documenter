using BN.WorkflowDoc.Core.Parsing;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class WorkflowDefinitionParserStressTests
{
    [Fact]
    public async Task ParseAsync_LargeCustomizationsFile_ParsesAllWorkflows()
    {
        var root = Path.Combine(Path.GetTempPath(), "BNWorkflowDocStress", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var workflowCount = 250;
        var customizationsPath = Path.Combine(root, "customizations.xml");
        await File.WriteAllTextAsync(customizationsPath, BuildCustomizationsXml(workflowCount));

        var package = new SolutionPackage(
            SourcePath: "stress.zip",
            ExtractedPath: root,
            Version: "1.0.0.0",
            Workflows: Array.Empty<WorkflowDefinition>(),
            Warnings: Array.Empty<ProcessingWarning>());

        var sut = new WorkflowDefinitionParser();
        var result = await sut.ParseAsync(package);

        Assert.Equal(ProcessingStatus.Success, result.Status);
        var workflows = Assert.IsAssignableFrom<IReadOnlyList<WorkflowDefinition>>(result.Value);
        Assert.Equal(workflowCount, workflows.Count);
        Assert.All(workflows, wf => Assert.NotEmpty(wf.DisplayName));
    }

    private static string BuildCustomizationsXml(int workflowCount)
    {
        var workflows = Enumerable.Range(1, workflowCount)
            .Select(i =>
                $"<Workflow Id=\"{Guid.NewGuid()}\" Name=\"Stress Workflow {i}\" PrimaryEntity=\"account\" Mode=\"0\">" +
                "<Process><Step Name=\"Do Thing\" /><Action Name=\"Notify\" ActionType=\"SendEmail\" /></Process>" +
                "</Workflow>");

        return "<ImportExportXml><Workflows>" + string.Concat(workflows) + "</Workflows></ImportExportXml>";
    }
}
