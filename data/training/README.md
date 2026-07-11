# Training data

Place licensed training and validation text under separate directories, for example:

```text
data/training/train/
data/training/validation/
```

Do not train on secrets, production databases, private customer content, or code whose license does not permit model training. The corpus loader skips `data`, `node_modules`, `bin`, `obj`, `.git`, and build-output directories when a source tree is used.
