using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using BN.WorkflowDoc.Cli;
using BN.WorkflowDoc.Core.Application;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;
using BN.WorkflowDoc.Core.Parsing;

if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  BN.WorkflowDoc.Cli <path-to-unmanaged-solution.zip>");
    Console.Error.WriteLine("  BN.WorkflowDoc.Cli extract <path-to-unmanaged-solution.zip>");
    Console.Error.WriteLine("  BN.WorkflowDoc.Cli document <path-to-unmanaged-solution.zip> [output-folder] [--diagram-detail standard|detailed]");
    Console.Error.WriteLine("  BN.WorkflowDoc.Cli docx <path-to-unmanaged-solution.zip> [output-folder] [--diagram-detail standard|detailed]");
    Console.Error.WriteLine("  BN.WorkflowDoc.Cli pack <path-to-unmanaged-solution.zip> [output-folder] [--diagram-detail standard|detailed]");
    Console.Error.WriteLine("  BN.WorkflowDoc.Cli batch <zip|folder|glob> [output-folder] [--diagram-detail standard|detailed]");
    Console.Error.WriteLine("  BN.WorkflowDoc.Cli docx-live <live-request.json> [output-folder] [--diagram-detail standard|detailed] [--narrative-tone business|technical] [--output-mode per-workflow|single]");
    return 2;
}

var (command, inputSpec, outputFolder, diagramDetailLevel, narrativeTone, outputMode) = ResolveCommand(args);

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() }
};

if (string.Equals(command, "docx-live", StringComparison.OrdinalIgnoreCase))
{
    if (!File.Exists(inputSpec))
    {
        Console.Error.WriteLine($"Live request JSON not found: {inputSpec}");
        return 2;
    }

    var requestJson = await File.ReadAllTextAsync(inputSpec);
    var liveRequest = JsonSerializer.Deserialize<LiveWorkflowDocumentationRequest>(requestJson, options);
    if (liveRequest is null)
    {
        Console.Error.WriteLine($"Live request JSON could not be parsed: {inputSpec}");
        return 2;
    }

    var workflowBuilder = new DeterministicWorkflowDocumentBuilder();
    var overviewBuilder = new DeterministicOverviewDocumentBuilder();
    var documentationPipeline = new WorkflowDocumentationPipeline(
        new WorkflowExtractionPipeline(new SolutionPackageReader(), new WorkflowDefinitionParser()),
        workflowBuilder,
        overviewBuilder);

    var request = LiveWorkflowTransportMapper.ToRequest(liveRequest);
    var docResult = await documentationPipeline.GenerateAsync(request);
    var resolvedOutputFolder = string.IsNullOrWhiteSpace(outputFolder)
        ? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", ArtifactPathNaming.SanitizeFileName(request.SourceName, "live-selection"))
        : Path.GetFullPath(outputFolder);
    Directory.CreateDirectory(resolvedOutputFolder);

    var docxWriter = new OpenXmlDocxArtifactWriter(new DeterministicPngDiagramRenderer(diagramDetailLevel), narrativeTone);
    var isSingleOutput = string.Equals(outputMode, "single", StringComparison.OrdinalIgnoreCase);
    var writeOptions = new DocxArtifactWriteOptions(
        IncludePerWorkflowDocuments: !isSingleOutput,
        IncludeOverviewDocument: !isSingleOutput,
        IncludeCombinedFullDetailDocument: isSingleOutput);
    var docxResult = docResult.Value is null
        ? new ParseResult<DocxArtifactResult>(ProcessingStatus.Failed, null, docResult.Warnings, docResult.ErrorMessage ?? "Documentation model generation failed.")
        : await docxWriter.WriteAsync(docResult.Value, resolvedOutputFolder, writeOptions);

    var manifestPath = Path.Combine(resolvedOutputFolder, "bundle-manifest.json");
    if (docxResult.Value is not null)
    {
        var workflowEntries = docResult.Value.WorkflowDocuments.Select((workflow, index) => new WorkflowArtifactManifestEntry(
            WorkflowName: workflow.WorkflowName,
            PrimaryDocumentFile: index < docxResult.Value.WorkflowFiles.Count ? docxResult.Value.WorkflowFiles[index] : string.Empty,
            DiagramFiles: Array.Empty<string>())).ToArray();

        var bundleManifest = new ArtifactBundleManifest(
            Mode: "docx-live",
            Status: docxResult.Status.ToString(),
            Input: inputSpec,
            OutputFolder: resolvedOutputFolder,
            OverviewFile: docxResult.Value.OverviewFile,
            Workflows: workflowEntries,
            DiagramAssets: Array.Empty<DiagramAssetManifestEntry>(),
            Warnings: docxResult.Warnings.Select(ToManifestWarning).ToArray(),
            Error: docxResult.ErrorMessage);

        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(bundleManifest, options));
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        status = docxResult.Status.ToString(),
        input = inputSpec,
        outputFolder = resolvedOutputFolder,
        workflowDocxFiles = docxResult.Value?.WorkflowFiles ?? Array.Empty<string>(),
        overviewDocxFile = docxResult.Value?.OverviewFile,
        combinedFullDetailDocxFile = docxResult.Value?.CombinedFullDetailFile,
        manifestFile = File.Exists(manifestPath) ? manifestPath : null,
        warnings = docxResult.Warnings.Select(ToManifestWarning),
        error = docxResult.ErrorMessage
    }, options));

    return docxResult.Status == ProcessingStatus.Failed ? 1 : 0;
}

