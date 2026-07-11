# Thoth Roadmap

## Phase 1: Runtime Foundation

- Structured .NET solution
- Agent run loop
- Tool registry
- SQLite memory
- Self-contained reasoning engine
- Arabic/English intent routing
- CLI and minimal API
- Safety policy
- Initial tests

Status: implemented.

## Phase 2: Product Chat Surface

- Angular full chat shell
- Conversation history
- Attachments and image uploads
- Tool trace inspector
- Memory panel
- Settings panel
- Backend conversation APIs
- Understanding/routing layer

Status: implemented.

## Phase 3: Smarter Agent Loop

- Event stream for thinking, tool calls, observations, and final answer
- Single durable run state model
- Conversation/run replay
- Human approval gates for risky tools
- Streaming from internal runtime events
- Tool result summarization
- Multi-step self-evaluation
- Retry policies
- Run trace files under `data/runs`
- Better JSON schema validation for plans

## Phase 4: Code Intelligence

- Symbol index
- Dependency graph
- Project-aware context builder
- Test runner tool with structured parsing
- Patch planning and review loop

## Phase 5: Memory Upgrade

- Workspace and memory indexing abstraction
- Vector search
- Memory compaction
- User preference memory
- Project decision log

## Phase 6: Dashboard Depth

- Approval queue
- Configuration editor
- Run trace persistence
- Project switcher

## Phase 7: Agent Teams

- Specialized agents for coding, research, review, and planning
- Task delegation
- Shared run context
- Cross-agent memory
