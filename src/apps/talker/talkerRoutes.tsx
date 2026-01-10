import type { AppType } from "@/index";

export function addPersonaRoutes(app: AppType) {
    // Personas application
    app.get("/talker", async (c) => {
        const personas = await getAllPersonas(c.env);
        console.log("Loading personas:", personas.length);
        return c.html(<Personas personas={personas} />);
    });
}