var reader = new SolutionPackageReader();
var parser = new WorkflowDefinitionParser();

if (string.Equals(command, "batch", StringComparison.OrdinalIgnoreCase))
{
    var inputs = ResolveBatchInputs(inputSpec);
    if (inputs.Count == 0)
    {
        Console.Error.WriteLine($"No ZIP inputs matched: {inputSpec}");
        return 2;
    }

    var extractionPipeline = new WorkflowExtractionPipeline(reader, parser);
    var workflowBuilder = new DeterministicWorkflowDocumentBuilder();
    var overviewBuilder = new DeterministicOverviewDocumentBuilder();
    var documentationPipeline = new WorkflowDocumentationPipeline(extractionPipeline, workflowBuilder, overviewBuilder);
    var portfolioPipeline = new PortfolioDocumentationPipeline(documentationPipeline);
    var batchResult = await portfolioPipeline.GenerateAsync(inputs);

    var resolvedOutputFolder = ResolveBatchOutputFolder(outputFolder);
    Directory.CreateDirectory(resolvedOutputFolder);

    var docxWriter = new OpenXmlDocxArtifactWriter(new DeterministicPngDiagramRenderer(diagramDetailLevel));
    var solutionArtifacts = new List<BatchSolutionArtifactEntry>(batchResult.Value?.Results.Count ?? 0);
    var writingWarnings = new List<ProcessingWarning>(batchResult.Warnings);

    if (batchResult.Value is not null)
    {
        for (var i = 0; i < batchResult.Value.Results.Count; i++)
        {
            var item = batchResult.Value.Results[i];
            if (item.WorkflowDocuments is null || item.OverviewDocument is null)
            {
                solutionArtifacts.Add(new BatchSolutionArtifactEntry(
                    InputPath: item.InputPath,
                    Status: item.Status.ToString(),
                    OutputFolder: null,
                    OverviewFile: null,
                    WorkflowFiles: Array.Empty<string>()));
                continue;
            }

            var solutionName = item.OverviewDocument.SolutionName;
            var solutionFolderName = ArtifactPathNaming.BuildSolutionFolderName(i + 1, solutionName);
            var solutionOutputFolder = Path.Combine(resolvedOutputFolder, solutionFolderName);
            Directory.CreateDirectory(solutionOutputFolder);

            var generationResult = new DocumentationGenerationResult(
                Package: new SolutionPackage(item.InputPath, solutionOutputFolder, "unknown", Array.Empty<WorkflowDefinition>(), item.Warnings),
                WorkflowDocuments: item.WorkflowDocuments,
                OverviewDocument: item.OverviewDocument,
                Warnings: item.Warnings);

            var writeResult = await docxWriter.WriteAsync(generationResult, solutionOutputFolder);
            writingWarnings.AddRange(writeResult.Warnings);

            solutionArtifacts.Add(new BatchSolutionArtifactEntry(
                InputPath: item.InputPath,
                Status: writeResult.Status.ToString(),
                OutputFolder: solutionOutputFolder,
                OverviewFile: writeResult.Value?.OverviewFile,
                WorkflowFiles: writeResult.Value?.WorkflowFiles ?? Array.Empty<string>()));
        }

        var portfolioOverviewModel = new OverviewDocumentModel(
            SolutionName: "Portfolio Overview",
            Workflows: batchResult.Value.PortfolioSummary.TopRiskWorkflows,
            GlobalWarnings: writingWarnings,
            DependencyGraph: batchResult.Value.PortfolioSummary.CrossSolutionDependencyGraph);

        var portfolioGeneration = new DocumentationGenerationResult(
            Package: new SolutionPackage("batch", resolvedOutputFolder, "unknown", Array.Empty<WorkflowDefinition>(), writingWarnings),
            WorkflowDocuments: Array.Empty<WorkflowDocumentModel>(),
            OverviewDocument: portfolioOverviewModel,
            Warnings: writingWarnings);

        var portfolioDocxResult = await docxWriter.WriteAsync(portfolioGeneration, resolvedOutputFolder);
        writingWarnings.AddRange(portfolioDocxResult.Warnings);

        var portfolioManifest = new BatchArtifactManifest(
            Mode: "batch",
            Status: batchResult.Status.ToString(),
            InputSpec: inputSpec,
            OutputFolder: resolvedOutputFolder,
            Inputs: inputs,
            PortfolioOverviewFile: portfolioDocxResult.Value?.OverviewFile,
            Solutions: solutionArtifacts,
            Summary: batchResult.Value.PortfolioSummary,
            Warnings: writingWarnings.Select(ToManifestWarning).ToArray(),
            Error: batchResult.ErrorMessage);

        var summaryPath = Path.Combine(resolvedOutputFolder, "portfolio-summary.json");
        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(batchResult.Value, options));

        var manifestPath = Path.Combine(resolvedOutputFolder, "portfolio-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(portfolioManifest, options));

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = batchResult.Status.ToString(),
            inputSpec,
            inputCount = inputs.Count,
            outputFolder = resolvedOutputFolder,
            portfolioOverviewFile = portfolioDocxResult.Value?.OverviewFile,
            manifestFile = manifestPath,
            summaryFile = summaryPath,
            solutions = solutionArtifacts,
            warnings = writingWarnings.Select(ToManifestWarning),
            error = batchResult.ErrorMessage
        }, options));

        return batchResult.Status == ProcessingStatus.Failed ? 1 : 0;
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        status = batchResult.Status.ToString(),
        inputSpec,
        inputCount = inputs.Count,
        outputFolder = resolvedOutputFolder,
        warnings = batchResult.Warnings.Select(ToManifestWarning),
        error = batchResult.ErrorMessage
    }, options));

    return batchResult.Status == ProcessingStatus.Failed ? 1 : 0;
}

