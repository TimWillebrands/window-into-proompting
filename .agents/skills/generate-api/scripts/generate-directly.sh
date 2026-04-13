#!/usr/bin/env bash
# Generate using npm directly (host machine, not via Docker).
# Requires Node + npm installed locally and backend running at localhost:8080.
set -e

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT/frontend"
npm run api-generate
