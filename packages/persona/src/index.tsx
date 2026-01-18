import { getAuth } from "@hono/clerk-auth";
import { ensurePersonaBio, getAllPersonas } from "@proompting/backend";
import type { Persona } from "@proompting/core";
import { Hono } from "hono";
import {
    MissingPersona,
    PersonaBlank,
    PersonaDeleted,
    PersonaForm,
    Personas,
    PersonasList,
    PersonaTemplateMenu,
} from "./components/personas";

const app = new Hono<{ Bindings: Cloudflare.Env }>();

// Personas application
app.get("/", async (c) => {
    const personas = await getAllPersonas(c.env);
    console.log("Loading personas:", personas.length);
    return c.html(<Personas personas={personas} />);
});

// Just the personas list (for OOB swaps)
app.get("/list", async (c) => {
    const personas = await getAllPersonas(c.env);
    return c.html(<PersonasList personas={personas} />);
});

// Template menu
app.get("/templates", async (c) => {
    return c.html(<PersonaTemplateMenu />);
});

// Blank state
app.get("/blank", async (c) => {
    return c.html(<PersonaBlank />);
});

// Persona edit form
app.get("/:id", async (c) => {
    const id = c.req.param("id");
    const personaData = await ensurePersonaBio(c.env, id);

    return personaData !== null
        ? c.html(<PersonaForm persona={personaData} />)
        : c.html(<MissingPersona personaId={id} />, 404);
});

app.get("/:id/avatar", async (c) => {
    const id = c.req.param("id");
    const personaData = await ensurePersonaBio(c.env, id);

    const name = personaData === null ? `Unknown (${id})` : personaData.name;

    return c.html(<span>{name}</span>, 200, {
        "Cache-Control": "max-age=600",
    });
});

// Persona creation
app.post("/new", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

    const body = await c.req.formData();
    const id = crypto.randomUUID();

    const personaData: Persona = {
        id,
        name: body.get("name")?.toString() ?? "New Persona",
        systemPrompt:
            body.get("systemPrompt")?.toString() ??
            "You are a helpful AI assistant.",
        bio: body.get("bio")?.toString()?.trim() || undefined,
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
            <PersonasList
                personas={personas}
                hx-swap-oob="outerHTML:#personas-list"
            />
        </>,
        201,
    );
});

// Persona update
app.put("/:id", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

    const id = c.req.param("id");
    const body = await c.req.formData();
    const existingPersona = await c.env.DESKTOP_DATA.get<Persona>(
        `persona:${id}`,
        { type: "json" },
    );

    if (!existingPersona) {
        return c.html(<MissingPersona personaId={id} />, 404);
    }

    const bioValue = body.get("bio");
    const updatedPersona: Persona = {
        id,
        name: body.get("name")?.toString()?.trim() || existingPersona.name,
        systemPrompt:
            body.get("systemPrompt")?.toString()?.trim() ||
            existingPersona.systemPrompt,
        bio:
            bioValue === null
                ? existingPersona.bio
                : bioValue.toString().trim(),
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
            <PersonasList
                personas={personas}
                hx-swap-oob="outerHTML:#personas-list"
            />
        </>,
        200,
    );
});

app.post("/:id/generate-bio", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

    const id = c.req.param("id");
    const personaData = await ensurePersonaBio(c.env, id, { force: true });

    if (!personaData) {
        return c.html(<MissingPersona personaId={id} />, 404);
    }

    const personas = await getAllPersonas(c.env);
    return c.html(
        <>
            <PersonaForm persona={personaData} />
            <PersonasList
                personas={personas}
                hx-swap-oob="outerHTML:#personas-list"
            />
        </>,
        200,
    );
});

// Persona deletion
app.delete("/:id", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }
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
                <PersonasList
                    personas={personas}
                    hx-swap-oob="outerHTML:#personas-list"
                />
            </>,
            200,
        );
    } catch (error) {
        console.error("Failed to delete persona:", error);
        return c.text("Failed to delete persona", 500);
    }
});

// Export persona as JSON
app.get("/:id/export", async (c) => {
    const id = c.req.param("id");
    const persona = await ensurePersonaBio(c.env, id);

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
app.post("/:id/duplicate", async (c) => {
    const auth = getAuth(c);

    if (!auth?.userId || !auth.isAuthenticated) {
        return new Response("Unauthorized!!", { status: 401 });
    }

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
        bio: existingPersona.bio,
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
                <PersonasList
                    personas={personas}
                    hx-swap-oob="outerHTML:#personas-list"
                />
            </>,
            201,
        );
    } catch (error) {
        console.error("Failed to duplicate persona:", error);
        return c.text("Failed to duplicate persona", 500);
    }
});

export default app;
