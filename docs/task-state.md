# Thoth Task State

Thoth keeps typed active task state per conversation so short follow-up messages can complete the previous request instead of being treated as unrelated chat.

## Current Task Type

`CodeGenerationTask` tracks:

- conversation and task IDs;
- status and version;
- language, artifact kind, and behavior;
- inputs, output, and validation rules;
- accepted user turns;
- missing slots.

The first implemented behavior is calculator method generation for C#, TypeScript/JavaScript, and C++.

## Persistence

`SqliteConversationTaskStore` stores:

- `conversation_tasks`
- `conversation_task_events`

The event table preserves accepted state changes without storing hidden chain-of-thought. Conversation deletion cascades task cleanup.

## Runtime Flow

`ChatOrchestrator` loads the active task before understanding the new turn. If the new message is a continuation, it merges slot values, validates readiness, and either asks for missing public details or executes a deterministic procedure.

Active task summaries exposed to the API/UI are public-safe summaries, not raw state JSON.

## Limitations

Only one active task is used by default. Multiple concurrent tasks, conflict confirmation UX, and broader task types are still future work.
