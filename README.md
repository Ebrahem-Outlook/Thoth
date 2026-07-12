# Thoth

Thoth is a local-first AI agent platform built with .NET 8 and Angular 21. It includes a chat web app, HTTP API, Swagger, conversation storage, attachments, memory, local tools, a safe execution policy, CLI commands, a useful self-contained response core, and a trainable neural foundation.

Thoth does not call an external LLM by default. The local neural checkpoints start from random weights and must pass quality gates before they are trusted for user-facing generation.

## Requirements

- .NET SDK 8.x
- Node.js compatible with Angular 21
- npm 10.x or newer

## Quick Start

```powershell
dotnet restore Thoth.sln
dotnet build Thoth.sln
dotnet test Thoth.sln
```

Run the backend:

```powershell
dotnet run --project src/Thoth.Api --urls http://127.0.0.1:5055
```

Run the Angular frontend:

```powershell
npm ci --prefix src/Thoth.Web
npm start --prefix src/Thoth.Web
```

The frontend API URL is configured in `src/Thoth.Web/src/environments/environment.ts`. The checked-in default points to `http://127.0.0.1:5055`; local overrides should stay local unless intentionally committed.

## CLI

```powershell
dotnet run --project src/Thoth.Cli -- run "summarize this workspace"
dotnet run --project src/Thoth.Cli -- chat
dotnet run --project src/Thoth.Cli -- tools list
dotnet run --project src/Thoth.Cli -- model-status
```

Tokenizer and Transformer foundation:

```powershell
dotnet run --project src/Thoth.Cli -- tokenizer train --data data/training --output data/tokenizers/thoth-bpe --vocab-size 8000
dotnet run --project src/Thoth.Cli -- train --architecture transformer --tokenizer data/tokenizers/thoth-bpe --data data/training --checkpoint data/models/thoth-transformer.bin
dotnet run --project src/Thoth.Cli -- evaluate --architecture transformer --tokenizer data/tokenizers/thoth-bpe --data data/training/validation --checkpoint data/models/thoth-transformer.bin
dotnet run --project src/Thoth.Cli -- generate --architecture transformer --tokenizer data/tokenizers/thoth-bpe --checkpoint data/models/thoth-transformer.bin --experimental "User: hello`nAssistant:"
```

`--experimental` is required for generation from an unqualified checkpoint. A checkpoint becomes trusted only after evaluation metadata satisfies the quality gate.

## API

Swagger is available at `/swagger` when `Thoth.Api` is running.

Important endpoints:

- `GET /health`
- `GET /api/client-config`
- `GET /api/system/status`
- `GET /api/model/status`
- `GET /api/workspace/summary`
- `GET /api/tools`
- `GET /api/conversations`
- `POST /api/chat`
- `POST /api/conversations/{id}/messages`
- `POST /api/attachments`
- `GET /api/memory/search`
- `POST /runs`

## Verification

```powershell
dotnet restore Thoth.sln
dotnet build Thoth.sln -c Release --no-restore
dotnet test Thoth.sln -c Release --no-build
npm ci --prefix src/Thoth.Web
npm run build --prefix src/Thoth.Web
```

The normal .NET test suite includes lightweight CPU Transformer correctness tests. Expensive training experiments should be run separately.

