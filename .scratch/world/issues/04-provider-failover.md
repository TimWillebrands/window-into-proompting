# LLM provider failover

Status: resolved

The router picks the least-pressured enabled provider and never fails over. When the OpenRouter key runs out or expires, every call errors until someone switches providers by hand. Wanted: on a provider error, retry once on the next compatible provider, e.g. a local Ollama entry.

## Answer

Done in 69268a3: endpoints report 401/402/403/404/unreachable to the router, which benches the provider for 10 min; `LlmProviderEntry.Priority` (lower first) lets a local Ollama entry sit behind OpenRouter as fallback.