var zipPath = inputSpec;
if (!File.Exists(zipPath))
{
    Console.Error.WriteLine($"Input ZIP not found: {zipPath}");
    return 2;
}

if (string.Equals(command, "document", StringComparison.OrdinalIgnoreCase)
    || string.Equals(command, "docx", StringComparison.OrdinalIgnoreCase)
    || string.Equals(command, "pack", StringComparison.OrdinalIgnoreCase))
{
    var extractionPipeline = new WorkflowExtractionPipeline(reader, parser);
    var workflowBuilder = new DeterministicWorkflowDocumentBuilder();
    var overviewBuilder = new DeterministicOverviewDocumentBuilder();
    var documentationPipeline = new WorkflowDocumentationPipeline(extractionPipeline, workflowBuilder, overviewBuilder);

    var docResult = await documentationPipeline.GenerateAsync(zipPath);
    var resolvedOutputFolder = ResolveOutputFolder(outputFolder, zipPath);
    Directory.CreateDirectory(resolvedOutputFolder);

    if (string.Equals(command, "docx", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "pack", StringComparison.OrdinalIgnoreCase))
    {
        var docxWriter = new OpenXmlDocxArtifactWriter(new DeterministicPngDiagramRenderer(diagramDetailLevel));
        var docxResult = docResult.Value is null
            ? new ParseResult<DocxArtifactResult>(ProcessingStatus.Failed, null, docResult.Warnings, docResult.ErrorMessage ?? "Documentation model generation failed.")
            : await docxWriter.WriteAsync(docResult.Value, resolvedOutputFolder);

        var diagramAssets = new List<DiagramAssetManifestEntry>();
        if (docResult.Value is not null)
        {
            var renderer = new DeterministicSvgDiagramRenderer(diagramDetailLevel);
            var diagramOutputFolder = Path.Combine(resolvedOutputFolder, "diagrams");
            Directory.CreateDirectory(diagramOutputFolder);

            for (var i = 0; i < docResult.Value.WorkflowDocuments.Count; i++)
            {
                var workflowDoc = docResult.Value.WorkflowDocuments[i];
                var renderResult = await renderer.RenderAsync(workflowDoc.Diagrams);

                if (renderResult.Value is null)
                {
                    continue;
                }

                var workflowSlug = ArtifactPathNaming.SanitizeFileName(workflowDoc.WorkflowName, "workflow");
                foreach (var asset in renderResult.Value)
                {
                    var filePath = Path.Combine(diagramOutputFolder, $"{i + 1:D3}-{workflowSlug}-{asset.FileName}");
                    await File.WriteAllBytesAsync(filePath, asset.Content);
                    diagramAssets.Add(new DiagramAssetManifestEntry(
                        WorkflowName: workflowDoc.WorkflowName,
                        DiagramType: asset.Type.ToString(),
                        File: filePath,
                        ContentType: asset.ContentType,
                        Caption: asset.Caption));
                }
            }
        }

        var workflowDocxFiles = docxResult.Value?.WorkflowFiles ?? Array.Empty<string>();
        var workflowEntries = docResult.Value is null
            ? Array.Empty<WorkflowArtifactManifestEntry>()
            : docResult.Value.WorkflowDocuments.Select((w, i) => new WorkflowArtifactManifestEntry(
                WorkflowName: w.WorkflowName,
                PrimaryDocumentFile: i < workflowDocxFiles.Count ? workflowDocxFiles[i] : string.Empty,
                DiagramFiles: diagramAssets
                    .Where(d => string.Equals(d.WorkflowName, w.WorkflowName, StringComparison.Ordinal))
                    .Select(d => d.File)
                    .ToArray())).ToArray();

        var archiveFile = string.Equals(command, "pack", StringComparison.OrdinalIgnoreCase)
            ? resolvedOutputFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".zip"
            : null;

        var bundleManifest = new ArtifactBundleManifest(
            Mode: command.ToLowerInvariant(),
            Status: docxResult.Status.ToString(),
            Input: zipPath,
            OutputFolder: resolvedOutputFolder,
            OverviewFile: docxResult.Value?.OverviewFile,
            Workflows: workflowEntries,
            DiagramAssets: diagramAssets,
            Warnings: docxResult.Warnings.Select(ToManifestWarning).ToArray(),
            Error: docxResult.ErrorMessage,
            ArchiveFile: archiveFile);

        var manifestPath = Path.Combine(resolvedOutputFolder, "bundle-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(bundleManifest, options));

        if (archiveFile is not null)
        {
            if (File.Exists(archiveFile))
            {
                File.Delete(archiveFile);
            }
            ZipFile.CreateFromDirectory(resolvedOutputFolder, archiveFile);
        }

        var docxPayload = new
        {
            status = docxResult.Status.ToString(),
            input = zipPath,
            outputFolder = resolvedOutputFolder,
            workflowDocxFiles,
            overviewDocxFile = docxResult.Value?.OverviewFile,
            diagramAssets,
            archiveFile,
            manifestFile = manifestPath,
            warnings = docxResult.Warnings.Select(ToManifestWarning),
            error = docxResult.ErrorMessage
        };

        Console.WriteLine(JsonSerializer.Serialize(docxPayload, options));
        return docxResult.Status == ProcessingStatus.Failed ? 1 : 0;
    }

    if (docResult.Value is not null)
    {
        var workflowFileEntries = new List<WorkflowArtifactManifestEntry>(docResult.Value.WorkflowDocuments.Count);
        for (var i = 0; i < docResult.Value.WorkflowDocuments.Count; i++)
        {
            var workflowDoc = docResult.Value.WorkflowDocuments[i];
            var workflowFileName = ArtifactPathNaming.BuildWorkflowDocumentFileName(i + 1, workflowDoc.WorkflowName, ".json");
            var workflowPath = Path.Combine(resolvedOutputFolder, workflowFileName);
            await File.WriteAllTextAsync(workflowPath, JsonSerializer.Serialize(workflowDoc, options));
            workflowFileEntries.Add(new WorkflowArtifactManifestEntry(
                WorkflowName: workflowDoc.WorkflowName,
                PrimaryDocumentFile: workflowPath,
                DiagramFiles: Array.Empty<string>()));
        }

        var overviewPath = Path.Combine(resolvedOutputFolder, "overview.json");
        await File.WriteAllTextAsync(overviewPath, JsonSerializer.Serialize(docResult.Value.OverviewDocument, options));

        var bundleManifest = new ArtifactBundleManifest(
            Mode: "document",
            Status: docResult.Status.ToString(),
            Input: zipPath,
            OutputFolder: resolvedOutputFolder,
            OverviewFile: overviewPath,
            Workflows: workflowFileEntries,
            DiagramAssets: Array.Empty<DiagramAssetManifestEntry>(),
            Warnings: docResult.Warnings.Select(ToManifestWarning).ToArray(),
            Error: docResult.ErrorMessage);

        var manifestPath = Path.Combine(resolvedOutputFolder, "bundle-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(bundleManifest, options));

        var manifest = new
        {
            status = docResult.Status.ToString(),
            input = zipPath,
            outputFolder = resolvedOutputFolder,
            workflowDocumentCount = docResult.Value.WorkflowDocuments.Count,
            overviewFile = overviewPath,
            manifestFile = manifestPath,
            warnings = docResult.Warnings.Select(ToManifestWarning),
            error = docResult.ErrorMessage
        };

        Console.WriteLine(JsonSerializer.Serialize(manifest, options));
    }
    else
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = docResult.Status.ToString(),
            input = zipPath,
            outputFolder = resolvedOutputFolder,
            workflowDocumentCount = 0,
            warnings = docResult.Warnings.Select(ToManifestWarning),
            error = docResult.ErrorMessage
        }, options));
    }

    return docResult.Status == ProcessingStatus.Failed ? 1 : 0;
}

