# Model Selection Report

Captured on 2026-07-13. All measurements were local CPU-only. No remote GPUs, cloud APIs, paid services, pretrained weights, or pretrained tokenizers were used.

## Hardware Snapshot

- CPU: 13th Gen Intel(R) Core(TM) i7-13620H
- Physical cores: 10
- Logical cores: 16
- RAM: 16,786,583,552 bytes
- Available RAM during hardware script: 1,151,787,008 bytes
- Disk free on C: 188,402,909,184 bytes
- Torch backend: CPU
- CUDA available: false

## Short Learning Proof

- Run ID: `learning-proof-20260713-02`
- Corpus: owned deterministic synthetic instruction data
- Documents/examples: 400
- Corpus tokens used: 200,000 byte tokens
- Model: Torch Transformer, 2 layers, width 128, 4 heads, FFN 512, context 128
- Parameters: 558,208
- Steps: 20 optimizer steps
- Checkpoint/reload/resume: saved at step 10, resumed from step 10, completed step 20
- Loss: 5.5505 -> 4.7288
- Resume throughput: 2,283.9 tokens/sec
- Latest checkpoint: `data/runs/learning-proof-20260713-02/checkpoints/step-00000020`
- Sample status: generated text is still gibberish; this proves training mechanics, not useful language quality.

## Candidate 1: Throughput Oriented

- Profile: `candidate-1-throughput`
- Vocab: 8,000
- Context: 256
- Layers: 4
- Width: 256
- Heads: 8
- FFN: 1,024
- Exact parameters: 6,244,608
- Forward throughput: 7,924.55 tokens/sec
- Training throughput: 1,369.42 tokens/sec
- Step duration: 186.94 ms
- Last one-step training loss: 9.0567
- Checkpoint size: 74,937,048 bytes
- Save duration: 369.55 ms
- Load duration: 410.35 ms
- Available RAM before: 3,047,624,704 bytes
- Available RAM after: 1,803,743,232 bytes
- Projected 10M consumed tokens: 2.03 hours
- Projected 30M consumed tokens: 6.09 hours
- Projected 60M consumed tokens: 12.17 hours

## Candidate 2: Capacity Oriented

- Profile: `candidate-2-capacity`
- Vocab: 8,000
- Context: 256
- Layers: 6
- Width: 320
- Heads: 8
- FFN: 1,280
- Exact parameters: 12,394,560
- Forward throughput: 4,749.06 tokens/sec
- Training throughput: 568.46 tokens/sec
- Step duration: 450.34 ms
- Last one-step training loss: 9.0315
- Checkpoint size: 148,737,264 bytes
- Save duration: 964.27 ms
- Load duration: 796.75 ms
- Available RAM before: 2,884,182,016 bytes
- Available RAM after: 963,715,072 bytes
- Projected 10M consumed tokens: 4.89 hours
- Projected 30M consumed tokens: 14.66 hours
- Projected 60M consumed tokens: 29.32 hours

## Selection

Selected: `candidate-1-throughput`.

Reason:

- Candidate 2 leaves less than the required 2GB RAM headroom after the benchmark.
- Candidate 2 reaches only about 41.5% of Candidate 1 training tokens/sec, below the 60% rule.
- Candidate 1 is still projected slightly above two hours for 10M consumed tokens, so it requires explicit user approval before launch.

## Approval Report For A >2 Hour Run

Selected model: `candidate-1-throughput`
Exact parameters: `6,244,608`
Tokenizer vocabulary: `8,000` target for serious run; proof used byte tokenizer `260`
Context: `256`
Unique training tokens: not built yet for serious mixed corpus; proof corpus used `200,000` byte tokens
Planned consumed tokens: `10,000,000` initial serious preview target
Estimated epochs: depends on final unique-token corpus size
Measured tokens/sec: `1,369.42`
Estimated duration: `2.03 hours` for 10M consumed tokens
Peak RAM: measured available RAM after Candidate 1 was `1,803,743,232` bytes; this is below the preferred 2GB headroom and should be improved by closing other apps before launch
Free disk before start: `188,402,909,184` bytes
Estimated disk after run: checkpoint size is about `74,937,048` bytes each; latest-3 plus best-2 is about `375MB` before logs/samples
Validation interval: proposed `1,000` optimizer steps
Checkpoint interval: proposed `500` optimizer steps
Run directory: `data/runs/serious-candidate1-001`
Start command: requires explicit approval before running a >2 hour job
Status command: `scripts/training/Get-ThothTrainingStatus.ps1 -RunId serious-candidate1-001`
Stop command: `scripts/training/Stop-ThothTraining.ps1 -RunId serious-candidate1-001`
Resume command: `scripts/training/Resume-ThothTraining.ps1 -RunId serious-candidate1-001`

Long training was not launched because this approval report is required first.
