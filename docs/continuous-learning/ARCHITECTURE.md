# Thoth Continuous Learning Architecture

Thoth continuous learning is a bounded local pipeline:

```text
allowlisted source -> bounded spool -> security/quality scan -> normalize/dedup
  -> volatility route -> concept memory or token queue -> replay mixer -> local trainer
  -> latest/emergency checkpoint -> status/report
```

All runtime data lives under `data/continuous/` and is ignored by Git.

The first implementation uses a conservative CPU-safe dense Transformer window for continual replay. Larger dense runs and adapter growth are gated by the resource governor and checkpoint validation because the previous Candidate 1 attempt showed severe RAM pressure.

