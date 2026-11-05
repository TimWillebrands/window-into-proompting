# Tailnet sidecars: local providers reachable from production

> Status: accepted. Builds on ADR 0032 (Persona Models) and the failover in `.scratch/world/issues/04-provider-failover.md`. Vocabulary in `CONTEXT.md`.

Ollama and llama.cpp providers sit on machines at home. The server on Hetzner can't reach them, so local models only
worked in development.

## Decision

1. **Each Tailnet is a Tailscale container exposing SOCKS5.** In production it's a Kamal accessory. The backend only knows
   `Tailnets:<name>=socks5://host:port`, so another Tailnet is another accessory and one config line.
2. **A provider opts in with `LlmProviderEntry.Tailnet`.** Null connects directly, so nothing changes without it.
3. **A Tailnet that isn't configured fails; it never falls back to direct.** That path looks like an unreachable host, so
   the router benches the provider.
4. **DNS stays with the sidecar.** .NET sends the hostname through SOCKS5 unresolved.
5. **The Tailnet client skips `IHttpClientFactory`.** Its default resilience handler stops requests at 30 seconds, which
   would cut off generations.
6. **A dead path is a hard failure.** A sleeping box must bench the provider, not fail every call. The OpenAI client tries
   4 times, so a 5 second connect timeout stalls the first call about 20 seconds.

## Considered

- **`libtailscale` in the backend process:** there is no .NET package, so it needs our own P/Invoke layer, a Go build stage,
  and a Go runtime inside the CLR. It would allow Tailnets to be added at runtime, which we don't need.
- **`tailscale-rs`:** no .NET bindings ([issue 90](https://github.com/tailscale/tailscale-rs/issues/90)), a blocking socket
  API with no cancel, no MagicDNS or subnet routers, and pre-1.0.
- **Tailscale on the host:** no code, but it puts the whole server on the Tailnet and offers one Tailnet only.

## Security

The SOCKS5 port has no auth, so it's never published: production keeps it on the Docker network and development binds it
to loopback. Use a tagged auth key, and let tailnet ACLs limit that tag to the inference ports. Admins set provider URLs,
so without that ACL they could reach any device on the Tailnet through the backend.

`TS_AUTH_ONCE` matters: auth keys expire within 90 days, and without it a restart logs in again with the expired key.
The state volume keeps the node identity across restarts.
