# Validation

EFDoctor's rules are validated by running them against real, open-source EF Core codebases and recording a verdict for every finding. The goal is the product brief's gate: at least 90% precision for high- and medium-confidence rules on a real corpus.

## What's here

| Path | Contents |
|---|---|
| `corpus.json` | The public corpus: repositories pinned to commits, with the solution or project to analyze |
| `run_corpus.py` | Checks out each project, restores it, runs EFDoctor, and writes results and triage files |
| `summarize.py` | Turns the triage verdicts into per-rule and per-project precision |
| `SUMMARY.md` | Generated precision tables for the latest run |
| `FINDINGS.md` | A hand-written log of what validation found, and what was fixed |

Running the harness also writes these outputs, which the public repository doesn't track:

| Path | Contents |
|---|---|
| `results/<name>.json` | EFDoctor's JSON report for each project |
| `results/<name>.run.json` | Run metadata: commits, timings, restore failures, and whether the run was complete |
| `results/triage/<name>.md` | One section per finding, with a verdict and a note |

## Running it

```bash
python3 validation/run_corpus.py                 # the whole corpus
python3 validation/run_corpus.py --only eshop    # one project
python3 validation/summarize.py                  # regenerate SUMMARY.md
```

Checkouts go to `~/.cache/efdoctor-corpus` (override with `--cache`). The runner restores each project separately, so one project that can't restore (for example, one that needs a MAUI workload) doesn't block the rest.

A run is **complete** only when every EF Core project in the target restored. EFDoctor can't resolve EF Core types in an unrestored project, so its rules stay silent there. An incomplete run's "0 findings" means nothing, and the runner says so.

## Triage

Open `results/triage/<name>.md` and set each **Verdict**:

| Verdict | Meaning |
|---|---|
| `TP` | The finding is correct and worth acting on |
| `FP` | The finding is wrong |
| `acceptable` | The rule identified its shape correctly, but it's fine in this context (common for advisory rules) |
| `debatable` | Reasonable people could disagree; excluded from precision |
| `TODO` | Not triaged yet |

Add a short **Note** explaining the verdict, especially for `FP`. Rerunning the corpus keeps existing verdicts, because findings are keyed by rule, file, line, and column, and the corpus is pinned.

`summarize.py` reports two figures per rule:

- **Strict precision**, TP / (TP + FP + acceptable): how often a finding is worth acting on.
- **Detection precision**, (TP + acceptable) / (TP + FP + acceptable): how often the rule identified its shape correctly.

## Private codebases

Validate a private codebase the same way, keeping its results out of this repository:

```bash
python3 validation/run_corpus.py --corpus ~/private/corpus.json --results ~/private/efdoctor-results
python3 validation/summarize.py --results ~/private/efdoctor-results --output ~/private/efdoctor-results/SUMMARY.md
```
