# Neural Bootstrap Model

## What is implemented

The bootstrap model is a dependency-free recurrent next-token language model written with plain C# arrays. It contains real trainable parameters:

- token embeddings;
- input-to-hidden weights;
- recurrent hidden-to-hidden weights;
- hidden and output biases;
- hidden-to-vocabulary projection;
- Adam first and second moments.

Training computes cross-entropy loss and exact gradients through the complete sequence. Checkpoints include weights and optimizer state, so training can resume.

## Why a recurrent model first

A small RNN is not the final Thoth architecture. It is the smallest model that validates the complete engineering chain without hiding the important parts behind a pretrained model or external API:

```text
corpus -> tokens -> random parameters -> loss -> gradients -> optimizer
       -> checkpoint -> reload -> inference -> evaluation
```

This vertical slice must be trustworthy before adding attention, distributed GPU execution, mixed precision, or billions of parameters.

## Next model: decoder-only Transformer

The next model project should implement:

- learned token embeddings;
- RMSNorm;
- causal multi-head self-attention;
- rotary position embeddings;
- SwiGLU feed-forward blocks;
- residual connections;
- tied output embeddings;
- KV cache for generation;
- mixed-precision GPU tensors;
- gradient accumulation and distributed checkpoints.

The existing `ITextTokenizer`, training CLI, checkpoint lifecycle, `IChatModel`, evaluation commands, and agent loop should remain stable while the model implementation changes.

## Correct validation order

1. Tokenizer round-trip tests.
2. Forward output shape and finite-value tests.
3. Overfit one tiny sequence until loss falls sharply.
4. Checkpoint round-trip produces identical logits.
5. Held-out perplexity decreases.
6. Instruction-format fine-tuning.
7. Structured tool-decision accuracy.
8. Coding benchmarks with executable tests.

Do not judge intelligence from a few generated sentences before stages 1-5 pass.
