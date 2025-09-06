import { useState } from "hono/jsx/dom";

export function Party() {
    return (
        <div class="window" style="width: 320px">
            <div class="title-bar">
                <div class="title-bar-text">A lively chat...</div>
            </div>
            <div class="window-body" id="chat">
                <form hx-post="/message" hx-target="#chat" hx-swap="afterbegin">
                    <div class="field-row-stacked" style="width: 200px">
                        <label for="text24">Prompt:</label>
                        <textarea name="prompt" id="text24" rows={8}></textarea>
                        <button type="submit">Send</button>
                    </div>
                </form>
            </div>
            <div class="status-bar">
                <p class="status-bar-field">Press F1 for help</p>
                <p class="status-bar-field">Slide 1</p>
                <p class="status-bar-field">CPU Usage: 14%</p>
            </div>
        </div>
    );
}
