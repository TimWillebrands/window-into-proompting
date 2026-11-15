#!/bin/bash
set -euo pipefail
exec "${LLAMA_SERVER:-llama-server}" -hf ggml-org/Clef-Flash-GGUF:Q4_K_M \
    --host 127.0.0.1 --port "${PORT:-8091}" --no-mmproj \
    --fit on --fit-target "${FIT_TARGET_MIB:-2400}" -c 8192 -b 8192 -ub 8192
