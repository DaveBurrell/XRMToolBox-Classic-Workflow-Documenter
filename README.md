# BN Workflow Documenter

Parses unmanaged Dynamics 365 solution ZIP files, extracts classic workflow metadata, generates quality/dependency analysis, and produces workflow documentation artifacts.

<img width="966" height="893" alt="image" src="https://github.com/user-attachments/assets/787c9098-0b31-48ca-810e-ad5351364cb7" />


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

## XrmToolBox Plugin

The repository also ships an XrmToolBox plugin named **BridgeNexa Workflow Documenter** for live Dataverse environments.

### What The Plugin Does

- Connects to your Dataverse organization and loads classic process records.
- Lets you filter and select workflows, dialogs, and actions.
- Generates documentation by invoking the bundled CLI runtime from inside XrmToolBox.
- Uses the packaged `cli/` runtime folder when available and retains the zipped runtime as a fallback for direct plugin deployment.
- Produces either per-workflow DOCX files + overview, or one combined full-detail DOCX.

### Install The Plugin

Option 1: Plugin Store (recommended when available)

1. Open XrmToolBox.
2. Open **Plugins Store**.
3. Search for **BridgeNexa Workflow Documenter**.
4. Install and restart XrmToolBox when prompted.

Option 2: Local package/manual deployment

1. Build and pack this repository:

```powershell
dotnet build src/BN.WorkflowDoc.XrmToolBox/BN.WorkflowDoc.XrmToolBox.csproj -c Release
dotnet pack src/BN.WorkflowDoc.XrmToolBox/BN.WorkflowDoc.XrmToolBox.csproj -c Release
```

2. Use the generated package in `artifacts/nuget/` (or the prepared submission bundle under `artifacts/submission-v<version>/`).
3. If deploying from the `.nupkg`, preserve the packaged folder structure under `lib/net48/Plugins/BN.WorkflowDoc.XrmToolBox/`, including the bundled `cli/` folder.
4. If deploying manually from the prepared `release-plugin` bundle instead, copy the plugin payload files to your XrmToolBox plugin folder:
	- `BN.WorkflowDoc.XrmToolBox.dll`
	- `BN.WorkflowDoc.XrmToolBox.pdb`
	- `BN.WorkflowDoc.Cli.runtime.zip`

### How To Use The Plugin

1. Open **BridgeNexa Workflow Documenter** in XrmToolBox.
2. Connect to your Dataverse environment (the plugin can open without a connection, but loading/export requires one).
3. Click **Load Workflows** to pull available classic workflows/dialogs/actions.
4. Filter using:
	- search box (name/entity/trigger text)
	- category chips (**Workflow**, **Dialog**, **Action**)
5. Select items with checkboxes. You can use:
	- **Select All Found**
	- **Select Visible**
	- **Clear All**
6. Click **Settings** to configure:
	- default output folder
	- narrative style (Business or Technical)
	- diagram detail (Detailed or Standard)
	- output mode (Per-workflow + overview, or Single combined full-detail)
7. Click **Generate Docs**.
8. Review the completion message and open the output folder.

### Plugin Output

Depending on settings and selected workflows, the plugin generates:

- overview documentation (or one combined full-detail document)
- per-workflow DOCX documents (when per-workflow mode is selected)
- diagrams and supporting artifacts
- warning details in appendices/worker output when partial-success conditions are encountered

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
