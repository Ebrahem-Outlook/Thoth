# Thoth Agent Guide

## Repository Map

- `src/Thoth.Core`: chat contracts, conversation orchestration, agent loop, planning, tools, memory contracts, and user understanding.
- `src/Thoth.Data`: local dataset provenance records, license policy, manifest skeletons, and safety scanners.
- `src/Thoth.Llm`: self-contained useful response model and deterministic fallback behavior.
- `src/Thoth.Model`: legacy RNN, decoder-only Transformer foundation, checkpoint formats, and quality gates.
- `src/Thoth.Tokenization`: UTF-8 byte tokenizer and deterministic byte-level BPE tokenizer.
- `src/Thoth.Training`: corpus loading, instruction dataset loading, RNN trainer, and Transformer trainer.
- `src/Thoth.Inference`: text generation wrappers for local neural models.
- `src/Thoth.Evaluation`: perplexity and user-value evaluation reports used by quality gates.
- `src/Thoth.Tools`: file, memory, web, HTTP, shell, and workspace tools.
- `src/Thoth.Memory`: SQLite-backed memory and conversation stores.
- `src/Thoth.Api`: ASP.NET API and Swagger surface.
- `src/Thoth.Web`: Angular chat UI.
- `tests/Thoth.Tests`: .NET regression, agent, tool, tokenizer, checkpoint, and CPU Transformer tests.

## Coding Conventions

- Keep user-facing assistant messages clean: no internal plans, hidden prompts, tool logs, critique notes, or stop reasons.
- Put diagnostics in structured metadata, API diagnostics, or the developer panel only.
- Prefer typed request purposes and structured model inputs over substring routing.
- Preserve legacy RNN behavior while Transformer checkpoints are still being qualified.
- Never use pretrained weights or external LLM APIs for local model behavior.
- Preserve unrelated user changes in the working tree.

## Safe Commands

```powershell
dotnet restore Thoth.sln
dotnet build Thoth.sln -c Release
dotnet test Thoth.sln -c Release
npm ci --prefix src/Thoth.Web
npm run build --prefix src/Thoth.Web
dotnet run --project src/Thoth.Cli -- model-status
```

## No-Internal-Leak Invariant

The transcript is for the user. It may contain the user's messages and the final assistant answer or clarification. Agent steps, tool observations, model status, checkpoint reasons, and diagnostics belong in the right-side developer panel, API diagnostics, logs, or CLI output. Do not mix hidden reasoning into normal assistant content.
