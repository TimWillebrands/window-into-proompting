export async function* streamPost<T>(
    url: string,
    body: unknown,
    signal?: AbortSignal,
): AsyncGenerator<T, void, unknown> {
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
        signal,
    });

    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    if (!response.body) return;

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    try {
        while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });

            const lines = buffer.split('\n');
            buffer = lines.pop() ?? '';

            for (const line of lines) {
                if (line.startsWith('data: ')) {
                    const data = line.slice(6).trim();
                    if (data) yield JSON.parse(data) as T;
                }
            }
        }
    } finally {
        reader.releaseLock();
    }
}
