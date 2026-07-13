# Model Growth

Initial continuous mode keeps model growth disabled by default.

Planned growth path:

1. Establish stable dense checkpoint.
2. Add one domain adapter at a time.
3. Warm up only the new adapter.
4. Run held-out evaluation.
5. Promote only when global and domain scores do not regress.

Initial adapter domains are Arabic, English/general, C#/.NET, TypeScript/Angular, C++, dialogue/task-state, and tools/procedures.

