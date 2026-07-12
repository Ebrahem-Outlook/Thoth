# Thoth Agent Guide

## Repository Map
- `src/Thoth.Core`: chat contracts, conversation orchestration, agent loop, planning, tool abstractions, and understanding contracts.
- `src/Thoth.Llm`: local fallback chat model and deterministic response generation.
- `src/Thoth.Model`, `src/Thoth.Tokenization`, `src/Thoth.Training`, `src/Thoth.Inference`: local neural bootstrap, tokenizer, training, and inference code.
- `src/Thoth.Tools`: file, memory, web, shell, and workspace tools.
- `src/Thoth.Memory`: SQLite-backed memory and conversation stores.
- `src/Thoth.Api`: ASP.NET API and Swagger surface.
- `src/Thoth.Web`: Angular chat UI.
- `tests/Thoth.Tests`: .NET regression and unit tests.

## Safe Verification Commands
- `dotnet restore Thoth.sln`
- `dotnet build Thoth.sln -c Release`
- `dotnet test Thoth.sln -c Release`
- `npm ci --prefix src/Thoth.Web`
- `npm run build --prefix src/Thoth.Web`

## Invariants
- Normal assistant messages must be concise user-facing answers or clarifications.
- Never expose internal task graphs, prompt markers, critique codes, tool logs, stop reasons, or hidden reasoning in assistant content.
- Use typed request purposes and structured payloads for internal model roles.
- Generic code generation should not run workspace tools unless the user explicitly asks to inspect files, modify the repo, execute code, or run tests.
- Do not use external LLM APIs or pretrained weights for local model behavior.
- Preserve unrelated user changes in the working tree.
