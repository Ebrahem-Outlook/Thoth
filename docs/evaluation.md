# Evaluation and Quality Gates

Thoth separates "a checkpoint exists" from "a checkpoint is trusted".

## Checkpoint Status

- `Missing`: checkpoint file does not exist.
- `LoadingFailed`: checkpoint file exists but cannot be loaded.
- `Unqualified`: checkpoint loads but metadata or metrics do not satisfy thresholds.
- `ExperimentalOnly`: checkpoint loads and has metadata, but has not passed mandatory evaluation yet.
- `QualifiedForGeneration`: allowed for direct generation.
- `QualifiedForUnderstanding`: allowed for user understanding.
- `QualifiedForAgentDecisions`: allowed for agent decisions.

## Required Metadata

Sidecar metadata lives at:

```text
<checkpoint>.metadata.json
```

It records:

- checkpoint format and version;
- architecture;
- tokenizer;
- model config hash;
- optimizer step;
- dataset manifest path;
- evaluation report path;
- evaluation metrics.

## Metrics

The default quality gate checks:

- minimum optimizer steps;
- minimum evaluated tokens;
- maximum average loss;
- maximum perplexity;
- `generation_health`;
- `language_health`;
- `no_internal_leak`;
- `deterministic_loading`;
- `minimum_task_benchmarks`;
- optional understanding and agent-decision scores.

Evaluation reports are machine-readable JSON so CI and the UI can inspect them.

## Benchmark Suites

`Thoth.Evaluation` ships a versioned `thoth-core/v1` benchmark suite covering:

- Arabic and English language health;
- UTF-8 integrity and non-empty/non-degenerate output;
- C++, C#, and TypeScript calculator prompts;
- code-fence closure and language detection;
- multi-turn cognition context;
- safety leakage markers such as `ordered tasks`, `request.atomize`, `answer.revise`, and `cognitive frame`.

Reports include suite hash, optional checkpoint/tokenizer/dataset hashes, command, timestamp, raw counts, category pass rates, and normalized scores. Loss-only evaluation reports intentionally do not qualify a checkpoint for generation; they only emit `loss_health` and `finite_loss`.
