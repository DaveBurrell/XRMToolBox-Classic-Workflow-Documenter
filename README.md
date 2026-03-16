# BN Workflow Documenter

Parses unmanaged Dynamics 365 solution ZIP files, extracts classic workflow metadata, generates quality/dependency analysis, and produces workflow documentation artifacts.

## Current Capabilities

- Classic workflow extraction from unmanaged solution ZIP packages.
- Workflow and solution overview model generation.
- Quality scoring with risk-band output.
- Dependency graph construction and visualization.
- DOCX output generation for workflow-level and overview reports.
- Detailed workflow documentation sections including fields read, fields set or updated, process flow steps, transition matrix, and full step breakdown.
- Workflow step inventory CSV export for downstream review and audit.
- Diagram rendering with improved visual styling, action-type color coding, semantic node shapes, vertical flow layout, and automatic large-diagram splitting for Word readability.
- Business-friendly titles for split diagram views derived from workflow decision points and branch themes.
- Configurable diagram detail level (`Standard` or `Detailed`) in both CLI and WPF UI.
- CLI modes for single-run and batch/portfolio execution.
- WPF desktop UI with a single Configure view for single-run and batch generation, plus progress, cancellation, warnings, and output shortcuts.

## Output Artifacts

- Per-workflow DOCX report with summary, trigger matrix, fields read, fields set or updated, process flow steps, transition matrix, diagrams, warnings, and full step inventory.
- Solution overview DOCX with executive summary, quality assessment, dependency graph, workflow cards, and appendix-level workflow step inventory.
- SVG diagram exports for each workflow.
- `workflow-step-inventory.csv` containing flattened step-level inventory across generated workflows.
- JSON manifests and portfolio summary artifacts for single-run and batch execution modes.

## Sample Outputs

Single-run `docx` or `pack` output folder typically contains:

- `overview.docx`
- `001-<workflow-name>.docx`
- `002-<workflow-name>.docx`
- `diagrams/`
- `workflow-step-inventory.csv`
- `bundle-manifest.json`
- optional archive: `<output-folder>.zip` when using `pack`

Batch output folder typically contains:

- `portfolio-overview.docx`
- `portfolio-summary.json`
- `portfolio-manifest.json`
- `workflow-step-inventory.csv`
- one folder per solution, for example: `001-<solution-name>/`
- `001-<solution-name>/overview.docx`
- `001-<solution-name>/001-<workflow-name>.docx`
- `001-<solution-name>/002-<workflow-name>.docx`
- `001-<solution-name>/diagrams/`
- optional archive: `<output-folder>.zip` when using packed batch output

## Which Mode To Use

- Use `extract` when you want structured extraction output for debugging or parser inspection.
- Use `document` when you want JSON workflow and overview artifacts without generating Word documents.
- Use `docx` when you want full documentation outputs including workflow DOCX files, overview DOCX, diagrams, manifests, and step inventory CSV.
- Use `pack` when you want the same output as `docx` plus a ZIP archive of the generated folder.
- Use `batch` when you need to process multiple solution ZIPs in one run and generate portfolio-level summary artifacts.
- Use the WPF app when you want a desktop workflow with drag and drop input, configure-time tone/detail settings, progress tracking, warnings, and batch selection without CLI commands.

## Build And Test

```powershell
dotnet restore BNWorkflowDocumenter.sln
dotnet build BNWorkflowDocumenter.sln
dotnet test tests/BN.WorkflowDoc.Core.Tests/BN.WorkflowDoc.Core.Tests.csproj
```

## CLI Usage

```powershell
dotnet run --project src/BN.WorkflowDoc.Cli -- extract <path-to-solution.zip>
dotnet run --project src/BN.WorkflowDoc.Cli -- document <path-to-solution.zip> [output-folder]
dotnet run --project src/BN.WorkflowDoc.Cli -- docx <path-to-solution.zip> [output-folder] [--diagram-detail standard|detailed]
dotnet run --project src/BN.WorkflowDoc.Cli -- pack <path-to-solution.zip> [output-folder] [--diagram-detail standard|detailed]
dotnet run --project src/BN.WorkflowDoc.Cli -- batch <zip|folder|glob> [output-folder] [--diagram-detail standard|detailed]
```

## First Commit Checklist

1. Run: `dotnet build BNWorkflowDocumenter.sln`
2. Run: `dotnet test tests/BN.WorkflowDoc.Core.Tests/BN.WorkflowDoc.Core.Tests.csproj`
3. Run: `dotnet test tests/BN.WorkflowDoc.Wpf.Tests/BN.WorkflowDoc.Wpf.Tests.csproj`
4. Launch the desktop app once: `dotnet run --project src/BN.WorkflowDoc.Wpf`
5. Verify generated outputs for one sample ZIP: workflow DOCX files, `overview.docx`, `diagrams/`, and `workflow-step-inventory.csv`
6. Confirm docs are current: `README.md`, `docs/architecture.md`, `docs/run-modes.md`, `docs/release-checklist.md`

## Architecture Docs

- docs/architecture.md
- docs/run-modes.md

## Extension Points

- `IWorkflowDefinitionParser`
- `IWorkflowDocumentBuilder`
- `IOverviewDocumentBuilder`
- `IDiagramRenderer`
- `IWorkflowAnalysisEngine`
- `IDependencyGraphBuilder`
- `IDependencyGraphDiagramMapper`
- `IWorkflowDocumentationPipeline`
- `IPortfolioDocumentationPipeline`
