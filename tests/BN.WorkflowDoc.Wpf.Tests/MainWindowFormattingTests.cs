using BN.WorkflowDoc.Wpf;
using Xunit;

namespace BN.WorkflowDoc.Wpf.Tests;

public sealed class MainWindowFormattingTests
{
    [Fact]
    public void BuildWarningSummary_GroupsByCode()
    {
        var warnings = new[]
        {
            "[A] first",
            "[A] second",
            "[B] third"
        };

        var summary = MainWindowFormatting.BuildWarningSummary(warnings);

        Assert.Equal("[A] x2", summary[0]);
        Assert.Equal("[B] x1", summary[1]);
    }

    [Fact]
    public void BuildBatchSummaryLine_ContainsAllCoreFields()
    {
        var line = MainWindowFormatting.BuildBatchSummaryLine(
            status: "Success",
            workflowCount: 7,
            warningCount: 2,
            duration: TimeSpan.FromSeconds(4.2));

        Assert.Contains("Status: Success", line, StringComparison.Ordinal);
        Assert.Contains("Workflows: 7", line, StringComparison.Ordinal);
        Assert.Contains("Warnings: 2", line, StringComparison.Ordinal);
        Assert.Contains("Duration:", line, StringComparison.Ordinal);
    }
}
