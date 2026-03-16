using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using BN.WorkflowDoc.Core.Contracts;
using BN.WorkflowDoc.Core.Domain;

namespace BN.WorkflowDoc.Core.Parsing;

public sealed class SolutionPackageReader : ISolutionPackageReader
{
    private const string CustomizationsEntry = "customizations.xml";
    private const string SolutionEntry = "solution.xml";

    public async Task<ParseResult<SolutionPackage>> ReadAsync(string solutionZipPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(solutionZipPath))
        {
            return new ParseResult<SolutionPackage>(
                ProcessingStatus.Failed,
                null,
                new[]
                {
                    new ProcessingWarning(
                        "INPUT_EMPTY",
                        "Solution zip path is required.",
                        solutionZipPath,
                        true,
                        WarningCategory.Input,
                        WarningSeverity.Error)
                },
                "Solution zip path is required.");
        }

        if (!File.Exists(solutionZipPath))
        {
            return new ParseResult<SolutionPackage>(
                ProcessingStatus.Failed,
                null,
                new[]
                {
                    new ProcessingWarning(
                        "ZIP_NOT_FOUND",
                        "Solution zip file was not found.",
                        solutionZipPath,
                        true,
                        WarningCategory.Input,
                        WarningSeverity.Error)
                },
                "Solution zip file was not found.");
        }

        var warnings = new List<ProcessingWarning>();

        try
        {
            var extractPath = Path.Combine(Path.GetTempPath(), "BNWorkflowDocumenter", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractPath);

            using var zip = ZipFile.OpenRead(solutionZipPath);
            var customizations = zip.GetEntry(CustomizationsEntry);
            var solution = zip.GetEntry(SolutionEntry);

            if (customizations is null)
            {
                warnings.Add(new ProcessingWarning(
                    "MISSING_CUSTOMIZATIONS_XML",
                    "customizations.xml is missing from solution package.",
                    CustomizationsEntry,
                    true,
                    WarningCategory.Input,
                    WarningSeverity.Error));
            }

            if (solution is null)
            {
                warnings.Add(new ProcessingWarning(
                    "MISSING_SOLUTION_XML",
                    "solution.xml is missing from solution package.",
                    SolutionEntry,
                    false,
                    WarningCategory.Input,
                    WarningSeverity.Warning));
            }

            var canonicalExtractRoot = Path.GetFullPath(extractPath) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = Path.Combine(extractPath, entry.FullName);
                var canonicalDest = Path.GetFullPath(destinationPath);
                if (!canonicalDest.StartsWith(canonicalExtractRoot, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(new ProcessingWarning(
                        "ZIP_SLIP_BLOCKED",
                        $"ZIP entry '{entry.FullName}' was skipped: path traversal detected.",
                        entry.FullName,
                        false,
                        WarningCategory.Input,
                        WarningSeverity.Warning));
                    continue;
                }

                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                if (!string.IsNullOrEmpty(entry.Name))
                {
                    entry.ExtractToFile(destinationPath, overwrite: true);
                }
            }

            var version = "unknown";
            if (solution is not null)
            {
                var solutionPath = Path.Combine(extractPath, SolutionEntry);
                if (File.Exists(solutionPath))
                {
                    version = await TryReadVersionAsync(solutionPath, cancellationToken).ConfigureAwait(false);
                }
            }

            var status = warnings.Any(static x => x.IsBlocking)
                ? ProcessingStatus.PartialSuccess
                : ProcessingStatus.Success;

            var package = new SolutionPackage(
                solutionZipPath,
                extractPath,
                version,
                Array.Empty<WorkflowDefinition>(),
                warnings);

            return new ParseResult<SolutionPackage>(status, package, warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new ProcessingWarning(
                "ZIP_READ_FAILED",
                ex.Message,
                solutionZipPath,
                true,
                WarningCategory.Input,
                WarningSeverity.Critical));
            return new ParseResult<SolutionPackage>(ProcessingStatus.Failed, null, warnings, ex.Message);
        }
    }

    private static async Task<string> TryReadVersionAsync(string solutionXmlPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(solutionXmlPath);
        var xmlSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, Async = true };
        using var xmlReader = XmlReader.Create(stream, xmlSettings);
        var document = await XDocument.LoadAsync(xmlReader, LoadOptions.None, cancellationToken).ConfigureAwait(false);

        var versionElement = document
            .Descendants()
            .FirstOrDefault(x => string.Equals(x.Name.LocalName, "Version", StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(versionElement?.Value)
            ? "unknown"
            : versionElement.Value.Trim();
    }
}

