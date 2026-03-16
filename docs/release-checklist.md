# Release Readiness Checklist

Date: 2026-03-16

## Scope

Validation summary for BN Workflow Documenter expansion phases:
- Quality scoring and dependency graph overview
- Batch/portfolio CLI and WPF paths
- DOCX overview sections and manifests
- Build/test regression checks

## Build And Test

1. Full solution build
- Command: `dotnet build BNWorkflowDocumenter.sln`
- Result: PASS

2. Core test suite
- Command: `dotnet test tests/BN.WorkflowDoc.Core.Tests/BN.WorkflowDoc.Core.Tests.csproj`
- Result: PASS (37 passed, 0 failed)

3. WPF behavior tests
- Command: `dotnet test tests/BN.WorkflowDoc.Wpf.Tests/BN.WorkflowDoc.Wpf.Tests.csproj`
- Result: PASS (2 passed, 0 failed)

## Functional Validation

1. CLI batch mode with 3 ZIP inputs
- Command pattern: `dotnet run --project src/BN.WorkflowDoc.Cli -- batch <glob> <output>`
- Executed input glob: `tmp-samples/*.zip`
- Result: PASS
- Evidence:
  - `tmp-samples/out/001-sample1`
  - `tmp-samples/out/002-sample2`
  - `tmp-samples/out/003-sample3`
  - `tmp-samples/out/portfolio-manifest.json`
  - `tmp-samples/out/portfolio-summary.json`
  - `tmp-samples/out/overview.docx`

2. Overview DOCX required sections
- Checked in generated `tmp-samples/out/overview.docx` (document XML)
- `Quality Assessment Summary`: PASS
- `Dependency Graph Overview`: PASS
- `Workflow Risk Matrix`: PASS

3. Portfolio manifest structure
- Checked `tmp-samples/out/portfolio-manifest.json`
- `Summary` field present: PASS
- `Solutions` list present with expected count (3): PASS

## Documentation And Maintainability

1. README updated to current capabilities and commands
- File: `README.md`
- Result: PASS

2. Contributor docs added
- File: `docs/architecture.md`
- File: `docs/run-modes.md`
- Result: PASS

4. UI/design documentation alignment
- Current WPF experience uses a unified Configure view (no separate Advanced Options banner/tab).
- README/run-modes/architecture documentation updated to reflect current layout and configurable diagram detail setting.
- Result: PASS

3. XML docs added on key public contracts/interfaces
- Files include:
  - `src/BN.WorkflowDoc.Core/Contracts/DocumentContracts.cs`
  - `src/BN.WorkflowDoc.Core/Application/DocxArtifactWriter.cs`
  - `src/BN.WorkflowDoc.Core/Application/WorkflowDocumentationPipeline.cs`
- Result: PASS (spot-checked)

## Remaining Manual Validation

1. WPF interactive batch run with 3+ ZIPs
- Status: PENDING (manual GUI interaction required)
- Manual checklist:
  - Select Batch mode
  - Add 3+ ZIP files
  - Run with and without pack mode
  - Confirm per-solution rows in results panel show status/workflow/warnings/duration
  - Confirm output shortcuts open expected folders/files
  - Confirm cancellation behavior during active run

## Overall Status

- Automated and terminal-verifiable checks: PASS
- Manual GUI acceptance checks: PENDING

Release recommendation:
- Ready for release candidate after manual WPF batch acceptance pass.
