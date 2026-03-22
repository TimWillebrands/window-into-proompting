/**
 * The main structure of the entire API response from the OpenRouter models endpoint.
 */
export interface OpenRouterModelsResponse {
    data: Data;
}

/**
 * The core data object containing lists of models, analytics, and categorized usage statistics.
 */
interface Data {
    /**
     * An array of available AI models, each with detailed configuration and endpoint information.
     */
    models: Model[];
    /**
     * A record of usage analytics, where each key is a model's permalink (e.g., "minimax/minimax-m2:free")
     * and the value contains its usage statistics.
     */
    analytics: Record<string, AnalyticsData>;
    /**
     * A record of models categorized by their usage in different domains (e.g., "programming", "science"),
     * showing rank and volume.
     */
    categories: Record<string, CategoryData[]>;
}

/**
 * Describes a single AI model's main properties, including its capabilities,
 * default configuration, and associated endpoint.
 */
interface Model {
    slug: string;
    hf_slug: string | null;
    updated_at: string;
    created_at: string;
    hf_updated_at: string | null;
    name: string;
    short_name: string;
    author: string;
    description: string;
    model_version_group_id: string | null;
    context_length: number;
    input_modalities: string[];
    output_modalities: string[];
    has_text_output: boolean;
    group: string;
    instruct_type: string | null;
    default_system: string | null;
    default_stops: string[];
    hidden: boolean;
    router: null;
    warning_message: string | null;
    promotion_message: string | null;
    permaslug: string;
    reasoning_config: ReasoningConfig | null;
    features: ModelFeatures | null;
    default_parameters: DefaultParameters;
    default_order: unknown[];
    quick_start_example_type: null;
    is_trainable_text: boolean | null;
    is_trainable_image: boolean | null;
    endpoint: Endpoint;
}

/**
 * Describes a model's specific deployment endpoint, including provider-specific details,
 * pricing, limits, and supported features.
 */
interface Endpoint {
    id: string;
    name: string;
    context_length: number;
    model: EndpointModel;
    model_variant_slug: string;
    model_variant_permaslug: string;
    adapter_name: string;
    provider_name: string;
    provider_info: ProviderInfo;
    provider_display_name: string;
    provider_slug: string;
    provider_model_id: string;
    quantization: string;
    variant: string;
    is_free: boolean;
    can_abort: boolean;
    max_prompt_tokens: number | null;
    max_completion_tokens: number | null;
    max_prompt_images: number | null;
    max_tokens_per_image: number | null;
    supported_parameters: string[];
    is_byok: boolean;
    moderation_required: boolean;
    data_policy: DataPolicy;
    pricing: Pricing;
    variable_pricings: unknown[];
    is_hidden: boolean;
    is_deranked: boolean;
    is_disabled: boolean;
    supports_tool_parameters: boolean;
    supports_reasoning: boolean;
    supports_multipart: boolean;
    limit_rpm: number | null;
    limit_rpd: number | null;
    limit_rpm_cf: number | null;
    has_completions: boolean;
    has_chat_completions: boolean;
    features: EndpointFeatures;
    provider_region: string | null;
}

/**
 * A simplified model definition used within the Endpoint object to represent
 * the base model's properties, preventing recursive type definitions.
 */
interface EndpointModel {
    slug: string;
    hf_slug: string | null;
    updated_at: string;
    created_at: string;
    hf_updated_at: string | null;
    name: string;
    short_name: string;
    author: string;
    description: string;
    model_version_group_id: string | null;
    context_length: number;
    input_modalities: string[];
    output_modalities: string[];
    has_text_output: boolean;
    group: string;
    instruct_type: string | null;
    default_system: string | null;
    default_stops: string[];
    hidden: boolean;
    router: null;
    warning_message: string | null;
    promotion_message: string | null;
    permaslug: string;
    reasoning_config: ReasoningConfig | null;
    features: ModelFeatures | null;
    default_parameters: DefaultParameters;
    default_order: unknown[];
    quick_start_example_type: null;
    is_trainable_text: boolean | null;
    is_trainable_image: boolean | null;
}

/**
 * Contains detailed information about the model provider, such as their name,
 * data policies, and technical capabilities.
 */
