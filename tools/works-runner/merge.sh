#!/usr/bin/env bash
# Applies Proposition <n> to the current checkout with its Architect as author. The Runner notices once it reaches the branch it mirrors.
set -euo pipefail
n=${1:?usage: merge.sh <proposition number>}
curl -fsS -H "Authorization: Bearer ${WORKS_TOKEN:?}" "${WORKS_RUNNER_URL:-http://127.0.0.1:8787}/propositions/$n/patch" | git am
