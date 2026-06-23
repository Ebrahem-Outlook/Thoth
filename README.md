# Thoth

Thoth is a local-first AI agent platform built in .NET 8. It is structured as a real agent runtime, not a single chat wrapper: it has a planning loop, tool registry, memory store, execution policy, CLI, and minimal HTTP API.

## Quick Start

```powershell
dotnet build Thoth.sln
dotnet test Thoth.sln
dotnet run --project src/Thoth.Cli -- run "summarize this workspace"
```

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
- `GET /tools`
- `POST /runs`
- `GET /memory/search?query=...`

## Model Configuration

Default mode is local and deterministic:

```json
{
  "Thoth": {
    "Model": {
      "Provider": "local",
      "Model": "thoth-local"
    }
  }
}
```

To use an OpenAI-compatible chat completions endpoint, update `configs/thoth.json`:

```json
{
  "Thoth": {
    "Model": {
      "Provider": "openai-compatible",
      "Model": "gpt-4.1-mini",
      "Endpoint": "https://api.openai.com/v1/chat/completions",
      "ApiKeyEnvironmentVariable": "OPENAI_API_KEY"
    }
  }
}
```

Then set the environment variable before running Thoth.
