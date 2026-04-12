---
name: generate-api
description: Use Orval to (re-)generate the typescript api bindings to the dotnet backend.
dependencies: bash
---

# Api binding guidelines
We use Orval to generate bindings to the backend's api for the frontend. This way the model is always usable in the frontend without having to write brittle types/interfaces in the frontend project. And we have a single method of calling the api functions so less written code.

## When to apply
Regenerate when any of the following change:
- C# controllers (`backend/Controllers/**/*.cs`)
- C# API models / DTOs (`backend/**/*.cs` public classes referenced by controllers)

## How to run

Running [scripts/generate-from-docker.sh](scripts/generate-from-docker.sh) is the preferred method — dev containers are usually running and this requires no path escaping.

## Files modified
Generated output lands in `frontend/src/api/`. After running, verify the diff looks correct before committing.
