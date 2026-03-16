namespace BN.WorkflowDoc.Core.Application;

/// <summary>
/// Provides consistent file and folder naming rules across Core, CLI, and WPF flows.
/// </summary>
public static class ArtifactPathNaming
{
    public static string SanitizeFileName(string value, string fallback = "artifact")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray();

        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    public static string BuildWorkflowDocumentFileName(int ordinal, string workflowName, string extension)
    {
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return $"workflow-{ordinal:D3}-{SanitizeFileName(workflowName, "workflow")}{ext}";
    }

    public static string BuildSolutionFolderName(int ordinal, string solutionName)
    {
        return $"{ordinal:D3}-{SanitizeFileName(solutionName, "solution")}";
    }
}
