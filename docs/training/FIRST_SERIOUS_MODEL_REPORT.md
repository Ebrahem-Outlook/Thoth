# First Serious Model Report

Status: deferred before launch.

The repository is now prepared for local supervised training, but the first serious >2 hour run was not launched because the approval report is required first.

## Completed Evidence

- Repository inspection completed.
- CLI help discovery completed after fixing model subcommand help routing.
- Baseline .NET tests passed: `138/138`.
- Angular production build passed.
- Hardware profile script created and executed.
- Source plans inspected.
- Owned deterministic learning-proof corpus generated locally.
- Short Torch learning proof completed.
- Checkpoint save/reload/resume proved inside the learning proof.
- Two serious candidates benchmarked locally.
- Runtime quality gates remain enabled.
- No checkpoint was promoted.

## Data

- Sources used for proof: owned deterministic synthetic examples only.
- License: `Thoth-owned`.
- Documents/examples: 400.
- Bytes: 261,806.
- Tokens used in proof: 200,000 byte tokens.
- External downloads: none.
- Rejections: not measured in this short proof because the generated owned corpus was already deterministic and local.

## Tokenizer

- Proof tokenizer: byte tokenizer.
- Vocabulary: 260.
- Reason: lossless UTF-8 and already wired into the runtime/trainer path.
- Serious-run tokenizer: not finalized; target remains 8,000 BPE after a larger clean corpus exists.

## Training Proof

- Run ID: `learning-proof-20260713-02`.
- Device: CPU.
- Parameters: 558,208.
- Context: 128.
- Steps: 20.
- Tokens consumed: 2,560 training-window tokens across the proof steps; corpus loaded/truncated to 200,000 tokens.
- Loss: 5.5505 -> 4.7288.
- Resume: checkpoint saved at step 10, loaded, then continued to step 20.
- Best checkpoint: `data/runs/learning-proof-20260713-02/checkpoints/step-00000020`.
- Generated sample: present but gibberish; not useful or qualified.

## Model Selection

- Candidate 1: 6,244,608 params, 1,369.42 training tokens/sec.
- Candidate 2: 12,394,560 params, 568.46 training tokens/sec.
- Selected: Candidate 1.
- Reason: Candidate 2 fails RAM headroom and throughput criteria.

## Evaluation And Qualification

- Final fixed-case evaluation was not run because no serious checkpoint exists yet.
- Qualification: no neural checkpoint qualified.
- Runtime fallback: retained.

## Deferral Reason

The selected Candidate 1 serious run is projected at 2.03 hours for 10M consumed tokens. Per the supervisor rules, it cannot start until the user approves the exact report in `MODEL_SELECTION_REPORT.md`.
