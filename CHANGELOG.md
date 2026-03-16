# Changelog

All notable changes to this project are documented in this file.

The format is based on Keep a Changelog and this project follows Semantic Versioning.

## [1.0.0] - 2026-03-16

### Added
- Initial end-to-end release of the Classic Workflow Documenter for Dynamics 365 unmanaged solution ZIP files.
- Classic workflow extraction pipeline from solution metadata.
- Workflow and solution overview model generation.
- Quality scoring with risk-band output.
- Dependency graph construction and visualization.
- DOCX artifact generation for per-workflow reports and overview reports.
- Workflow step inventory CSV export for downstream analysis.
- SVG diagram export for each workflow.
- CLI execution modes: extract, document, docx, pack, and batch.
- Configurable diagram detail level (Standard or Detailed) for CLI and WPF.
- WPF desktop application with single Configure flow, progress reporting, cancellation, warning display, and output shortcuts.

### Changed
- Diagram readability improvements including action-type color coding, semantic node shapes, vertical layout, and large-diagram splitting.
- Business-focused split titles for multi-page workflow diagrams.
- Documentation alignment across architecture, run-modes, and release checklist docs.

### Testing
- Core test suite passing: 37 tests.
- WPF test suite passing: 2 tests.

### Notes
- This is the first public baseline release of the project.

[1.0.0]: https://github.com/DaveBurrell/BridgeNexa-Workflow-Documenter/releases/tag/v1.0.0
