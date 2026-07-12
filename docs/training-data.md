# Training Data

Training data lives under `data/training` by default. Keep only data that is legally usable for local model training.

## Recommended Layout

```text
data/training/
  pretrain/
  validation/
  instructions/
```

- `pretrain`: plain text, Markdown, source files, or JSONL text used for next-token training.
- `validation`: held-out data used for perplexity/loss evaluation.
- `instructions`: instruction tuning examples in JSONL.

## Instruction JSONL

Each line:

```json
{
  "id": "example-001",
  "language": "en",
  "task": "coding",
  "messages": [
    { "role": "user", "content": "Build a C# calculator method." },
    { "role": "assistant", "content": "public static decimal Add(decimal a, decimal b) => a + b;" }
  ]
}
```

The loader requires an id, language, task, one user message, and one assistant message.

## Manifest

`CorpusLoader` writes a manifest next to checkpoints. It records source files, byte counts, character counts, token counts, partitions, and hashes. This manifest is part of checkpoint quality metadata.

## Licensing Warning

Do not train on private, copyrighted, customer, production, or secret material unless you have explicit permission. Do not include API keys, credentials, tokens, or personal data in datasets.

