using System.Text;
using System.Text.RegularExpressions;
using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class DeterministicSvgDiagramRendererTests
{
    [Fact]
    public async Task RenderAsync_ReturnsSvgAssets_ForFlowchartAndSwimlane()
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
                "Flowchart caption"),
            new DiagramGraph(
                DiagramType.Swimlane,
                new[]
                {
                    new DiagramNode("trigger", "Trigger", "Trigger Context", 220, 70),
                    new DiagramNode("n1", "Condition", "Decision Logic", 220, 70)
                },
                new[] { new DiagramEdge("trigger", "n1", "True") },
                "Swimlane caption")
        };

        var renderer = new DeterministicSvgDiagramRenderer();
        var result = await renderer.RenderAsync(diagrams);

        Assert.Equal(ProcessingStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value!.Count);

        foreach (var asset in result.Value)
        {
            Assert.Equal("image/svg+xml", asset.ContentType);
            var svg = Encoding.UTF8.GetString(asset.Content);
            Assert.Contains("<svg", svg);
            Assert.Contains("<rect", svg);
            Assert.Contains("Colour-coded by workflow action type", svg);
            Assert.Contains("Trigger", svg);
        }
    }

    [Fact]
    public async Task RenderAsync_UsesVerticalFlowLayout_ForSingleLaneFlowchart()
    {
        var diagram = new DiagramGraph(
            DiagramType.Flowchart,
            new[]
            {
                new DiagramNode("trigger", "Trigger", "Flow", 220, 70),
                new DiagramNode("action", "Action", "Flow", 220, 70)
            },
            new[] { new DiagramEdge("trigger", "action", null) },
            "Vertical flowchart caption");

        var renderer = new DeterministicSvgDiagramRenderer();
        var result = await renderer.RenderAsync([diagram]);
        var asset = Assert.Single(result.Value!);
        var svg = Encoding.UTF8.GetString(asset.Content);

        var rectMatches = Regex.Matches(svg, "<rect x='(?<x>\\d+)' y='(?<y>\\d+)' rx='10' ry='10' width='(?<w>\\d+)' height='(?<h>\\d+)' fill='#[0-9a-f]{6}' stroke='#[0-9a-f]{6}' stroke-width='2' filter='url\\(#softShadow\\)' />", RegexOptions.IgnoreCase);
        Assert.True(rectMatches.Count >= 2);

        var firstX = int.Parse(rectMatches[0].Groups["x"].Value);
        var firstY = int.Parse(rectMatches[0].Groups["y"].Value);
        var secondX = int.Parse(rectMatches[1].Groups["x"].Value);
        var secondY = int.Parse(rectMatches[1].Groups["y"].Value);

        Assert.Equal(firstX, secondX);
        Assert.True(secondY > firstY);

        var lineMatch = Regex.Match(svg, "<line x1='(?<x1>\\d+)' y1='(?<y1>\\d+)' x2='(?<x2>\\d+)' y2='(?<y2>\\d+)' stroke='#4a5668' stroke-width='2' marker-end='url\\(#arrow\\)' />");
        Assert.True(lineMatch.Success);
        Assert.Equal(lineMatch.Groups["x1"].Value, lineMatch.Groups["x2"].Value);
        Assert.True(int.Parse(lineMatch.Groups["y2"].Value) > int.Parse(lineMatch.Groups["y1"].Value));
    }

    [Fact]
    public async Task RenderAsync_IncludesComponentBadgesLegendAndDetailLines()
    {
        var diagram = new DiagramGraph(
            DiagramType.Swimlane,
            new[]
            {
                new DiagramNode(
                    "trigger",
                    "Account Update Trigger",
                    "Trigger Context",
                    240,
                    122,
                    WorkflowComponentType.Trigger,
                    ["Starts when the workflow trigger fires", "Entity: account", "Event: update"]),
                new DiagramNode(
                    "call",
                    "Invoke ERP Sync",
                    "External Calls",
                    240,
                    122,
                    WorkflowComponentType.ExternalCall,
                    ["Calls an external system or integration", "Endpoint: ERP API"])
                ,new DiagramNode(
                    "decision",
                    "Evaluate credit policy",
                    "Decision Logic",
                    240,
                    132,
                    WorkflowComponentType.Condition,
                    ["Rule: CreditScore > 700", "Field: credit_score"])
                ,new DiagramNode(
                    "stop",
                    "Stop process",
                    "Terminal States",
                    220,
                    110,
                    WorkflowComponentType.Stop,
                    ["Reason: Credit policy failed"])
            },
            new[]
            {
                new DiagramEdge("trigger", "decision", null),
                new DiagramEdge("decision", "call", "Yes"),
                new DiagramEdge("decision", "stop", "No")
            },
            "Rich visual test");

        var renderer = new DeterministicSvgDiagramRenderer();
        var result = await renderer.RenderAsync([diagram]);
        var svg = Encoding.UTF8.GetString(Assert.Single(result.Value!).Content);

        Assert.Contains("TRIGGER", svg);
        Assert.Contains("EXTERNAL", svg);
        Assert.Contains("DECISION", svg);
        Assert.Contains("STOP", svg);
        Assert.Contains("Entity: account", svg);
        Assert.Contains("Endpoint: ERP API", svg);
        Assert.Contains("Child Workflow", svg);
        Assert.Contains("Yes", svg);
        Assert.Contains("No", svg);
        Assert.Contains("<polygon points='", svg);
        Assert.Contains("rx='55' ry='55'", svg);
        Assert.Contains(">?<", svg);
        Assert.Contains(">X<", svg);
    }

    [Fact]
    public async Task RenderAsync_SplitsLargeDiagramIntoMultipleAssets()
    {
        var nodes = Enumerable.Range(1, 52)
            .Select(i => new DiagramNode($"n{i}", $"Step {i}", "Flow", 220, 70))
            .ToArray();
        var edges = Enumerable.Range(1, 51)
            .Select(i => new DiagramEdge($"n{i}", $"n{i + 1}", null))
            .ToArray();

        var renderer = new DeterministicSvgDiagramRenderer();
        var result = await renderer.RenderAsync([
            new DiagramGraph(DiagramType.Flowchart, nodes, edges, "Large diagram")
        ]);

        Assert.True(result.Value!.Count > 1);
        Assert.All(result.Value!, asset => Assert.Contains("flowchart", asset.FileName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ProcessingStatus.Success, result.Status);
    }

    [Fact]
    public async Task RenderAsync_PrefersLogicalBoundary_WhenSplittingLargeDiagram()
    {
        var nodes = Enumerable.Range(1, 30)
            .Select(i =>
            {
                if (i == 20)
                {
                    return new DiagramNode($"n{i}", "Decision Gate", "Decision Logic", 220, 70);
                }

                if (i == 24)
                {
                    return new DiagramNode($"n{i}", "Merge", "Decision Logic", 220, 70);
                }

                return new DiagramNode($"n{i}", $"Step {i}", "Flow", 220, 70);
            })
            .ToArray();

        var edges = new List<DiagramEdge>();
        for (var i = 1; i < 20; i++)
        {
            edges.Add(new DiagramEdge($"n{i}", $"n{i + 1}", null));
        }

        edges.Add(new DiagramEdge("n20", "n21", "Yes"));
        edges.Add(new DiagramEdge("n20", "n22", "No"));
        edges.Add(new DiagramEdge("n21", "n23", null));
        edges.Add(new DiagramEdge("n22", "n24", null));
        edges.Add(new DiagramEdge("n23", "n24", null));
        for (var i = 24; i < 30; i++)
        {
            edges.Add(new DiagramEdge($"n{i}", $"n{i + 1}", null));
        }

        var renderer = new DeterministicSvgDiagramRenderer();
        var result = await renderer.RenderAsync([
            new DiagramGraph(DiagramType.Flowchart, nodes, edges, "Boundary-aware split")
        ]);

        Assert.True(result.Value!.Count >= 2);
        var firstSvg = Encoding.UTF8.GetString(result.Value[0].Content);
        Assert.Contains("Decision Gate", firstSvg);
        Assert.DoesNotContain("Step 25", firstSvg);
    }

    [Fact]
    public async Task RenderAsync_UsesBusinessFriendlySplitTitles_ForTechnicalLabels()
    {
        var nodes = Enumerable.Range(1, 30)
            .Select(i =>
            {
                if (i == 18)
                {
                    return new DiagramNode($"n{i}", "[Condition] ConditionStep76: PM Check Duplicate", "Decision Logic", 220, 70);
                }

                return new DiagramNode($"n{i}", $"CreateStep{i}: Create Follow for Contact", "Flow", 220, 70);
            })
            .ToArray();
        var edges = Enumerable.Range(1, 29)
            .Select(i => new DiagramEdge($"n{i}", $"n{i + 1}", null))
            .ToArray();

        var renderer = new DeterministicSvgDiagramRenderer();
        var result = await renderer.RenderAsync([
            new DiagramGraph(DiagramType.Flowchart, nodes, edges, "Friendly title test")
        ]);

        Assert.True(result.Value!.Count >= 2);
        Assert.Contains("Duplicate Check and Routing", result.Value[0].Caption);
    }

    [Fact]
    public async Task RenderAsync_StandardDetailMode_ShowsCondensedNodeDetails()
    {
        var diagram = new DiagramGraph(
            DiagramType.Flowchart,
            new[]
            {
                new DiagramNode(
                    "n1",
                    "Update account",
                    "Actions",
                    240,
                    122,
                    WorkflowComponentType.Action,
                    ["Performs a Dataverse workflow action", "Entity: account", "Field: credit_limit"])
            },
            Array.Empty<DiagramEdge>(),
            "Standard detail mode");

        var renderer = new DeterministicSvgDiagramRenderer(DiagramDetailLevel.Standard);
        var result = await renderer.RenderAsync([diagram]);
        var svg = Encoding.UTF8.GetString(Assert.Single(result.Value!).Content);

        Assert.Contains("Performs", svg);
        Assert.DoesNotContain("Entity: account", svg);
        Assert.DoesNotContain("Field: credit_limit", svg);
    }
}

