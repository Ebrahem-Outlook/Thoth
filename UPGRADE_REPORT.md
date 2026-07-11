# Thoth Neural Upgrade Report

## Delivered in this archive

This upgrade replaces the claim of a "self-contained model" with a real vertical neural-model path while preserving the existing product shell.

### Added

- `Thoth.Tokenization`: deterministic UTF-8 byte tokenizer.
- `Thoth.Model`: a randomly initialized recurrent language model with trainable embeddings and matrices.
- Full forward pass, softmax cross-entropy, back-propagation through time, global gradient clipping and AdamW.
- Atomic binary checkpoint save/load including optimizer moments.
- `Thoth.Training`: source-tree/text corpus loading, warmup + cosine schedule, resume support and progress reports.
- `Thoth.Inference`: top-k temperature sampling and `IChatModel` integration.
- `Thoth.Evaluation`: held-out loss and perplexity.
- CLI commands: `train`, `generate`, and `evaluate`.
- Iterative agent decision loop that observes each tool result before choosing the next action.
- Duplicate tool-call protection.
- Exact atomic `file.patch` tool.
- Ranked multi-token memory retrieval.
- Neural, checkpoint, agent-loop and memory tests.

## Honest score after this pass

These scores describe the code foundation, not the intelligence of an untrained checkpoint.

| Area | Before | This archive | Why it is not 10/10 yet |
|---|---:|---:|---|
| Product architecture | 7/10 | 8.5/10 | CI is included; still needs observability, migrations and load testing |
| Tools / sandbox / memory | 6/10 | 7.5/10 | Needs diff parser, approvals, rollback and stronger shell isolation |
| Agent autonomy | 2/10 | 6/10 foundation | Quality depends on a trained decision model and executable verification |
| Language intelligence | 0.5/10 | trainable, initially low | Intelligence comes from data, scale and training, not source files alone |
| Real neural model | 0/10 | 4/10 bootstrap | Real neural learning exists, but the final architecture should be a Transformer |
| Training pipeline | 0/10 | 6/10 local baseline | Needs GPU, distributed training, data governance, mixed precision and experiment tracking |

## What could not be truthfully delivered as "10/10"

A 10/10 model cannot be created by adding code alone. It requires a high-quality licensed corpus, significant compute, repeated experiments, evaluation sets, model scaling, instruction tuning, and outcome-based optimization. This archive gives Thoth the first real mechanism capable of learning from those inputs.

## Verification status in the creation environment

The archive passed structural checks for C# delimiter balance, project references, XML project files, JSON configuration, and solution membership. `npm ci` and the Angular production build completed successfully. The creation environment did not contain the .NET SDK, so the .NET solution was not compiled here. Run the commands below first on a machine with .NET 8:

```powershell
dotnet restore Thoth.sln
dotnet build Thoth.sln -c Release
dotnet test Thoth.sln -c Release
```

Any compiler finding should be fixed before spending time on a long training run.
