# Run Modes

## CLI Modes

- `extract <zip>`
  - Parses package and returns structured extraction payload.
- `document <zip> [output]`
  - Emits JSON workflow/overview artifacts and bundle manifest.
- `docx <zip> [output]`
  - Emits DOCX workflow/overview outputs plus diagram assets, workflow step inventory CSV, and manifest.
  - Optional: `--diagram-detail standard|detailed`.
- `pack <zip> [output]`
  - Same as `docx` and archives output folder as ZIP.
  - Optional: `--diagram-detail standard|detailed`.
- `batch <zip|folder|glob> [output]`
  - Processes multiple ZIP inputs and emits per-solution outputs plus portfolio summary artifacts.
  - Optional: `--diagram-detail standard|detailed`.

## WPF Modes

- Single solution mode
  - Choose one ZIP and output folder.
  - Configure Narrative Tone and Diagram Detail in the same Configure view.
  - Generate documentation with optional archive packing.
- Batch mode
  - Select multiple ZIP files.
  - Configure Narrative Tone and Diagram Detail in the same Configure view.
  - Generate per-solution output folders, portfolio summary artifacts, and optional archive.

## Narrative Tone

- Business (plain English)
- Technical

Tone applies to generated narrative sections in workflow and overview documents.

## Documentation Output Detail

- Workflow DOCX reports include fields read, fields set or updated, process flow steps, transition matrix, and full step breakdown tables.
- Large diagrams are automatically split into multiple named views for readability in Word.
- Overview outputs include detailed workflow step inventory in an appendix.
- `workflow-step-inventory.csv` is generated alongside DOCX outputs to provide a flat export of workflow step detail.

## Progress and Cancellation

- Both CLI and WPF support cancellation through cancellation tokens in pipeline calls.
- WPF shows stage-level progress, step counters, elapsed time, and ETA for determinate batch work.
