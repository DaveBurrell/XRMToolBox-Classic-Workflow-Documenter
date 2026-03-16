## BN Workflow Documenter v1.0.0

First production-ready release of BN Workflow Documenter.

### What is included
- Classic workflow extraction from unmanaged Dynamics 365 solution ZIP files.
- Workflow and overview DOCX generation.
- Quality/risk scoring and dependency graph analysis.
- Workflow step inventory CSV export.
- SVG diagram exports plus DOCX-embedded diagram images.
- CLI modes: `extract`, `document`, `docx`, `pack`, `batch`.
- WPF desktop UI with configure-based generation flow.
- Diagram detail controls (`standard` and `detailed`) in CLI and WPF.

### Diagram improvements in this release
- Action-type color coding.
- Semantic node shapes.
- Vertical layout and large-diagram page splitting.
- Business-focused split diagram titles.

### Validation
- Core tests: 37 passed.
- WPF tests: 2 passed.

### Notes
- v1.0.0 is the initial public baseline.
- Scope is classic workflow documentation from unmanaged solutions.
