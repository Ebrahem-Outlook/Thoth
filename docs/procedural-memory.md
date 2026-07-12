# Thoth Procedural Memory

Procedural memory contains deterministic skills that should not live as ad-hoc fallback `if` blocks.

## Current Registry

`ProcedureRegistry` can execute:

- `calculator.method.v1`

The calculator procedure renders C#, TypeScript/JavaScript, or C++ code from a completed `CodeGenerationTask`.

## Verification State

Generated artifacts include validation notes and user-visible guards such as division-by-zero handling. Compiler-backed verification is not fully wired yet; the next step is controlled temporary compilation for C#, TypeScript, and C++ when local compilers are available.

## Boundary

Procedures are deterministic engineering skills. They are not a general language model and they do not make Thoth intelligent by themselves.
