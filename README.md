# Thoth

Thoth is a local-first AI agent platform built in .NET 8 and Angular. It has a real agent runtime, a full chat web shell, conversation storage, attachment upload, memory, tools, execution policy, CLI, and HTTP API.

## Quick Start

```powershell
dotnet build Thoth.sln
dotnet test Thoth.sln
dotnet run --project src/Thoth.Cli -- run "summarize this workspace"
```

## Web App

Run the backend:

```powershell
dotnet run --project src/Thoth.Api --urls http://127.0.0.1:5055
```

Run the Angular frontend:

```powershell
cd src/Thoth.Web
npm install
npm start
```

The frontend expects the API at `http://127.0.0.1:5055`.

## Commands

```powershell
dotnet run --project src/Thoth.Cli -- run "summarize this workspace"
dotnet run --project src/Thoth.Cli -- chat
dotnet run --project src/Thoth.Cli -- tools list
dotnet run --project src/Thoth.Cli -- memory add "User prefers Arabic progress updates" --scope user
dotnet run --project src/Thoth.Cli -- memory search "Arabic" --scope user
dotnet run --project src/Thoth.Cli -- config show
```

## API

```powershell
dotnet run --project src/Thoth.Api
```

Available endpoints:

- `GET /health`
- `GET /api/client-config`
- `GET /api/tools`
- `GET /api/conversations`
- `POST /api/conversations`
- `GET /api/conversations/{id}`
- `PATCH /api/conversations/{id}`
- `DELETE /api/conversations/{id}`
- `POST /api/chat`
- `POST /api/conversations/{id}/messages`
- `POST /api/conversations/{id}/messages/stream`
- `POST /api/attachments`
- `GET /api/attachments/{id}/download`
- `GET /api/memory/search`
- `POST /api/memory`
- `POST /runs`

## Model Configuration

Default mode is `local`: it uses Thoth's built-in deterministic fallback and does not depend on any remote model provider.

```json
{
  "Thoth": {
    "Model": {
      "Provider": "local",
      "Model": "thoth-local",
      "Endpoint": "http://localhost:11434/api/chat"
    }
  }
}
```

To use a stronger local model, run Ollama locally and set `Provider` to `ollama`, `Model` to your local model name, and `Endpoint` to `http://localhost:11434/api/chat`.
