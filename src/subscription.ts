export class Subscription<T> {
    private readonly messageQueue: T[] = [];
    private readonly resolvers: Array<(value: T | null) => void> = [];

    private isConnected = true;

    constructor(private readonly webSocket: WebSocket) {
        this.webSocket.addEventListener("message", (event) => {
            const message = JSON.parse(event.data);
            this.handleMessage(message);
        });

        this.webSocket.addEventListener("close", () => {
            // Signal end to all streams
            this.resolvers.forEach((resolve) => {
                resolve(null);
            });
            this.resolvers.length = 0;
        });

        this.webSocket.addEventListener("error", (e) => {
            console.error("WebSocket error:", e);
            this.resolvers.forEach((resolve) => {
                resolve(null);
            });
            this.resolvers.length = 0;
        });
    }

    private handleMessage(message: T): void {
        if (this.resolvers.length > 0) {
            // biome-ignore lint/style/noNonNullAssertion: We good
            const resolve = this.resolvers.shift()!;
            resolve(message);
        } else {
            this.messageQueue.push(message);
        }
    }

    private async waitForMessage(): Promise<T | null> {
        if (this.messageQueue.length > 0) {
            // biome-ignore lint/style/noNonNullAssertion:Impossible
            return this.messageQueue.shift()!;
        }

        if (!this.isConnected) return null;

        return new Promise((resolve) => {
            this.resolvers.push(resolve);
        });
    }

    async *messages(): AsyncGenerator<T, void, unknown> {
        while (this.isConnected) {
            const message = await this.waitForMessage();
            if (message === null) break;
            yield message;
        }
    }
}
