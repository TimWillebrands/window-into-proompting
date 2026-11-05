# PostHog AI Observability setup

## Status

**Wired, awaiting runtime verification.** The integration uses the `manual-capture` AI Observability variant because the project does not declare a Node or Python LLM SDK, while the backend makes OpenAI-compatible calls through its own OpenRouter and Ollama endpoint grains.

## What was added

- `backend/Observability/AiObservabilityCapture.cs`
  - Sends a `$ai_generation` event to the configured PostHog ingestion host after every streamed and one-shot generation.
  - Includes provider, model, request messages, generated output, temperature, streaming status, base URL, and success/error status.
  - Uses the chat-group identifier as `$ai_session_id`, so multiple turns in the same chat are grouped in one AI Observability session.
  - Uses the active .NET activity trace ID as `$ai_trace_id`, so all generations inside a persona turn share a trace.
  - Falls back to a process-scoped session and a new trace ID for background work without a chat context.
  - Does not add a user identifier because no authenticated user identifier is available at these LLM call sites. The capture uses the AI session ID as its stable distinct ID.
  - Does not interfere with an LLM response if capture delivery fails.

- `backend/Grains/Generation/LlmEndpointGrainBase.cs`
  - Captures both streamed and one-shot generations at the shared endpoint seam, covering OpenRouter and Ollama routes without changing existing product-analytics events.

- `backend/Grains/Generation/OpenRouterEndpointGrain.cs` and `backend/Grains/Generation/OllamaEndpointGrain.cs`
  - Inject the additive capture service and provide the actual provider, model, and base URL for each generation.

- `backend/Services/ResponsePipeline/ResponsePipeline.cs`
  - Propagates the chat session identifier through the existing activity context for normal and initiated persona turns.

- `backend/Program.cs`
  - Registers the capture service without altering the existing PostHog analytics initialization, identify behavior, capture calls, or dashboards.

- `.posthog-wizard-cache/.posthog-ai.json`
  - Records this manual-capture setup for subsequent wizard reports.

## Configuration

The project environment now contains these keys:

- `POSTHOG_PROJECT_TOKEN`
- `POSTHOG_HOST`

The capture service deliberately reuses the environment-variable names already used by the project’s PostHog analytics configuration. No token or other secret was added to source code.

## Expected trace shape

For a chat message that causes a persona to respond:

1. AI Observability receives one `$ai_session_id` for that chat group.
2. The decision and speaking calls within one persona turn share one `$ai_trace_id`.
3. A later turn in the same chat group creates a new trace under the same session.
4. The provider is recorded as `openrouter` or `ollama`, matching the routed endpoint.

The project does not register LLM tools/functions at its generation call sites, so no `$ai_span` tool events were added. This is expected for the current flow.

## Verification

A live model call was not made during setup. To verify delivery:

1. Start the backend with the configured environment values.
2. In the application, send a message that makes a persona generate a response. A turn that performs both decision and speaking generation is the best test.
3. Open **AI Observability → Traces** in the PostHog project and inspect the newest trace.
4. Send a second message in the same chat group. Confirm that it creates another trace in the same AI session.
5. Confirm the trace contains the expected provider/model and the complete prompt/output data.

The backend build command could not be run in this execution environment because the available command runner does not permit `dotnet`. Static inspection confirms the service registration, capture calls, endpoint dependency injection, and shared trace/session propagation are connected. Run `dotnet build backend/backend.csproj` locally before deployment.

## Privacy mode

**Effective setting: prompt and completion content is captured.** This is a manual-capture integration, so the payload in `backend/Observability/AiObservabilityCapture.cs` explicitly includes `$ai_input` and `$ai_output_choices`; there is no SDK-wide `privacyMode` switch on this path.

Before processing prompts or responses that must not be stored in PostHog, update that capture helper to omit `$ai_input` and `$ai_output_choices` for the relevant calls. Do not assume an SDK privacy option will remove manually supplied payload fields. This change only affects newly captured events; it does not remove previously stored data.

See [AI Observability privacy mode](https://posthog.com/docs/ai-observability/privacy-mode) for the behavior of SDK-managed integrations.
