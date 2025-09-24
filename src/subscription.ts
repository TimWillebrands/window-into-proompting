export class Subscription<T> {
    private messageQueue: T[] = [];
    private resolvers: Array<(value: T | null) => void> = [];
    private streams = new Set<MessageStream<T>>();

    constructor(public webSocket: WebSocket) {
        this.webSocket.addEventListener("message", (event) => {
            const message = JSON.parse(event.data);
            this.handleMessage(message);
        });

        this.webSocket.addEventListener("close", () => {
            // Signal end to all streams
            this.streams.forEach((stream) => {
                stream.close();
            });
            this.resolvers.forEach((resolve) => {
                resolve(null);
            });
            this.resolvers = [];
        });

        this.webSocket.addEventListener("error", () => {
            // Signal error to all streams
            this.streams.forEach((stream) => {
                stream.close();
            });
            this.resolvers.forEach((resolve) => {
                resolve(null);
            });
            this.resolvers = [];
        });
    }

    private handleMessage(message: T): void {
        // Fan-out: send message to all active streams
        this.streams.forEach((stream) => {
            stream.pushMessage(message);
        });

        // Legacy support: also handle single consumer pattern
        if (this.resolvers.length > 0) {
            // biome-ignore lint/style/noNonNullAssertion: We good
            const resolve = this.resolvers.shift()!;
            resolve(message);
        } else {
            this.messageQueue.push(message);
        }
    }

    // New fan-out method: creates independent message streams
    messages(): AsyncGenerator<T, void, unknown> {
        const stream = new MessageStream<T>();
        this.streams.add(stream);

        // Clean up when stream is done
        const cleanup = () => {
            this.streams.delete(stream);
        };

        return stream.generator(cleanup);
    }
}

class MessageStream<T> {
    private messageQueue: T[] = [];
    private resolvers: Array<(value: T | null) => void> = [];
    private isActive = true;

    pushMessage(message: T): void {
        if (!this.isActive) return;

        if (this.resolvers.length > 0) {
            // biome-ignore lint/style/noNonNullAssertion: We good
            const resolve = this.resolvers.shift()!;
            resolve(message);
        } else {
            this.messageQueue.push(message);
        }
    }

    close(): void {
        this.isActive = false;
        this.resolvers.forEach((resolve) => {
            resolve(null);
        });
        this.resolvers = [];
    }

    private i = 0;

    private async getNextMessage(): Promise<T | null> {
        if (this.messageQueue.length > 0) {
            return this.messageQueue.shift() ?? null;
        }

        if (!this.isActive) {
            return null;
        }

        return new Promise<T | null>((resolve) => {
            this.resolvers.push(resolve);
        });
    }

    async *generator(cleanup: () => void): AsyncGenerator<T, void, unknown> {
        try {
            while (this.isActive) {
                const message = await this.getNextMessage();
                if (message === null) {
                    break;
                }
                yield message;
            }
        } finally {
            cleanup();
        }
    }
}
