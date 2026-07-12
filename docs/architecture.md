# Thoth Architecture

## Layers

```text
Thoth.Web / Thoth.Cli / Thoth.Api
  -> Thoth.Runtime
    -> Thoth.Core
    -> Thoth.Llm
    -> Thoth.Model / Thoth.Tokenization / Thoth.Training / Thoth.Inference
    -> Thoth.Tools
    -> Thoth.Memory
    -> Thoth.Sandbox
```

## Public Response Path

```text
HTTP request or CLI input
  -> parse request and attachments
  -> understand user intent
  -> store the user message
  -> direct reply or agent run
  -> store clean assistant content
  -> return transcript-safe response
```

The assistant message must be a concise answer or clarification. It must not contain hidden prompts, internal task graphs, critique loops, raw tool traces, or stop reasons.

## Internal Agent Path

```text
AgentRequest
  -> memory lookup
  -> plan
  -> policy check
  -> tool execution
  -> observations
  -> synthesis
  -> run memory
```

The UI can show this path in the collapsible developer panel. API responses can include structured diagnostics, but normal chat content remains clean.

## Neural Path

The runtime can use:

- `self`: self-contained useful response model.
- `hybrid`: qualified checkpoint when available, otherwise self-contained fallback.
- `neural`: checkpoint only; startup fails if the checkpoint is missing or unqualified.

Checkpoint quality is inspected through metadata and evaluation metrics before any model is trusted for generation, understanding, or agent decisions.

## Transformer Foundation

The decoder-only Transformer foundation is implemented separately from the legacy RNN. It starts from random weights, has its own checkpoint format, and is covered by lightweight CPU correctness tests. It is not claimed to be generally intelligent or production-qualified until training and evaluation artifacts pass the quality gate.

