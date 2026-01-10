import { html, render } from "https://esm.sh/htm/preact/standalone";
import { AppType } from "..";

export default function TalkerApp() {
    const App = () => {
        return html`
        <div>
          <h1>Hello, World!</h1>
        </div>
      `;
    };

    render(App(), document.getElementById("root"));
}

export function addPersonaRoutes(app: AppType) {
    // Personas application
    app.get("/personas", async (c) => {
        const personas = await getAllPersonas(c.env);
        console.log("Loading personas:", personas.length);
        return c.html(<Personas personas={personas} />);
    });

    // Just the personas list (for OOB swaps)
    app.get("/personas/list", async (c) => {
        const personas = await getAllPersonas(c.env);
        return c.html(<PersonasList personas={personas} />);
    });
}
