# Training data

The default training root is this directory: `data/training`.

Place licensed training, validation, and test text under separate directories:

```text
data/training/pretrain/
data/training/validation/
data/training/test/
data/training/instructions/
```

Instruction/chat examples use JSONL:

```json
{"id":"ar-ts-001","language":"ar","task":"clarification","messages":[{"role":"user","content":"ممكن تبني method Type script"},{"role":"assistant","content":"أكيد. الميثود في TypeScript مطلوب منها تعمل إيه؟ ابعت المدخلات والناتج المتوقع وأي قواعد validation مهمة."}]}
```

Do not train on secrets, production databases, private customer content, generated uploads, checkpoints, SQLite files, or code whose license does not permit model training. The corpus loader records a deterministic manifest with file hashes and token counts.
