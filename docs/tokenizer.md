# Thoth Tokenizer

Thoth v0.6 uses a deterministic UTF-8 byte-level BPE tokenizer trained locally from approved corpus files. It does not use pretrained tokenizers.

## Profiles

| Profile | Target vocabulary |
| --- | ---: |
| `smoke` | 2,048 |
| `laptop-pilot` | 8,000 |
| `laptop-max` | 12,000 |

Use `--vocab-size` only to override a profile for a small local experiment.

## Special Tokens

Stable IDs:

| ID | Token |
| ---: | --- |
| 0 | `<PAD>` |
| 1 | `<BOS>` |
| 2 | `<EOS>` |
| 3 | `<USER>` |
| 4 | `<ASSISTANT>` |
| 5 | `<SYSTEM>` |
| 6 | `<TOOL_CALL>` |
| 7 | `<TOOL_RESULT>` |
| 8 | `<END_TURN>` |

There is no `<UNK>` token in the default tokenizer. Unknown text falls back to UTF-8 bytes so Arabic, English, C#, TypeScript, C++, punctuation, emoji, and unusual identifiers can round-trip.

## Artifact Layout

```text
data/tokenizers/<tokenizer-name>/
|-- tokenizer.json
|-- vocab.json
|-- merges.txt
|-- tokenizer-metadata.json
`-- training-manifest.sha256
```

`tokenizer.json` records hashes for the sidecar files. `LoadAsync` verifies those hashes and rejects corrupted artifacts.

## CLI

```powershell
dotnet run --project src/Thoth.Cli -- tokenizer train `
  --data data/splits/pretrain/train `
  --output data/tokenizers/thoth-bpe `
  --profile laptop-pilot `
  --min-frequency 2
```

Use `--normalize-nfc` only when the dataset build explicitly chooses Unicode NFC normalization. The default preserves text bytes exactly.
