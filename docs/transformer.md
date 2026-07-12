# Thoth Transformer

Thoth has two Transformer-related implementations:

- `TransformerLanguageModel`: the earlier deterministic array implementation. It remains useful for educational/reference CPU checks, but it must not be described as the real full-parameter training architecture.
- `TorchTransformerLanguageModel`: the TorchSharp CPU autograd implementation introduced for true trainable parameter updates.

## TorchSharp Core

The TorchSharp path currently includes:

- token embeddings;
- pre-norm RMSNorm;
- multi-head causal attention;
- RoPE;
- SwiGLU feed-forward layers;
- final RMSNorm;
- LM head, with optional tied output embeddings;
- cross-entropy with padding ignored;
- dropout path when configured;
- manual AdamW update over TorchSharp gradients;
- CPU profiles for smoke and laptop-pilot sizing;
- Torch parameter and Adam-moment checkpoint round-trip;
- CPU correctness tests for finite gradients, parameter updates, shape, causal isolation, padding masking, determinism, loss reduction, profile parameter counts, next-token logits, tied embeddings, and checkpoint round-trip.

The pinned dependency is `TorchSharp-cpu` `0.107.0`.

## Local Trainer

`TorchTransformerTrainer` provides a small local training loop over token windows with gradient accumulation, warmup/cosine learning-rate scheduling, finite-loss checks, JSONL logs, tokens/sec reporting, and atomic checkpoint directories. It is intended for smoke and pilot runs only until the full curriculum/evaluation gates are complete.

## Torch Generation

`TorchTransformerTextGenerator` supports greedy or seeded sampling, top-k/top-p, repetition penalty, stop tokens/sequences, cancellation, and a streaming token callback. `TorchTransformerGenerationCache` currently stores token history for deterministic replay/truncation; it is a cache contract, not yet an optimized per-layer KV tensor cache.

## Hybrid Runtime

The runtime can load qualified legacy RNN, C# Transformer, and Torch Transformer checkpoints through the checkpoint quality gate. It falls back to the deterministic self-contained model when a checkpoint is missing, experimental, unqualified, fails to load, emits empty/degenerate text, or leaks internal diagnostics. `/api/system/status` and `/api/model/status` expose safe metadata such as architecture, checkpoint hash, tokenizer, dataset hash, passed/failed scores, training step, parameter count, evaluation timestamp, and inference device.

## Not Complete Yet

This is not a production-qualified Transformer runtime yet. Remaining Phase 6 work includes:

- full training resume command integration with scheduler/RNG state;
- optimized per-layer KV tensor cache and cache-equivalence benchmarks;
- top-k/top-p/repetition-penalty generation;
- mixed precision and CUDA configuration;
- full trainer/data-loader integration with gradient accumulation;
- all mandatory acceptance tests from the master prompt.

Until those gates pass, Thoth must not claim a trained or qualified Transformer checkpoint.
