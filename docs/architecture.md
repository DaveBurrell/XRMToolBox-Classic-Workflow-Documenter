# Architecture Overview

BN Workflow Documenter processes unmanaged Dynamics 365 solution ZIP files and generates documentation artifacts.

## Projects

- `src/BN.WorkflowDoc.Core`
  - Parsing: solution/package reading, workflow XML/XAML extraction and normalization.
  - Application: deterministic builders, quality analysis, dependency graph, documentation and portfolio pipelines.
  - Rendering: SVG/PNG diagram renderers with vertical layout, large-diagram paging, and business-friendly split titles.
- `src/BN.WorkflowDoc.Cli`
  - Commands for extract/document/docx/pack/batch execution modes.
- `src/BN.WorkflowDoc.Wpf`
  - Desktop UI for single and batch documentation runs with a unified Configure view, progress, cancellation, and results view.
- `tests/BN.WorkflowDoc.Core.Tests`
  - Unit and component-style tests for parsing, analysis, graphing, rendering, and pipeline behavior.

## Core Flow

1. Read and extract ZIP package files.
2. Parse customizations and workflow definitions.
3. Build normalized domain models (triggers, graph, conditions, dependencies).
4. Enrich with quality and risk metrics.
5. Build workflow-level and overview document models, including step inventory and transition detail.
6. Render diagrams (PNG for DOCX embedding, SVG for exports) with automatic paging for large workflows.
7. Write DOCX artifacts, CSV step inventory export, and manifests.

## Workflow Documentation Sections

Per-workflow reports are designed to capture both summary and step-level detail. The workflow DOCX writer emits:

1. Workflow summary and overview narrative.
2. Trigger matrix and execution mode summary.
3. Fields read table.
4. Fields set or updated table.
5. Process flow steps table.
6. Transition matrix.
7. Full step breakdown with per-step metadata.
8. Diagram views, including paged diagram segments when the workflow is large.
9. Warning appendix.

The overview DOCX adds solution-level quality, dependency, and workflow portfolio sections plus an appendix with detailed workflow step inventory.

## Diagram Strategy

- Diagrams use a top-to-bottom flow layout with swimlane columns.
- Diagrams are color-coded by action type and use semantic node shapes (including decision diamonds and stop pills).
- Large diagrams are split into multiple views when node density would reduce readability in Word.
- Split boundaries prefer decision and merge points rather than raw fixed-size chunks.
- Split captions are normalized into business-friendly titles when technical labels can be mapped to process themes.
- Diagram node detail density is configurable via `DiagramDetailLevel` (`Standard` or `Detailed`) in CLI and WPF run flows.

## Batch/Portfolio Flow

1. Process all provided ZIP inputs sequentially in `PortfolioDocumentationPipeline`.
2. Aggregate per-solution status, warnings, durations, and quality signals.
3. Build cross-solution summary models and dependency network view.
4. Emit portfolio summary JSON and DOCX artifacts.

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

## Naming Conventions

Use `ArtifactPathNaming` for file and folder names to keep output conventions consistent across Core, CLI, and WPF paths.
