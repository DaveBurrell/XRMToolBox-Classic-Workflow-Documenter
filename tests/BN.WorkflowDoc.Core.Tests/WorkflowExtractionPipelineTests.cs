using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class WorkflowExtractionPipelineTests
{
    [Fact]
    public async Task ExtractAsync_ReturnsSuccess_WhenReaderAndParserSucceed()
    {
        var readerResult = new ParseResult<SolutionPackage>(
            ProcessingStatus.Success,
            new SolutionPackage("a.zip", "c:\\tmp", "1.0", Array.Empty<WorkflowDefinition>(), Array.Empty<ProcessingWarning>()),
            Array.Empty<ProcessingWarning>());

        var parserResult = new ParseResult<IReadOnlyList<WorkflowDefinition>>(
            ProcessingStatus.Success,
            new[]
            {
                new WorkflowDefinition(
                    Guid.NewGuid(),
                    "AccountFollowUp",
                    "Account Follow Up",
                    "classic",
                    "organization",
                    null,
                    ExecutionMode.Asynchronous,
                    new WorkflowTrigger("account", false, true, false, Array.Empty<string>(), null),
                    WorkflowStageGraph.Empty,
                    null,
                    Array.Empty<WorkflowDependency>(),
                    Array.Empty<ProcessingWarning>())
            },
            Array.Empty<ProcessingWarning>());

        var pipeline = new WorkflowExtractionPipeline(new FakeReader(readerResult), new FakeParser(parserResult));
        var result = await pipeline.ExtractAsync("a.zip");

        Assert.Equal(ProcessingStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value!.Workflows);
    }

    [Fact]
    public async Task ExtractAsync_AggregatesWarningsAndPartialStatus()
    {
        var readWarnings = new[] { new ProcessingWarning("READ_WARN", "Read warning", "solution.zip") };
        var parseWarnings = new[] { new ProcessingWarning("PARSE_WARN", "Parse warning", "customizations.xml") };

        var readerResult = new ParseResult<SolutionPackage>(
            ProcessingStatus.PartialSuccess,
            new SolutionPackage("a.zip", "c:\\tmp", "1.0", Array.Empty<WorkflowDefinition>(), readWarnings),
            readWarnings);

        var parserResult = new ParseResult<IReadOnlyList<WorkflowDefinition>>(
            ProcessingStatus.Success,
            Array.Empty<WorkflowDefinition>(),
            parseWarnings);

        var pipeline = new WorkflowExtractionPipeline(new FakeReader(readerResult), new FakeParser(parserResult));
        var result = await pipeline.ExtractAsync("a.zip");

        Assert.Equal(ProcessingStatus.PartialSuccess, result.Status);
        Assert.Equal(2, result.Warnings.Count);
    }

    private sealed class FakeReader : ISolutionPackageReader
    {
        private readonly ParseResult<SolutionPackage> _result;

        public FakeReader(ParseResult<SolutionPackage> result) => _result = result;

        public Task<ParseResult<SolutionPackage>> ReadAsync(string solutionZipPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class FakeParser : IWorkflowDefinitionParser
    {
        private readonly ParseResult<IReadOnlyList<WorkflowDefinition>> _result;

        public FakeParser(ParseResult<IReadOnlyList<WorkflowDefinition>> result) => _result = result;

        public Task<ParseResult<IReadOnlyList<WorkflowDefinition>>> ParseAsync(
            SolutionPackage package,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }
}

