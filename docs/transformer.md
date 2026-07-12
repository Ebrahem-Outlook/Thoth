# Decoder-Only Transformer Foundation

Thoth includes a from-random-weights decoder-only Transformer foundation. It is implemented for correctness and local experimentation, not as a claim of frontier-level intelligence.

## Implemented

- learned token embeddings;
- RMSNorm;
- multi-head causal self-attention;
- RoPE rotation;
- SwiGLU feed-forward blocks;
- residual connections;
- causal masking;
- stable seeded initialization;
- next-token cross-entropy evaluation;
- AdamW updates for the language modeling head;
- checkpoint save/load including optimizer moments;
- deterministic CPU tests for shape, causality, training loss reduction, checkpoint round-trip, resume, generation bounds, tokenizer round-trip, and non-finite failure handling.

## Configurations

- Tiny: 2 layers, width 128, 4 heads, context 128.
- Bootstrap: 8 layers, width 512, 8 heads, FFN 2048, context 1024.

Tests use smaller CPU-only configs so CI stays fast.

## Limitations

The current trainer updates the LM head and bias while the Transformer stack participates in forward inference. This is enough for CPU correctness and checkpoint lifecycle tests, but it is not full end-to-end Transformer pretraining. Full backpropagation through attention and FFN weights is a future training milestone.

Do not describe a checkpoint as capable or generally intelligent until it has been trained on appropriate data and has passed the quality gate.

