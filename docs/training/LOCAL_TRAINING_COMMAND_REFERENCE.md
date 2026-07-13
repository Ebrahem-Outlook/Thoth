# Local Training Command Reference

Captured from the current CLI on 2026-07-13.

## Environment

- CLI project: `src\Thoth.Cli`
- Branch: `master`
- .NET SDK: `9.0.302`
- Node: `v22.17.0`
- npm: `10.9.2`

## Baseline Checks

```powershell
dotnet test Thoth.sln -c Release
npm run build --prefix src/Thoth.Web
```

Observed baseline:

- .NET tests: passed, `138/138`.
- Angular build: passed.

## Model Command

```powershell
dotnet run --project src\Thoth.Cli -c Release --no-build -- model --help
```

```text
Usage:
  thoth model train [existing thoth train options]
  thoth model generate --prompt "text" [existing thoth generate options]
  thoth model evaluate [existing thoth evaluate options]
  thoth model status|qualify [--checkpoint path]
  thoth model benchmark [--profile smoke-cpu|laptop-pilot|candidate-1|candidate-2] [--steps n] [--train-steps n] [--sequence n] [--json]
  thoth model learning-proof --data path [--run-dir path] [--steps n] [--context n] [--resume-checkpoint model.bin]
```

## Train

```powershell
dotnet run --project src\Thoth.Cli -c Release --no-build -- model train --help
```

```text
Usage:
  thoth model train --data path [--checkpoint path] [--epochs n] [--steps-per-epoch n]
                    [--sequence n] [--embedding n] [--hidden n] [--lr value] [--fresh]
  thoth model train --architecture transformer --tokenizer path --data path
                    [--checkpoint path] [--preset tiny|bootstrap]
                    [--layers n] [--width n] [--heads n] [--ffn n] [--batch-size n]
```

## Learning Proof

```powershell
dotnet run --project src\Thoth.Cli -c Release --no-build -- model learning-proof `
  --data data\splits\instruction\train\learning-proof-owned.jsonl `
  --run-id learning-proof-001 `
  --steps 20 `
  --context 128 `
  --layers 2 `
  --width 128 `
  --heads 4 `
  --ffn 512
```

The command trains a small TorchSharp Transformer, saves a checkpoint, reloads it, resumes from that checkpoint, writes `learning-proof.json`, and emits a fixed prompt sample.

## Benchmark

```powershell
dotnet run --project src\Thoth.Cli -c Release --no-build -- model benchmark --help
```

```text
Usage:
  thoth model benchmark [--profile smoke-cpu|laptop-pilot|candidate-1|candidate-2] [--vocab-size n]
                        [--steps n] [--train-steps n] [--sequence n] [--seed n] [--json]
```

Profiles:

- `smoke-cpu`: tiny local correctness profile.
- `laptop-pilot`: existing pilot profile.
- `candidate-1`: throughput-oriented serious candidate, 4 layers, width 256, 8 heads, FFN 1024, context 256, vocab 8000.
- `candidate-2`: capacity-oriented serious candidate, 6 layers, width 320, 8 heads, FFN 1280, context 256, vocab 8000.

## Evaluate

```powershell
dotnet run --project src\Thoth.Cli -c Release --no-build -- model evaluate --help
```

```text
Usage:
  thoth model evaluate [--architecture transformer] [--tokenizer path] [--data path]
                       [--checkpoint path] [--sequence n] [--max-sequences n] [--report path]
```

## Generate

```powershell
dotnet run --project src\Thoth.Cli -c Release --no-build -- model generate --help
```

```text
Usage:
  thoth model generate --prompt "text" [--architecture transformer] [--tokenizer path]
                       [--checkpoint path] [--tokens n] [--temperature value]
                       [--top-k n] [--top-p value] [--seed n] [--experimental]
```

## Status / Qualify

```powershell
dotnet run --project src\Thoth.Cli -c Release --no-build -- model status --help
```

```text
Usage:
  thoth model status [--checkpoint path]
  thoth model qualify [--checkpoint path]
```

## Hardware

```powershell
dotnet run --project src\Thoth.Cli -c Release --no-build -- hardware inspect --json
```

Observed hardware snapshot:

- CPU: `13th Gen Intel(R) Core(TM) i7-13620H`
- Logical cores: `16`
- RAM: `16,786,583,552` bytes total
- Available RAM during capture: about `2,074,087,424` bytes
- Disk free on `C:\`: about `188,454,301,696` bytes
- Torch backend: CPU
- CUDA available: `false`

## Artifact Policy

Do not commit generated data, tokenizers, shards, checkpoints, reports, logs, run state, or uploads. Keep only code, docs, scripts, schemas, examples, and manifests in Git.
