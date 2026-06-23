# Thoth Architecture

## Layers

```text
Thoth.Cli / Thoth.Api
  -> Thoth.Runtime
    -> Thoth.Core
    -> Thoth.Llm
    -> Thoth.Tools
    -> Thoth.Memory
    -> Thoth.Sandbox
```

## Core Concepts

- `AgentEngine` owns the run loop.
- `IAgentPlanner` creates a tool plan.
- `IChatModel` abstracts model providers.
- `IAgentTool` exposes inspectable capabilities.
- `IMemoryStore` persists useful context between runs.
- `IExecutionPolicy` decides whether a tool call is allowed.

## Run Loop

```text
AgentRequest
  -> search memory
  -> create JSON or heuristic plan
  -> authorize each tool call
  -> execute tools
  -> store run observations
  -> synthesize final answer
  -> store project memory
```

## Current Tools

- `workspace.map`: compact file tree
- `file.read`: read workspace text files
- `file.search`: search file names and file contents
- `file.write`: write workspace text files
- `memory.search`: search local memory
- `memory.write`: store local memory
- `shell.run`: run approved commands when enabled

## Safety Defaults

- Shell is disabled by default.
- File writes are limited to the workspace.
- Destructive command tokens are blocked by policy.
- Binary files are skipped by read and search tools.
- All tool execution produces structured `ToolResult` records.
