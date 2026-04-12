#!/usr/bin/env bash
set -e

docker ps -q -f name=dev-frontend | grep -q . || { echo "dev-frontend container not running. Start with: docker compose up"; exit 1; }

docker exec dev-frontend npm run api-generate
