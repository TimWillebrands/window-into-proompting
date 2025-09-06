export function Party() {
    return (
        <div
            style="
            position: fixed;
            top: 0;
            left: 0;
            width: 100vw;
            height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
            background: linear-gradient(135deg, #c0c0c0 0%, #a0a0a0 25%, #b8b8b8 50%, #9a9a9a 75%, #c0c0c0 100%);
        "
        >
            <div
                class="window"
                style="
                    width: clamp(600px, 80vw, 1000px);
                    height: clamp(400px, 70vh, 700px);
                    display: flex;
                    flex-direction: column;
                    resize: both;
                    overflow: hidden;
                    min-width: 500px;
                    min-height: 350px;
                "
            >
                <div class="title-bar">
                    <div class="title-bar-text">
                        🎉 Party Chat - LLM Conversation
                    </div>
                    <div class="title-bar-controls">
                        <button aria-label="Minimize"></button>
                        <button aria-label="Maximize"></button>
                        <button aria-label="Close"></button>
                    </div>
                </div>

                <div
                    class="window-body"
                    style="overflow-y: auto; flex: 1; display: flex; flex-direction: column; padding: 0;"
                >
                    {/* Messages Area */}
                    <div
                        id="chat-messages"
                        style="
                            flex: 1;
                            overflow-y: auto;
                            overflow-x: hidden;
                            padding: 16px;
                            border: 2px inset #dfdfdf;
                            margin: 8px;
                            background: white;
                            font-family: 'MS Sans Serif', sans-serif;
                            font-size: 11px;
                            height: 0;
                            min-height: 200px;
                        "
                    >
                        {/* Welcome message */}
                        <div
                            style="
                            text-align: center;
                            color: #666;
                            margin: 20px 0;
                            padding: 20px;
                            border: 1px dashed #ccc;
                        "
                        >
                            <div style="font-size: 16px; margin-bottom: 8px;">
                                💭
                            </div>
                            <div>Welcome to Party Chat!</div>
                            <div style="font-size: 10px; margin-top: 4px;">
                                Start a conversation with the AI...
                            </div>
                        </div>
                    </div>

                    {/* Input Area */}
                    <div style="padding: 8px; border-top: 1px solid #dfdfdf;">
                        <form
                            hx-post="/test/message"
                            hx-target="#chat-messages"
                            hx-swap="beforeend"
                            hx-include="[name='prompt']"
                            hx-on:before-request="
                                event.target.querySelector('[type=submit]').disabled = true;
                                event.target.querySelector('[type=submit]').textContent = '⏳ Sending...';
                            "
                            hx-on:after-request="
                                event.target.querySelector('[type=submit]').disabled = false;
                                event.target.querySelector('[type=submit]').textContent = '🚀 Send';
                                event.target.reset();
                                document.getElementById('chat-messages').scrollTop = document.getElementById('chat-messages').scrollHeight;
                            "
                            style="display: flex; flex-direction: column; gap: 8px;"
                        >
                            {/* Hidden div that gets submitted and creates user message */}
                            <div hx-swap-oob="beforeend:#chat-messages">
                                <template>
                                    <div
                                        class="user-message"
                                        style="
                                        margin-bottom: 16px;
                                        margin-left: 20px;
                                        padding: 12px;
                                        border: 1px solid #dfdfdf;
                                        background: #e6f3ff;
                                        box-shadow: inset -1px -1px #0a0a0a, inset 1px 1px #dfdfdf, inset -2px -2px #808080, inset 2px 2px #c0c0c0;
                                    "
                                    >
                                        <div style="font-weight: bold; margin-bottom: 4px; color: #000080;">
                                            👤 You
                                            <span style="float: right; font-weight: normal; font-size: 10px; color: #666;">
                                                {/* Timestamp will be added by server */}
                                            </span>
                                        </div>
                                        <div style="line-height: 1.4; white-space: pre-wrap;">
                                            {/* Message content will be added by server */}
                                        </div>
                                    </div>
                                </template>
                            </div>

                            <div class="field-row-stacked">
                                <label
                                    for="prompt-input"
                                    style="font-weight: bold;"
                                >
                                    💬 Your Message:
                                </label>
                                <textarea
                                    name="prompt"
                                    id="prompt-input"
                                    rows={3}
                                    required
                                    placeholder="Type your message here... (Ctrl+Enter to send)"
                                    style="
                                        width: 100%;
                                        resize: vertical;
                                        font-family: 'MS Sans Serif', sans-serif;
                                        font-size: 11px;
                                    "
                                    hx-trigger="keydown[ctrlKey&&key=='Enter']"
                                    hx-post="/test/message"
                                    hx-target="#chat-messages"
                                    hx-swap="beforeend"
                                    hx-include="closest form"
                                ></textarea>
                            </div>

                            <div
                                class="field-row"
                                style="justify-content: flex-end; gap: 8px;"
                            >
                                <button
                                    type="button"
                                    hx-on:click="
                                        document.getElementById('prompt-input').value = '';
                                        document.getElementById('prompt-input').focus();
                                    "
                                >
                                    🗑 Clear
                                </button>
                                <button type="submit" style="min-width: 75px;">
                                    🚀 Send
                                </button>
                            </div>
                        </form>
                    </div>
                </div>

                <div class="status-bar">
                    <p class="status-bar-field">🟢 Ready to chat</p>
                    <p class="status-bar-field" id="message-count">
                        Messages: 0
                    </p>
                    <p class="status-bar-field">🤖 AI Assistant Online</p>
                </div>
            </div>
        </div>
    );
}

export function Message({
    room,
    content,
    isUser = false,
}: {
    room: string;
    content?: string;
    isUser?: boolean;
}) {
    if (content) {
        // Static message display
        return (
            <div
                class={`message ${isUser ? "user-message" : "ai-message"}`}
                style={`
                    margin-bottom: 16px;
                    ${isUser ? "margin-left: 20px;" : "margin-right: 20px;"}
                    padding: 12px;
                    border: 1px solid #dfdfdf;
                    background: ${isUser ? "#e6f3ff" : "#f0f8e6"};
                    box-shadow: inset -1px -1px #0a0a0a, inset 1px 1px #dfdfdf, inset -2px -2px #808080, inset 2px 2px #c0c0c0;
                `}
                hx-swap-oob="afterbegin:#message-count"
            >
                <div style="font-weight: bold; margin-bottom: 4px; color: #000080;">
                    {isUser ? "👤 You" : "🤖 AI Assistant"}
                    <span style="float: right; font-weight: normal; font-size: 10px; color: #666;">
                        {new Date().toLocaleTimeString()}
                    </span>
                </div>
                <div style="line-height: 1.4; white-space: pre-wrap;">
                    {content}
                </div>
            </div>
        );
    }

    // Streaming AI response message
    return (
        <article
            role="tabpanel"
            hx-ext="sse"
            sse-connect={`${room}/prompt`}
            sse-swap="message"
            hx-swap="beforeend"
            hx-target="find .message-content"
            sse-close="finished"
            style="
                margin-bottom: 16px;
                margin-right: 20px;
                padding: 12px;
                border: 1px solid #dfdfdf;
                background: #f0f8e6;
                box-shadow: inset -1px -1px #0a0a0a, inset 1px 1px #dfdfdf, inset -2px -2px #808080, inset 2px 2px #c0c0c0;
            "
        >
            <div style="font-weight: bold; margin-bottom: 8px; color: #000080;">
                🤖 AI Assistant
                <span style="float: right; font-weight: normal; font-size: 10px; color: #666;">
                    {new Date().toLocaleTimeString()}
                </span>
            </div>

            <div
                class="message-content"
                style="
                font-family: 'MS Sans Serif', sans-serif;
                font-size: 11px;
                line-height: 1.4;
                min-height: 16px;
            "
            >
                {/* Typing indicator while loading */}
                <span
                    class="typing-indicator htmx-indicator"
                    style="opacity: 0.6;"
                >
                    <span style="animation: blink 1.4s infinite both;">●</span>
                    <span style="animation: blink 1.4s infinite both; animation-delay: 0.2s;">
                        ●
                    </span>
                    <span style="animation: blink 1.4s infinite both; animation-delay: 0.4s;">
                        ●
                    </span>
                </span>
            </div>

            <zero-md style="display: none;">
                <template></template>
                <script type="text/markdown"></script>
            </zero-md>
        </article>
    );
}

// Helper component for standalone user messages (if needed)
export function UserMessage({ content }: { content: string }) {
    return (
        <div
            class="message user-message"
            style="
                margin-bottom: 16px;
                margin-left: 20px;
                padding: 12px;
                border: 1px solid #dfdfdf;
                background: #e6f3ff;
                box-shadow: inset -1px -1px #0a0a0a, inset 1px 1px #dfdfdf, inset -2px -2px #808080, inset 2px 2px #c0c0c0;
            "
        >
            <div style="font-weight: bold; margin-bottom: 4px; color: #000080;">
                👤 You
                <span style="float: right; font-weight: normal; font-size: 10px; color: #666;">
                    {new Date().toLocaleTimeString()}
                </span>
            </div>
            <div style="line-height: 1.4; white-space: pre-wrap;">
                {content}
            </div>
        </div>
    );
}
