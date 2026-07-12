# Local Training Workflow

This workflow is local-only. It does not use paid infrastructure, cloud GPUs, hosted LLM APIs, pretrained weights, or pretrained tokenizers.

## Implemented Local Pipeline

1. Inspect local hardware:

```powershell
dotnet run --project src\Thoth.Cli -- hardware inspect
```

2. Plan data sources before acquisition:

```powershell
dotnet run --project src\Thoth.Cli -- data list-sources
dotnet run --project src\Thoth.Cli -- data plan-source --source arwiki
```

3. Generate owned synthetic smoke data:

```powershell
dotnet run --project src\Thoth.Cli -- data generate-owned --output data\splits\instruction\train\phase3-owned-smoke.jsonl --count 5 --seed 42
```

4. Train a tokenizer locally:

```powershell
dotnet run --project src\Thoth.Cli -- tokenizer train --data docs --output data\tokenizers\phase1-smoke --vocab-size 512 --profile smoke
```

5. Train a small smoke checkpoint:

```powershell
dotnet run --project src\Thoth.Cli -- train --data data\splits\instruction\train\phase3-owned-smoke.jsonl --checkpoint data\models\smoke-rnn.bin --epochs 1 --steps-per-epoch 3 --sequence 64 --embedding 16 --hidden 32 --lr 0.001 --fresh
```

6. Evaluate the smoke checkpoint:

```powershell
dotnet run --project src\Thoth.Cli -- evaluate --data data\splits\instruction\train\phase3-owned-smoke.jsonl --checkpoint data\models\smoke-rnn.bin --sequence 64 --max-sequences 5 --report data\reports\smoke-rnn.evaluation.json
```

7. Inspect checkpoint qualification:

```powershell
dotnet run --project src\Thoth.Cli -- model status --checkpoint data\models\smoke-rnn.bin
```

## Latest Smoke Run

- Checkpoint: `data\models\smoke-rnn.bin`
- Architecture: `legacy-recurrent-rnn-v1`
- Parameters: 14,308
- Optimizer steps: 3
- Tokens seen: 192
- Training loss: 5.5105 -> 5.5392
- Training elapsed: 00:00:00.0622834
- Evaluation tokens: 320
- Evaluation sequences: 5
- Average loss: 5.552562
- Perplexity: 257.897
- Loss health: 0.222871
- Qualification: `Unqualified`
- Reason: role-specific benchmark scores did not meet thresholds.

The raw experimental generation command completed, but the output was invalid/gibberish. That checkpoint must not be used for user-facing generation. The hybrid runtime quality gate falls back to the deterministic self-contained model for missing, experimental, unqualified, invalid, or leaking neural outputs.

## Artifact Policy

The following are intentionally ignored and must not be committed:

- `data/splits/`
- `data/tokenizers/`
- `data/models/`
- `data/reports/`
- raw uploads, raw datasets, checkpoints, SQLite files, and generated logs.
