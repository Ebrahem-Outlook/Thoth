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

## Not Complete Yet

This is not a production-qualified Transformer runtime yet. Remaining Phase 6 work includes:

- full training resume command integration with scheduler/RNG state;
- KV-cache generation and cache-equivalence tests;
- top-k/top-p/repetition-penalty generation;
- mixed precision and CUDA configuration;
- full trainer/data-loader integration with gradient accumulation;
- all mandatory acceptance tests from the master prompt.

Until those gates pass, Thoth must not claim a trained or qualified Transformer checkpoint.
