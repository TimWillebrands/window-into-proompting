import { Hono } from "hono";
import { Welcome } from "./components/welcome";

const app = new Hono<{ Bindings: Cloudflare.Env }>();

app.get("/", async (c) => {
    return c.html(<Welcome />);
});

export default app;
