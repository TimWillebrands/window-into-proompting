#!/bin/bash
# Local System One (ADR 0019): Cloudflare Clef-Flash on llama.cpp's /v1/systemone at :8091.
# Needs a recent llama.cpp (decision-model support, e.g. b11784+). LLAMA_SERVER overrides the binary.
# Decision models score the whole state in one batch, so -b/-ub must exceed the largest prompt (~4k).
# --fit keeps layers on GPU that fit; --fit-target leaves room for the big compute buffer.
set -euo pipefail
exec "${LLAMA_SERVER:-llama-server}" -hf ggml-org/Clef-Flash-GGUF:Q4_K_M \
    --host 127.0.0.1 --port "${PORT:-8091}" --no-mmproj \
    --fit on --fit-target "${FIT_TARGET_MIB:-2400}" -c 8192 -b 8192 -ub 8192
