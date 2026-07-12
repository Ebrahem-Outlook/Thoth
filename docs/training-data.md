# Training Data

Training data must be local, licensed, provenance-tracked, and scanned before it can be used for Thoth training. Raw data, normalized corpora, token shards, checkpoints, local SQLite databases, and logs are ignored by Git.

## Dataset Layout

```text
data/
  raw/
  staging/
  normalized/
  deduplicated/
  splits/
    pretrain/train/
    pretrain/validation/
    pretrain/test/
    instruction/train/
    instruction/validation/
    instruction/test/
  tokenized/
  tokenizers/
  models/
  evaluation/
  manifests/
```

Only small governance metadata under `data/manifests` is versioned by default.

## Required Manifests

```text
data/manifests/sources.json
data/manifests/documents.jsonl
data/manifests/licenses.json
data/manifests/dataset-build.json
data/manifests/exclusions.jsonl
data/manifests/attribution.md
```

Initialize missing files with:

```powershell
dotnet run --project src/Thoth.Cli -- data init-manifests
```

Every document record must include source ID, source URL, revision, license, language, content kind, relative path, byte count, SHA-256, split, and safety flags.

## License Policy

Allowed by default for the first local experiment:

```text
MIT
Apache-2.0
BSD-2-Clause
BSD-3-Clause
ISC
CC0-1.0
Unlicense
```

Share-alike content such as `CC-BY-SA-*` requires separate labeled handling and attribution. Missing, custom, proprietary, unknown, `NOASSERTION`, GPL, LGPL, and AGPL licenses are rejected by default until explicitly reviewed.

This is an engineering policy, not legal advice.

## Safety Scanning

`Thoth.Data` includes a local scanner for likely API keys, private keys, passwords, connection strings, high-entropy secrets, email addresses, phone numbers, and IP addresses. Findings are redacted; secret values must not be logged into manifests.

## Acquisition Planning

List source plans:

```powershell
dotnet run --project src/Thoth.Cli -- data list-sources
```

Inspect one source without downloading it:

```powershell
dotnet run --project src/Thoth.Cli -- data plan-source --source arwiki
```

The current catalog covers `arwiki`, `simplewiki`, `mdn-content`, `oasst1`, `curated-code`, and `owned-synthetic`. External sources are plan-only until the approval gate prints source URL, license, remote size, expected disk use, and current free disk space.

Generate a small owned local instruction set:

```powershell
dotnet run --project src/Thoth.Cli -- data generate-owned --count 100
```

Generated owned examples are deterministic and include verifier metadata. The default output path is under ignored `data/splits`.

## Instruction JSONL

Each line:

```json
{
  "id": "example-001",
  "language": "en",
  "task": "coding",
  "messages": [
    { "role": "user", "content": "Build a C# calculator method." },
    { "role": "assistant", "content": "public static decimal Add(decimal a, decimal b) => a + b;" }
  ]
}
```

The loader requires an id, language, task, one user message, and one assistant message.

## Checkpoint Corpus Manifest

`CorpusLoader` writes a manifest next to checkpoints. It records source files, byte counts, character counts, token counts, partitions, and hashes. This manifest is part of checkpoint quality metadata.

## Licensing Warning

Do not train on private, copyrighted, customer, production, or secret material unless you have explicit permission. Do not include API keys, credentials, tokens, or personal data in datasets.
