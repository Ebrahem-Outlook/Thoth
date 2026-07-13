# First Continuous Learning Run Report

Generated from local run output on 2026-07-13.

## Verification

- `dotnet test Thoth.sln -c Release --no-restore`: passed, 138 tests.
- `npm run build --prefix src\Thoth.Web`: passed.
- First source fetch: `mdn-js-introduction` fetched from the allowlisted MDN content repository.
- First accepted token segment: accepted owned curriculum and MDN-derived mixed route after long-identifier redaction.
- First mixed replay batch: created under `data/continuous/mixer/`.
- First checkpoint: written by `TorchCheckpointDirectory`.
- Stop/resume rehearsal: succeeded with emergency checkpoint-on-stop.

## Rehearsal

- Run: `continuous-rehearsal-20260713-03`
- Stop status: `stopped`
- Accepted/rejected: `4 / 77`
- Neural/concept docs: `4 / 2`
- New/replay/consumed tokens: `10,832 / 15,146 / 3,456`
- Step: `27`
- Tokens/sec: `688.7`
- Emergency checkpoint hash: `629d9600b16b8a30b96433b8830bbdd3493f4e428bf70552540cbf3893f631f9`

## Active Continuous Run

- Run: `continuous-local-20260713`
- State: `running`
- Model: Torch decoder-only Transformer, BPE 8K, context 64, 1 layer, width 64, 4 heads, FFN 256.
- Growth: disabled until stable evaluations justify adapter expansion.
- Accepted/rejected at last sample: `2 / 22`
- Neural/concept docs: `2 / 1`
- New/replay/consumed tokens: `5,416 / 4,482 / 2,048`
- Step: `8`
- Loss: `8.7315`
- Tokens/sec: `815.0`
- RAM available: `5.32 GB`
- Free disk: `174.91 GB`
- Checkpoint hash: `9a978b140d5b42c08b1809539d486dc96a7cc7619b58ed7694b0fd88a4d0b433`

## Limitations

- Wikimedia, code repository ingestion, OASST1, and generic crawler remain disabled until their cursor/fetch paths are separately proven.
- Dense Candidate 1 remains unsafe for indefinite mode on this laptop because the prior serious run caused heavy RAM pressure.
- Adapter/MoE growth is represented in policy and config, but no adapter is activated until evaluation gates exist.
