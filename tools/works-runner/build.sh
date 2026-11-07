#!/usr/bin/env bash
# Builds the sandbox image from a clean export of <ref>, default HEAD. Rebuild when dependencies change.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
ref=${1:-HEAD}
context=$(mktemp -d)
trap 'rm -rf "$context"' EXIT
git -C "$(git -C "$here" rev-parse --show-toplevel)" archive "$ref" | tar -x -C "$context"
mkdir -p "$context/tools/works-runner/sandbox"
cp "$here/sandbox/works-entry" "$here/sandbox/works-checks" "$context/tools/works-runner/sandbox/"
docker build -t "${WORKS_IMAGE:-works-sandbox:latest}" -f "$here/sandbox/Dockerfile" "$context"