var pipeline = new WorkflowExtractionPipeline(reader, parser);
var result = await pipeline.ExtractAsync(zipPath);

var payload = new
{
    status = result.Status.ToString(),
    input = zipPath,
    extractedPath = result.Value?.ExtractedPath,
    solutionVersion = result.Value?.Version,
    workflowCount = result.Value?.Workflows.Count ?? 0,
    workflows = result.Value?.Workflows.Select(w => new
    {
        id = w.WorkflowId,
        name = w.DisplayName,
        entity = w.Trigger.PrimaryEntity,
        mode = w.ExecutionMode.ToString(),
        trigger = new
        {
            onCreate = w.Trigger.OnCreate,
            onUpdate = w.Trigger.OnUpdate,
            onDelete = w.Trigger.OnDelete,
            filters = w.Trigger.AttributeFilters
        },
        graph = new
        {
            nodes = w.StageGraph.Nodes.Count,
            edges = w.StageGraph.Edges.Count
        },
        dependencies = w.Dependencies.Select(d => new
        {
            type = d.DependencyType,
            name = d.Name,
            referenceId = d.ReferenceId
        }),
        rootCondition = w.RootCondition is null
            ? null
            : new
            {
                operatorType = w.RootCondition.Operator.ToString(),
                left = w.RootCondition.Left,
                right = w.RootCondition.Right,
                children = w.RootCondition.Children.Count
            }
    }),
    warnings = result.Warnings.Select(ToManifestWarning),
    error = result.ErrorMessage
};

