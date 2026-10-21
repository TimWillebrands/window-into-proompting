# Verify the Jev adapter against the real API

Status: resolved

`JevJudge` was written from TypeSafe's docs; there was no key to test it with. Set `TYPESAFE_API_KEY`, run the world, and check:

- the logs show `Capture gate p(memorable)=…` lines;
- Life choices look sane.

Choice keys are the option numbers ("0".."10").

## Answer

Verified live through OpenRouter (`POST https://openrouter.ai/api/v1/systemone`, model `~typesafe/jev-latest` → `typesafe/jev-1.14`). The wire format matches the adapter (`answers.*.probabilities` / `noul`). Each answer takes about 0.5 s and costs about $0.00002.

The adapter is now generic (`HttpSystemOneJudge`), and a local Cloudflare Clef-Flash on llama.cpp (`tools/systemone/run-clef.sh`) is tried first. A 2.8k-token decision takes ~1.3 s on an RTX 4070 that is sharing VRAM. Jev on OpenRouter is the fallback.
