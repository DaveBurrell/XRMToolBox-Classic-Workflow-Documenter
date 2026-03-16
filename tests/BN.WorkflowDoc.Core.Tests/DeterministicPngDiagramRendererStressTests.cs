using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class DeterministicPngDiagramRendererStressTests
{
    [Fact]
    public async Task RenderAsync_LargeDiagram_CompletesAndReturnsAsset()
    {
        var nodes = Enumerable.Range(1, 140)
            .Select(i => new DiagramNode(
                Id: $"n{i}",
                Label: $"Node {i}",
                Lane: i <= 35 ? "Trigger" : i <= 70 ? "Logic" : i <= 105 ? "Actions" : "Integrations",
                Width: 180,
                Height: 64))
            .ToArray();

        var edges = Enumerable.Range(1, 139)
            .Select(i => new DiagramEdge($"n{i}", $"n{i + 1}", i % 7 == 0 ? "branch" : null))
            .ToArray();

        var diagram = new DiagramGraph(
            Type: DiagramType.Flowchart,
            Nodes: nodes,
            Edges: edges,
            Caption: "Stress diagram");

        var renderer = new DeterministicPngDiagramRenderer();
        var started = DateTime.UtcNow;
        var result = await renderer.RenderAsync([diagram]);
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(ProcessingStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value!);
        Assert.All(result.Value!, asset => Assert.True(asset.Content.Length > 0));
        Assert.True(elapsed < TimeSpan.FromSeconds(10));
    }
}
