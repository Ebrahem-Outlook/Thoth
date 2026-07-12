# Thoth Cognitive Core

The cognitive core is the deterministic control layer around Thoth's local models. Its job is to preserve task state, activate structured concepts, choose procedures, and keep unsafe or unqualified neural paths from pretending to be stronger than they are.

## Components

- `Thoth.Cognition.Tasks`: active task state, continuation resolution, merging, and clarification.
- `Thoth.Cognition.Concepts`: typed concepts, aliases, contextual relations, provenance, confidence, and activation.
- `Thoth.Cognition.Procedures`: deterministic procedure registry and code artifact generation.
- `Thoth.Memory.Cognition`: SQLite persistence for task state and concept graph data.

## What It Is Not

This layer is not consciousness, not a trained Transformer, and not a substitute for data, training, and evaluation. It is a reliable scaffold for making local model behavior less brittle while the neural stack matures.
