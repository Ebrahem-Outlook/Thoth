#!/usr/bin/env bash
set -euo pipefail

dotnet restore Thoth.sln
dotnet build Thoth.sln -c Release
dotnet test Thoth.sln -c Release

dotnet run --project src/Thoth.Cli -c Release -- train \
  --data . \
  --epochs 3 \
  --steps-per-epoch 500 \
  --sequence 128 \
  --checkpoint data/models/thoth-bootstrap.bin