Console.WriteLine(JsonSerializer.Serialize(payload, options));

return result.Status == ProcessingStatus.Failed ? 1 : 0;

static (string Command, string ZipPath, string? OutputFolder, DiagramDetailLevel DiagramDetailLevel, DocumentNarrativeTone NarrativeTone, string OutputMode) ResolveCommand(string[] cliArgs)
{
    if (cliArgs.Length >= 2 && (string.Equals(cliArgs[0], "extract", StringComparison.OrdinalIgnoreCase)
        || string.Equals(cliArgs[0], "document", StringComparison.OrdinalIgnoreCase)
        || string.Equals(cliArgs[0], "docx", StringComparison.OrdinalIgnoreCase)
        || string.Equals(cliArgs[0], "pack", StringComparison.OrdinalIgnoreCase)
        || string.Equals(cliArgs[0], "batch", StringComparison.OrdinalIgnoreCase)
        || string.Equals(cliArgs[0], "docx-live", StringComparison.OrdinalIgnoreCase)))
    {
        var command = cliArgs[0];
        var input = cliArgs[1];
        string? outputFolder = null;

        var optionStart = 2;
        if ((string.Equals(command, "document", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "docx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "pack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "batch", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "docx-live", StringComparison.OrdinalIgnoreCase))
            && cliArgs.Length >= 3
            && !cliArgs[2].StartsWith("--", StringComparison.Ordinal))
        {
            outputFolder = cliArgs[2];
            optionStart = 3;
        }

        var optionArgs = cliArgs.Skip(optionStart).ToArray();
        var detailLevel = ResolveDiagramDetailLevel(optionArgs);
        var narrativeTone = ResolveNarrativeTone(optionArgs);
        var outputMode = ResolveOutputMode(optionArgs);
        return (command, input, outputFolder, detailLevel, narrativeTone, outputMode);
    }

    var defaultOptionArgs = cliArgs.Skip(1).ToArray();
    return ("extract", cliArgs[0], null, ResolveDiagramDetailLevel(defaultOptionArgs), ResolveNarrativeTone(defaultOptionArgs), ResolveOutputMode(defaultOptionArgs));
}

