# LLM provider failover

Status: ready-for-agent

The router picks the least-pressured enabled provider and never fails over. When the OpenRouter key runs out or expires, every call errors until someone switches providers by hand. Wanted: on a provider error, retry once on the next compatible provider, e.g. a local Ollama entry.
