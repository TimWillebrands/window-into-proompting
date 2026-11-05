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
- **A "Connect tailnet" login link:** self-serve and closest to OAuth, since Tailscale has no third-party consent flow. It
  needs a multi-tenant gateway and puts the server on each person's whole tailnet. Revisit if sharing gets tedious.
- **Tailscale on the host:** no code, but it puts the whole server on the Tailnet and offers one Tailnet only.

## Security

The SOCKS5 port has no auth, so it's never published: production keeps it on the Docker network and development binds it
to loopback.

The sidecar is a user-owned node, not a tagged one: tagged machines can't reach devices shared from another tailnet, and
sharing is how other people's machines get in. Each person shares one machine, which stays quarantined and can't start
connections. Disable key expiry on the node, since user-owned keys expire after 180 days by default. Admins set provider
URLs, so the owner's tailnet policy decides what the backend can reach.

A shared machine in the pool gets everyone's prompts, DMs included, and its owner can read them. We accept that for now,
for a small group who trust each other. Reserved keeps a machine to the Personas routed to it.

`TS_AUTH_ONCE` matters: auth keys expire within 90 days, and without it a restart logs in again with the expired key.
The state volume keeps the node identity across restarts.
