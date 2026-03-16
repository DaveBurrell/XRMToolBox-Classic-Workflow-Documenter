using System.IO.Compression;
using BN.WorkflowDoc.Core.Domain;
using BN.WorkflowDoc.Core.Parsing;
using Xunit;

namespace BN.WorkflowDoc.Core.Tests;

public sealed class SolutionPackageReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsPartialSuccess_WhenCustomizationsIsMissing()
    {
        var zipPath = await CreateZipAsync(
            ("solution.xml", "<ImportExportXml><SolutionManifest><Version>1.2.3.4</Version></SolutionManifest></ImportExportXml>"));

        var reader = new SolutionPackageReader();
        var result = await reader.ReadAsync(zipPath);

        Assert.Equal(ProcessingStatus.PartialSuccess, result.Status);
        Assert.NotNull(result.Value);
        Assert.Contains(result.Warnings, x => x.Code == "MISSING_CUSTOMIZATIONS_XML");
        Assert.Equal("1.2.3.4", result.Value!.Version);
    }

    [Fact]
    public async Task ReadAsync_ReturnsSuccess_WhenRequiredEntriesExist()
    {
        var zipPath = await CreateZipAsync(
            ("solution.xml", "<ImportExportXml><SolutionManifest><Version>9.9.9.9</Version></SolutionManifest></ImportExportXml>"),
            ("customizations.xml", "<ImportExportXml><Workflows /></ImportExportXml>"));

        var reader = new SolutionPackageReader();
        var result = await reader.ReadAsync(zipPath);

        Assert.Equal(ProcessingStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("9.9.9.9", result.Value!.Version);
        Assert.DoesNotContain(result.Warnings, x => x.IsBlocking);
    }

    [Fact]
    public async Task ReadAsync_BlocksZipSlipEntry_AndEmitsWarning()
    {
        var root = Path.Combine(Path.GetTempPath(), "BdWorkflowDocTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "malicious.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var normal = archive.CreateEntry("customizations.xml");
            using (var w = new StreamWriter(normal.Open()))
                w.Write("<ImportExportXml><Workflows /></ImportExportXml>");

            // Create a traversal entry by writing raw zip bytes — ZipArchive.CreateEntry
            // allows arbitrary FullName values including "../" sequences.
            var evil = archive.CreateEntry("../../evil-zipslip-test.txt");
            using (var w = new StreamWriter(evil.Open()))
                w.Write("pwned");
        }

        var reader = new SolutionPackageReader();
        var result = await reader.ReadAsync(zipPath);

        Assert.Contains(result.Warnings, x => x.Code == "ZIP_SLIP_BLOCKED");

        // Confirm the traversal file was NOT written outside the temp root
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var evilPath = Path.GetFullPath(Path.Combine(tempRoot, "evil-zipslip-test.txt"));
        Assert.False(File.Exists(evilPath));
    }

    [Fact]
    public async Task ReadAsync_AcceptsLegitimateNestedEntries_WithoutWarning()
    {
        var zipPath = await CreateZipAsync(
            ("customizations.xml", "<ImportExportXml><Workflows /></ImportExportXml>"),
            ("Workflows/workflow1.xaml", "<Activity />"));

        var reader = new SolutionPackageReader();
        var result = await reader.ReadAsync(zipPath);

        Assert.DoesNotContain(result.Warnings, x => x.Code == "ZIP_SLIP_BLOCKED");
    }

    private static Task<string> CreateZipAsync(params (string Name, string Content)[] entries)
    {
        var root = Path.Combine(Path.GetTempPath(), "BdWorkflowDocTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var zipPath = Path.Combine(root, "sample-solution.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Name);
                using var writer = new StreamWriter(zipEntry.Open());
                writer.Write(entry.Content);
            }
        }

        return Task.FromResult(zipPath);
    }
}