static DocumentNarrativeTone ResolveNarrativeTone(IEnumerable<string> args)
{
    var values = args.ToArray();
    for (var i = 0; i < values.Length; i++)
    {
        if (!string.Equals(values[i], "--narrative-tone", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (i + 1 >= values.Length)
        {
            break;
        }

        if (string.Equals(values[i + 1], "technical", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentNarrativeTone.Technical;
        }

        if (string.Equals(values[i + 1], "business", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentNarrativeTone.Business;
        }
    }

    return DocumentNarrativeTone.Business;
}

static string ResolveOutputMode(IEnumerable<string> args)
{
    var values = args.ToArray();
    for (var i = 0; i < values.Length; i++)
    {
        if (!string.Equals(values[i], "--output-mode", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (i + 1 >= values.Length)
        {
            break;
        }

        if (string.Equals(values[i + 1], "single", StringComparison.OrdinalIgnoreCase))
        {
            return "single";
        }

        if (string.Equals(values[i + 1], "per-workflow", StringComparison.OrdinalIgnoreCase))
        {
            return "per-workflow";
        }
    }

    return "per-workflow";
}

static DiagramDetailLevel ResolveDiagramDetailLevel(IEnumerable<string> args)
{
    var values = args.ToArray();
    for (var i = 0; i < values.Length; i++)
    {
        if (!string.Equals(values[i], "--diagram-detail", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (i + 1 >= values.Length)
        {
            break;
        }

        if (string.Equals(values[i + 1], "standard", StringComparison.OrdinalIgnoreCase))
        {
            return DiagramDetailLevel.Standard;
        }

        if (string.Equals(values[i + 1], "detailed", StringComparison.OrdinalIgnoreCase))
        {
            return DiagramDetailLevel.Detailed;
        }
    }

    return DiagramDetailLevel.Detailed;
}

static string ResolveOutputFolder(string? outputFolder, string zipPath)
{
    if (!string.IsNullOrWhiteSpace(outputFolder))
    {
        return Path.GetFullPath(outputFolder);
    }

    var baseName = Path.GetFileNameWithoutExtension(zipPath);
    return Path.Combine(
        Directory.GetCurrentDirectory(),
        "artifacts",
        ArtifactPathNaming.SanitizeFileName(baseName, "solution"));
}

static string ResolveBatchOutputFolder(string? outputFolder)
{
    if (!string.IsNullOrWhiteSpace(outputFolder))
    {
        return Path.GetFullPath(outputFolder);
    }

    var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
    return Path.Combine(Directory.GetCurrentDirectory(), "artifacts", $"batch-{stamp}");
}

static IReadOnlyList<string> ResolveBatchInputs(string inputSpec)
{
    if (string.IsNullOrWhiteSpace(inputSpec))
    {
        return Array.Empty<string>();
    }

    if (Directory.Exists(inputSpec))
    {
        return Directory
            .GetFiles(inputSpec, "*.zip", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    if (inputSpec.Contains('*') || inputSpec.Contains('?'))
    {
        var directory = Path.GetDirectoryName(inputSpec);
        var pattern = Path.GetFileName(inputSpec);
        var baseDirectory = string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(directory);

        if (!Directory.Exists(baseDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .GetFiles(baseDirectory, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    return File.Exists(inputSpec)
        ? new[] { Path.GetFullPath(inputSpec) }
        : Array.Empty<string>();
}

static WarningManifestEntry ToManifestWarning(ProcessingWarning warning)
{
    return new WarningManifestEntry(
        Code: warning.Code,
        Message: warning.Message,
        Source: warning.Source,
        Blocking: warning.IsBlocking,
        Category: warning.Category.ToString(),
        Severity: warning.Severity.ToString());
}

internal sealed record ArtifactBundleManifest(
    string Mode,
    string Status,
    string Input,
    string OutputFolder,
    string? OverviewFile,
    IReadOnlyList<WorkflowArtifactManifestEntry> Workflows,
    IReadOnlyList<DiagramAssetManifestEntry> DiagramAssets,
    IReadOnlyList<WarningManifestEntry> Warnings,
    string? Error,
    string? ArchiveFile = null);

internal sealed record WorkflowArtifactManifestEntry(
    string WorkflowName,
    string PrimaryDocumentFile,
    IReadOnlyList<string> DiagramFiles);

internal sealed record DiagramAssetManifestEntry(
    string WorkflowName,
    string DiagramType,
    string File,
    string ContentType,
    string Caption);

internal sealed record WarningManifestEntry(
    string Code,
    string Message,
    string? Source,
    bool Blocking,
    string Category,
    string Severity);

internal sealed record BatchArtifactManifest(
    string Mode,
    string Status,
    string InputSpec,
    string OutputFolder,
    IReadOnlyList<string> Inputs,
    string? PortfolioOverviewFile,
    IReadOnlyList<BatchSolutionArtifactEntry> Solutions,
    PortfolioSummaryModel Summary,
    IReadOnlyList<WarningManifestEntry> Warnings,
    string? Error);

internal sealed record BatchSolutionArtifactEntry(
    string InputPath,
    string Status,
    string? OutputFolder,
    string? OverviewFile,
    IReadOnlyList<string> WorkflowFiles);

