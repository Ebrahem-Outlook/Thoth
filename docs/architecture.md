# Thoth Architecture

## Layers

```text
Thoth.Web / Thoth.Cli / Thoth.Api
  -> Thoth.Runtime
    -> Thoth.Core
    -> Thoth.Llm (internal chat contract + self-contained reasoning engine)
    -> Thoth.Tools
    -> Thoth.Memory
    -> Thoth.Sandbox
```

## Core Concepts

- `AgentEngine` owns the run loop.
- `IAgentPlanner` creates a tool plan.
- `IChatModel` is the internal chat/reasoning contract used by Thoth's self-contained engine.
- `IAgentTool` exposes inspectable capabilities.
- `IMemoryStore` persists useful context between runs.
- `IExecutionPolicy` decides whether a tool call is allowed.
- `IConversationStore` persists chats, messages, and attachments.
- `IUserUnderstandingService` classifies user intent before routing a turn.
- `ChatOrchestrator` decides whether to answer directly or run the agent/tools.

## Chat Turn Flow

```text
HTTP multipart/json request
  -> save attachments
  -> understand user intent
  -> store user message
  -> if workspace/tool task: run AgentEngine
  -> otherwise: call the self-contained reasoning engine with conversation history
  -> store assistant message
  -> return conversation, understanding, optional run trace
```

## Agent Run Loop

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
- `file.list`: list a workspace directory
- `file.info`: inspect file or directory metadata
- `file.read`: read workspace text files
- `file.search`: search file names and file contents
- `file.write`: write workspace text files
- `memory.search`: search local memory
- `memory.recent`: list recent memory
- `memory.write`: store local memory
- `http.get`: fetch text from an HTTP URL
- `shell.run`: run approved commands when enabled

## Safety Defaults

- Shell is disabled by default.
- File writes are limited to the workspace.
- Destructive command tokens are blocked by policy.
- Binary files are skipped by read and search tools.
- All tool execution produces structured `ToolResult` records.