interface ProviderInfo {
    name: string;
    displayName: string;
    slug: string;
    baseUrl: string;
    dataPolicy: DataPolicy;
    headquarters?: string;
    datacenters?: string[];
    regionOverrides: Record<string, unknown>;
    hasChatCompletions: boolean;
    hasCompletions: boolean;
    isAbortable: boolean;
    moderationRequired: boolean;
    editors: string[];
    owners: string[];
    adapterName: string;
    isMultipartSupported: boolean;
    statusPageUrl: string | null;
    byokEnabled: boolean;
    icon: Icon;
    ignoredProviderModels: string[];
}

/**
 * Defines the provider's policy regarding data usage, training, retention, and privacy.
 */
interface DataPolicy {
    training: boolean;
    trainingOpenRouter: boolean;
    retainsPrompts: boolean;
    canPublish: boolean;
    termsOfServiceURL?: string;
    privacyPolicyURL?: string;
    retentionDays?: number;
    requiresUserIDs?: boolean;
}

/**
 * Specifies the pricing details for various types of model usage, such as prompt tokens,
 * completion tokens, and image generation.
 */
interface Pricing {
    prompt: string;
    completion: string;
    image: string;
    request: string;
    web_search: string;
    internal_reasoning: string;
    image_output: string;
    discount: number;
}

/**
 * Describes special features of a model, such as its reasoning configuration or
 * how it handles chat templates.
 */
interface ModelFeatures {
    reasoning_config?: ReasoningConfig;
    chat_template_config?: {
        should_hoist_and_merge_system_messages: boolean;
    };
}

/**
 * Details the specific features supported by a deployment endpoint, such as tool use,
 * structured outputs, and audio input.
 */
interface EndpointFeatures {
    is_mandatory_reasoning?: boolean;
    should_send_reasoning_text_in_text_content?: boolean;
    supports_tool_choice: SupportsToolChoice;
    supported_parameters?: Record<string, unknown>;
    supports_input_audio?: boolean;
    disable_free_endpoint_limits?: boolean;
}

/**
 * Details the endpoint's support for different modes of tool-calling (function calling),
 * such as auto, required, or none.
 */
interface SupportsToolChoice {
    literal_none: boolean;
    literal_auto: boolean;
    literal_required: boolean;
    type_function: boolean;
}

/**
 * Defines the configuration for a model's reasoning or "thinking" process,
 * including the special tokens used to enclose reasoning steps.
 */
interface ReasoningConfig {
    start_token: string;
    end_token: string;
    system_prompt?: string;
}

/**
 * Specifies the default inference parameters for the model, such as temperature,
 * top_p, and frequency penalty.
 */
interface DefaultParameters {
    temperature: number | null;
    top_p: number | null;
    frequency_penalty: number | null;
}

/**
 * Represents a display icon for a provider, including its URL and optional styling class.
 */
interface Icon {
    url: string;
    className?: string;
}

/**
 * Contains aggregated analytics data for a specific model variant over a given period,
 * including token counts and feature usage.
 */
interface AnalyticsData {
    date: string;
    model_permaslug: string;
    variant: string;
    variant_permaslug: string;
    count: number;
    total_completion_tokens: number;
    total_prompt_tokens: number;
    total_native_tokens_reasoning: number;
    num_media_prompt: number;
    num_media_completion: number;
    num_audio_prompt: number;
    total_native_tokens_cached: number;
    total_tool_calls: number;
    requests_with_tool_call_errors: number;
}

/**
 * Represents categorical usage data for a model, showing its rank and volume
 * in a specific domain like "programming" or "finance".
 */
interface CategoryData {
    id: number;
    date: string;
    model: string;
    category: string;
    count: number;
    total_prompt_tokens: number;
    total_completion_tokens: number;
    volume: number;
    rank: number;
}

export async function loadFreeOpenRouterModels() {
    const freeStuff = await fetch(
        "https://openrouter.ai/api/frontend/models/find?context=128000&fmt=cards&input_modalities=text&max_price=0&order=top-weekly",
        {
            cf: {
                cacheTtl: 60,
                cacheEverything: true,
            },
            headers: {
                Accept: "application/json",
                "Accept-Encoding": "gzip, deflate, br, zstd",
                "Sec-GPC": "1",
                Connection: "keep-alive",
                "Sec-Fetch-Dest": "empty",
                "Sec-Fetch-Mode": "cors",
                "Sec-Fetch-Site": "same-origin",
                Priority: "u=4",
                TE: "trailers",
            },
        },
    );

    //TODO: typecheckin response
    const freeModels = await freeStuff.json<OpenRouterModelsResponse>();

    return freeModels.data;
}
