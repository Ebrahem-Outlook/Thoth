# Evaluation and Quality Gates

Thoth separates "a checkpoint exists" from "a checkpoint is trusted".

## Checkpoint Status

- `Missing`: checkpoint file does not exist.
- `LoadingFailed`: checkpoint file exists but cannot be loaded.
- `Unqualified`: checkpoint loads but metadata or metrics do not satisfy thresholds.
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
- `no_internal_leak`;
- optional understanding and agent-decision scores.

Evaluation reports are machine-readable JSON so CI and the UI can inspect them.

