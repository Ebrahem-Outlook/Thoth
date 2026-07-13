# Continuous Learning Resource Policy

Targets:

- CPU target: aggressive but bounded, no real-time priority.
- RAM floor: at least 2 GB.
- Disk floor: at least 25 GB.
- Spool maximum: configured by `--spool-max-gb`.
- Ingestion pauses under memory or disk pressure.

The previous dense Candidate 1 run fell to about 107-146 tokens/sec and pushed available RAM near the floor. Continuous mode therefore starts with a small checkpoint-safe model and can grow only after stable measurements.

