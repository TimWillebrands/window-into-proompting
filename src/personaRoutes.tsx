import type { AppType } from ".";
import {
    MissingPersona,
    type Persona,
    PersonaBlank,
    PersonaDeleted,
    PersonaForm,
    type PersonaMetadata,
    Personas,
    PersonasList,
    PersonaTemplateMenu,
} from "./components/personas";

export function addPersonaRoutes(app: AppType) {
    // Helper function to get all personas
    async function getAllPersonas(env: Cloudflare.Env): Promise<PersonaMetadata[]> {
        const personaData = await env.DESKTOP_DATA.list<PersonaMetadata>({
            prefix: "persona:",
        });

        return personaData.keys
            .map((key: { metadata?: PersonaMetadata }) => key.metadata)
            .filter((persona): persona is PersonaMetadata => persona !== undefined);
    }

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

    // Template menu
    app.get("/personas/templates", async (c) => {
        return c.html(<PersonaTemplateMenu />);
    });

    // Blank state
    app.get("/personas/blank", async (c) => {
        return c.html(<PersonaBlank />);
    });

    // Persona edit form
    app.get("/personas/:id", async (c) => {
        const id = c.req.param("id");
        const personaData = await c.env.DESKTOP_DATA.get<Persona>(
            `persona:${id}`,
            { type: "json" },
        );

        return personaData !== null
            ? c.html(<PersonaForm persona={personaData} />)
            : c.html(<MissingPersona personaId={id} />, 404);
    });

    // Persona creation
    app.post("/personas/new", async (c) => {
        const body = await c.req.formData();
        const id = crypto.randomUUID();

        const personaData: Persona = {
            id,
            name: body.get("name")?.toString() ?? "New Persona",
            systemPrompt:
                body.get("systemPrompt")?.toString() ??
                "You are a helpful AI assistant.",
        };

        // Store with metadata for easy listing
        await c.env.DESKTOP_DATA.put(
            `persona:${personaData.id}`,
            JSON.stringify(personaData),
            {
                metadata: {
                    id: personaData.id,
                    name: personaData.name,
                },
            },
        );

        console.log("Created new persona:", personaData.name, personaData.id);

        // Return both the form and updated list (OOB swap)
        const personas = await getAllPersonas(c.env);
        return c.html(
            <>
                <PersonaForm persona={personaData} />
                <PersonasList personas={personas} hx-swap-oob="outerHTML:#personas-list" />
            </>,
            201,
        );
    });

    // Persona update
    app.put("/personas/:id", async (c) => {
        const id = c.req.param("id");
        const body = await c.req.formData();
        const existingPersona = await c.env.DESKTOP_DATA.get<Persona>(
            `persona:${id}`,
            { type: "json" },
        );

        if (!existingPersona) {
            return c.html(<MissingPersona personaId={id} />, 404);
        }

        const updatedPersona: Persona = {
            id,
            name: body.get("name")?.toString()?.trim() || existingPersona.name,
            systemPrompt:
                body.get("systemPrompt")?.toString()?.trim() ||
                existingPersona.systemPrompt,
        };

        // Validate inputs
        if (!updatedPersona.name) {
            return c.text("Name cannot be empty", 400);
        }

        if (!updatedPersona.systemPrompt) {
            return c.text("System prompt cannot be empty", 400);
        }

        // Update with metadata for easy listing
        await c.env.DESKTOP_DATA.put(
            `persona:${id}`,
            JSON.stringify(updatedPersona),
            {
                metadata: {
                    id: updatedPersona.id,
                    name: updatedPersona.name,
                },
            },
        );

        console.log("Updated persona:", updatedPersona.name, id);

        // Return both the form and updated list (OOB swap)
        const personas = await getAllPersonas(c.env);
        return c.html(
            <>
                <PersonaForm persona={updatedPersona} />
                <PersonasList personas={personas} hx-swap-oob="outerHTML:#personas-list" />
            </>,
            200,
        );
    });

    // Persona deletion
    app.delete("/personas/:id", async (c) => {
        const id = c.req.param("id");
        const existingPersona = await c.env.DESKTOP_DATA.get<Persona>(
            `persona:${id}`,
            { type: "json" },
        );

        if (!existingPersona) {
            return c.html(<MissingPersona personaId={id} />, 404);
        }

        try {
            await c.env.DESKTOP_DATA.delete(`persona:${id}`);
            console.log("Deleted persona:", existingPersona.name, id);

            // Return deleted message and updated list (OOB swap)
            const personas = await getAllPersonas(c.env);
            return c.html(
                <>
                    <PersonaDeleted />
                    <PersonasList personas={personas} hx-swap-oob="outerHTML:#personas-list" />
                </>,
                200,
            );
        } catch (error) {
            console.error("Failed to delete persona:", error);
            return c.text("Failed to delete persona", 500);
        }
    });

    // Export persona as JSON
    app.get("/personas/:id/export", async (c) => {
        const id = c.req.param("id");
        const persona = await c.env.DESKTOP_DATA.get<Persona>(`persona:${id}`, {
            type: "json",
        });

        if (!persona) {
            return c.text("Persona not found", 404);
        }

        const filename = `${persona.name.replace(/[^a-z0-9]/gi, "_").toLowerCase()}_persona.json`;

        return c.json(persona, 200, {
            "Content-Disposition": `attachment; filename="${filename}"`,
            "Content-Type": "application/json",
        });
    });

    // Duplicate persona
    app.post("/personas/:id/duplicate", async (c) => {
        const id = c.req.param("id");
        const existingPersona = await c.env.DESKTOP_DATA.get<Persona>(
            `persona:${id}`,
            { type: "json" },
        );

        if (!existingPersona) {
            return c.html(<MissingPersona personaId={id} />, 404);
        }

        const newId = crypto.randomUUID();
        const duplicatedPersona: Persona = {
            id: newId,
            name: `${existingPersona.name} (Copy)`,
            systemPrompt: existingPersona.systemPrompt,
        };

        try {
            await c.env.DESKTOP_DATA.put(
                `persona:${newId}`,
                JSON.stringify(duplicatedPersona),
                {
                    metadata: {
                        id: duplicatedPersona.id,
                        name: duplicatedPersona.name,
                    },
                },
            );

            console.log("Duplicated persona:", duplicatedPersona.name, newId);

            // Return both the form and updated list (OOB swap)
            const personas = await getAllPersonas(c.env);
            return c.html(
                <>
                    <PersonaForm persona={duplicatedPersona} />
                    <PersonasList personas={personas} hx-swap-oob="outerHTML:#personas-list" />
                </>,
                201,
            );
        } catch (error) {
            console.error("Failed to duplicate persona:", error);
            return c.text("Failed to duplicate persona", 500);
        }
    });
}
