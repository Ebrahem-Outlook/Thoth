# Thoth Current Training Baseline

Date: 2026-07-12

This is a factual baseline for the local-only v0.6 training work. It records what exists before the tokenizer/data/training phases are expanded.

## Baseline Commands

| Command | Result |
| --- | --- |
| `dotnet --info` | Passed. SDK `9.0.302`; .NET 8 runtime installed; Windows `win-x64`; no `global.json`. |
| `dotnet restore Thoth.sln` | Passed. All projects were already up to date. |
| `dotnet build Thoth.sln -c Release` | Passed with 0 warnings and 0 errors. |
| `dotnet test Thoth.sln -c Release` | Initial baseline passed: 88 tests. After Phase 0 regressions, passed: 90 tests. |
| `npm ci --prefix src\Thoth.Web` | Passed. npm reported 7 dependency vulnerabilities: 3 low, 4 high. |
| `npm run build --prefix src\Thoth.Web` | Passed. Angular production bundle generated in `src\Thoth.Web\dist\Thoth.Web`. |

## Solution Inventory

`Thoth.sln` contains these projects:

- `src/Thoth.Api`
- `src/Thoth.Cli`
- `src/Thoth.Cognition`
- `src/Thoth.Core`
- `src/Thoth.Evaluation`
- `src/Thoth.Inference`
- `src/Thoth.Llm`
- `src/Thoth.Memory`
- `src/Thoth.Model`
- `src/Thoth.Runtime`
- `src/Thoth.Sandbox`
- `src/Thoth.Tokenization`
- `src/Thoth.Tools`
- `src/Thoth.Training`
- `tests/Thoth.Tests`

## Tokenizers

- `ByteTokenizer` is the default runtime tokenizer. It is deterministic UTF-8 byte-level encoding with fixed special token IDs.
- `BpeTokenizer` is a deterministic byte-level BPE implementation with byte fallback. It currently saves a single `tokenizer.json` artifact and loads from that artifact.
- Missing for v0.6: multi-file tokenizer artifact layout, explicit special tokens for chat/tool turns, manifest hashes, streaming BPE training, save/load metadata checks, and full tokenizer CLI profiles.

## Model and Training Stack

- `Thoth.Model` references `TorchSharp-cpu` version `0.107.0`.
- The legacy `RecurrentLanguageModel` remains the default checkpoint format at `data/models/thoth-bootstrap.bin`.
- `TransformerLanguageModel` is the older deterministic array implementation. It remains present for reference and checkpoint compatibility.
- `TorchTransformerLanguageModel` is the current TorchSharp decoder-only Transformer core. It has embeddings, causal attention, RoPE, RMSNorm, SwiGLU/FFN, dropout control, cross-entropy loss, AdamW-style optimizer state, gradient clipping, and finite-gradient checks.
- CPU correctness tests confirm gradients, parameter updates, output shape, future-token isolation, padding ignore behavior, deterministic inference without dropout, and loss reduction on a deterministic fixture.
- Missing for v0.6-v0.9: Torch tensor checkpoint round-trip, resume on the Torch model, generation with KV cache, sharded token loaders, gradient accumulation, long-run approval gates, and formal evaluation reports for a trained checkpoint.

## Data and Checkpoints

- `CorpusLoader` reads local files, skips build/generated/private-ish directories, normalizes text lightly, tokenizes, and creates a source manifest.
- `InstructionDatasetLoader` parses local instruction examples into training text.
- RNN and array-Transformer training CLIs exist under `thoth train`; Transformer mode currently uses the array model, not the TorchSharp model.
- Checkpoints have sidecar metadata at `<checkpoint>.metadata.json`; evaluation reports default to `<checkpoint>.evaluation.json`.
- The quality gate separates missing, failed, unqualified, generation-qualified, understanding-qualified, and agent-decision-qualified states.
- Phase 0 regression coverage now explicitly confirms unqualified checkpoints cannot use generation, understanding, or agent-decision roles.

## Runtime and UI

- Runtime provider modes are `self`, `neural`, and `hybrid`.
- `hybrid` falls back to `SelfContainedReasoningModel` when the checkpoint is missing or unqualified.
- Phase 0 regression coverage now confirms a missing hybrid checkpoint still returns a useful self-contained answer instead of surfacing checkpoint diagnostics to the user.
- API model status is exposed at `/api/model/status` and included in `/api/system/status`.
- Angular displays provider and checkpoint quality state in status chips and the workspace panel.

## Hardware and Local-Only Controls

- `thoth hardware inspect` now reports OS, architecture, CPU name, physical/logical cores when discoverable, RAM, disk free space, Torch CPU/CUDA availability, dtype checks, recommended CPU thread count, and writable training/checkpoint/tokenizer directories.
- Probe result on this laptop: Windows `10.0.26200`, `X64`, Intel Core i7-13620H, physical cores unavailable because `wmic` is not installed, 16 logical cores, recommended Torch CPU threads 14, 15.63 GB total RAM, 5.65 GB available RAM at probe time, 179.36 GB free on `C:\`, Torch CPU available, CUDA not available, float32/float64 CPU dtype checks passed.
- Default hardware behavior remains CPU-only. The current package is `TorchSharp-cpu`; CUDA is reported only if TorchSharp detects it.
- `.gitignore` now excludes raw/staged/normalized data, token shards, tokenizer artifacts, checkpoints, model artifacts, downloads, reports, training logs, SQLite databases, and common checkpoint extensions.

## CI

- GitHub Actions runs on `windows-latest`.
- CI restores, builds, and tests the .NET solution, then installs and builds the Angular frontend with Node 22.

## Known Limitations

1. There is no useful locally trained LLM checkpoint yet.
2. The production tokenizer does not yet meet the required v0.6 artifact layout.
3. The existing Transformer training CLI is not yet wired to the TorchSharp full-parameter model.
4. No local data source registry, license scanner, PII scanner, deduplication, dataset splits, token shards, or download approval gate exists yet.
5. Evaluation metrics are basic language-model metrics and simple scores; they do not yet prove instruction following, code quality, tool-schema success, or no-leak behavior from generated benchmark outputs.
6. The Angular build passes, but dependency audit vulnerabilities remain in the npm tree.
