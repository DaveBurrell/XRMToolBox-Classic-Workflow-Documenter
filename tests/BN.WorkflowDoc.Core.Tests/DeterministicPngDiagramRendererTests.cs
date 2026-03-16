using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class DeterministicPngDiagramRendererTests
{
    [Fact]
    public async Task RenderAsync_ReturnsPngAssets()
    {
        var diagrams = new[]
        {
            new DiagramGraph(
                DiagramType.Flowchart,
                new[]
                {
                    new DiagramNode("trigger", "Trigger", "Flow", 220, 70),
                    new DiagramNode("n1", "Action", "Flow", 220, 70)
                },
                new[] { new DiagramEdge("trigger", "n1", null) },
                "Flowchart caption")
        };

        var renderer = new DeterministicPngDiagramRenderer();
        var result = await renderer.RenderAsync(diagrams);

        Assert.Equal(ProcessingStatus.Success, result.Status);
        var asset = Assert.Single(result.Value!);
        Assert.Equal("image/png", asset.ContentType);
        Assert.True(asset.Content.Length > 8);

        // PNG signature bytes: 89 50 4E 47 0D 0A 1A 0A
        Assert.Equal(0x89, asset.Content[0]);
        Assert.Equal(0x50, asset.Content[1]);
        Assert.Equal(0x4E, asset.Content[2]);
        Assert.Equal(0x47, asset.Content[3]);
    }
}

