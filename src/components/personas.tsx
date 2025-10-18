import { defaultPersonas } from "@/defaultPersonas";
import { WindowContainer } from "./window";

export type PersonaMetadata = {
    id: string;
    name: string;
};

export type Persona = PersonaMetadata & {
    systemPrompt: string;
};

// Component for just the personas list content (for OOB swaps)
export function PersonasList({ 
    personas, 
    "hx-swap-oob": hxSwapOob
}: { 
    personas: PersonaMetadata[];
    "hx-swap-oob"?: string;
}) {
    return (
        <div
            className="w-1/3 border-r border-gray-300 overflow-y-auto bg-gradient-to-b from-gray-50 to-white"
            id="personas-list"
            hx-swap-oob={hxSwapOob}
        >
            <div className="p-4">
                <div className="flex items-center justify-between mb-4">
                    <h3 className="font-bold text-gray-700 text-sm uppercase tracking-wide">
                        Available Personas
                    </h3>
                    <span className="text-xs text-white bg-blue-600 px-2.5 py-1 rounded-full shadow-sm font-semibold">
                        {personas.length}
                    </span>
                </div>

                {personas.length === 0 ? (
                    <div className="text-gray-500 text-center py-10 border-2 border-dashed border-gray-300 rounded-lg bg-white">
                        <p className="text-sm font-medium">No personas yet.</p>
                        <p className="text-xs mt-2">
                            Create your first persona to get started!
                        </p>
                    </div>
                ) : (
                    <div className="space-y-2.5">
                        {personas.map((persona: PersonaMetadata) => (
                            <div
                                key={persona.id}
                                className="group border-2 border-gray-300 rounded-lg p-3 hover:bg-blue-50 hover:border-blue-400 hover:shadow-md cursor-pointer transition-all duration-200 bg-white shadow-sm"
                                hx-get={`/personas/${persona.id}`}
                                hx-target="#persona-editor"
                                hx-swap="innerHTML"
                            >
                                <div className="flex items-center space-x-3">
                                    <img
                                        src={`https://robohash.org/${persona.id}.png?size=40x40`}
                                        alt={`${persona.name} avatar`}
                                        className="w-10 h-10 rounded-full border-2 border-gray-300 group-hover:border-blue-400 transition-colors shadow-sm"
                                    />
                                    <div className="flex-1 min-w-0">
                                        <h4 className="font-semibold text-gray-800 truncate">
                                            {persona.name}
                                        </h4>
                                        <p className="text-xs text-gray-500 truncate">
                                            ID: {persona.id.slice(0, 8)}...
                                        </p>
                                    </div>
                                    <div className="opacity-0 group-hover:opacity-100 transition-opacity">
                                        <svg
                                            className="w-5 h-5 text-blue-500"
                                            fill="none"
                                            stroke="currentColor"
                                            viewBox="0 0 24 24"
                                        >
                                            <title>Edit persona</title>
                                            <path
                                                strokeLinecap="round"
                                                strokeLinejoin="round"
                                                strokeWidth="2"
                                                d="M9 5l7 7-7 7"
                                            />
                                        </svg>
                                    </div>
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </div>
    );
}

export function Personas({ personas }: { personas: PersonaMetadata[] }) {
    return (
        <WindowContainer id="Personas" title="👤 Persona Manager" url="/personas">
            <div className="window-body flex flex-col h-full">
                {/* Header */}
                <div className="p-4 border-b border-gray-300 bg-gradient-to-r from-blue-50 to-indigo-50">
                    <div className="flex items-center justify-between">
                        <h2 className="text-lg font-bold text-blue-900">
                            📋 Persona Management
                        </h2>
                        <div className="flex items-center space-x-2">
                            <button
                                type="button"
                                hx-get="/personas/templates"
                                hx-target="#template-menu"
                                hx-swap="innerHTML"
                            >
                                📋 Templates
                            </button>
                            <button
                                type="button"
                                hx-post="/personas/new"
                                hx-target="#persona-editor"
                                hx-swap="innerHTML"
                            >
                                ➕ New Persona
                            </button>
                        </div>
                    </div>
                    <div id="template-menu" className="mt-2"></div>
                </div>

                <div className="flex h-full">
                    {/* Personas List */}
                    <PersonasList personas={personas} />

                    {/* Persona Editor */}
                    <div
                        className="flex-1 overflow-y-auto bg-gradient-to-br from-gray-50 to-blue-50/30"
                        id="persona-editor"
                    >
                        <div className="p-8 h-full flex items-center justify-center">
                            <div className="text-center text-gray-500">
                                <div className="text-6xl mb-4">👤</div>
                                <h3 className="text-xl font-semibold mb-2 text-gray-700">
                                    Select a Persona
                                </h3>
                                <p className="text-sm text-gray-500">
                                    Choose a persona from the list to edit its
                                    details
                                </p>
                            </div>
                        </div>
                    </div>
                </div>

                {/* Status Bar */}
                <div className="status-bar">
                    <p className="status-bar-field">🟢 Ready</p>
                    <p className="status-bar-field">{personas.length} persona{personas.length !== 1 ? 's' : ''}</p>
                    <p className="status-bar-field">💾 Auto-saved</p>
                </div>
            </div>
        </WindowContainer>
    );
}

export function PersonaTemplateMenu() {
    return (
        <div className="bg-white border-2 border-gray-300 rounded-lg shadow-lg p-3 max-w-md">
            <h4 className="font-semibold text-gray-700 text-sm mb-3">
                📋 Quick Templates
            </h4>
            <div className="space-y-1.5">
                {defaultPersonas.map((template) => (
                    <button
                        key={template.name}
                        type="button"
                        className="w-full text-left px-3 py-2.5 text-sm hover:bg-blue-50 hover:border-blue-300 rounded-md border border-gray-200 transition-colors"
                        hx-post="/personas/new"
                        hx-target="#persona-editor"
                        hx-swap="innerHTML"
                        hx-vals={JSON.stringify({
                            name: template.name,
                            systemPrompt: template.systemPrompt,
                        })}
                        hx-on="click: document.getElementById('template-menu').innerHTML = ''"
                    >
                        <div className="font-medium text-gray-800">{template.name}</div>
                        <div className="text-xs text-gray-500 truncate mt-0.5">
                            {template.systemPrompt.slice(0, 60)}...
                        </div>
                    </button>
                ))}
            </div>
            <button
                type="button"
                className="w-full text-center px-3 py-2 text-sm text-gray-600 hover:bg-gray-50 rounded-md mt-2 border-t border-gray-200"
                hx-on="click: document.getElementById('template-menu').innerHTML = ''"
            >
                Close
            </button>
        </div>
    );
}

export function PersonaForm({ persona }: { persona: Persona }) {
    const wordCount = persona.systemPrompt
        .trim()
        .split(/\s+/)
        .filter((word) => word.length > 0).length;

    return (
        <div className="p-6 h-full bg-gradient-to-br from-white to-blue-50/20 overflow-y-auto">
            <div className="max-w-2xl mx-auto">
                {/* Header */}
                <div className="flex items-center space-x-4 mb-6 pb-4 border-b-2 border-blue-200">
                    <img
                        src={`https://robohash.org/${persona.id}.png?size=80x80`}
                        alt={`${persona.name} avatar`}
                        className="w-20 h-20 rounded-full border-4 border-blue-200 shadow-lg"
                    />
                    <div>
                        <h2 className="text-2xl font-bold text-gray-800">
                            ✏️ Edit Persona
                        </h2>
                        <p className="text-sm text-gray-500">ID: {persona.id}</p>
                    </div>
                </div>

                {/* Form */}
                <form
                    hx-put={`/personas/${persona.id}`}
                    hx-target="#persona-editor"
                    hx-swap="innerHTML"
                    className="space-y-5"
                >
                    {/* Name Field */}
                    <div>
                        <label
                            htmlFor="name"
                            className="block text-sm font-semibold text-gray-700 mb-2"
                        >
                            Persona Name
                        </label>
                        <input
                            type="text"
                            id="name"
                            name="name"
                            value={persona.name}
                            className="w-full px-4 py-3 border-2 border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 transition-all"
                            placeholder="Enter persona name..."
                            required
                        />
                    </div>

                    {/* System Prompt Field */}
                    <div>
                        <div className="flex items-center justify-between mb-2">
                            <label
                                htmlFor="systemPrompt"
                                className="block text-sm font-semibold text-gray-700"
                            >
                                System Prompt
                            </label>
                            <span className="text-xs text-white bg-blue-600 px-2.5 py-1 rounded-full shadow-sm font-semibold">
                                {wordCount} words
                            </span>
                        </div>
                        <textarea
                            id="systemPrompt"
                            name="systemPrompt"
                            rows={12}
                            className="w-full px-4 py-3 border-2 border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 transition-all resize-y font-mono text-sm"
                            placeholder="Enter the system prompt that defines this persona's behavior..."
                            required
                        >
                            {persona.systemPrompt}
                        </textarea>
                        <p className="mt-2 text-xs text-gray-600">
                            💡 This prompt defines how the AI will behave when using this persona.
                        </p>
                    </div>

                    {/* Action Buttons */}
                    <div className="flex items-center justify-between pt-4 border-t border-gray-300">
                        <div className="flex space-x-2">
                            <button
                                type="button"
                                hx-delete={`/personas/${persona.id}`}
                                hx-target="#persona-editor"
                                hx-swap="innerHTML"
                                hx-confirm="Are you sure you want to delete this persona? This action cannot be undone."
                            >
                                🗑️ Delete
                            </button>
                            <button
                                type="button"
                                hx-post={`/personas/${persona.id}/duplicate`}
                                hx-target="#persona-editor"
                                hx-swap="innerHTML"
                            >
                                📄 Duplicate
                            </button>
                        </div>

                        <div className="flex space-x-3">
                            <button
                                type="button"
                                hx-get="/personas/blank"
                                hx-target="#persona-editor"
                                hx-swap="innerHTML"
                            >
                                Cancel
                            </button>
                            <button type="submit">💾 Save Changes</button>
                        </div>
                    </div>
                </form>

                {/* Preview Section */}
                <div className="mt-8 p-4 bg-gradient-to-br from-blue-50 to-indigo-50 rounded-lg border-2 border-blue-200 shadow-sm">
                    <h3 className="font-semibold text-gray-700 mb-3 text-sm">
                        👁️ Preview
                    </h3>
                    <div className="flex items-center space-x-3 p-3 bg-white rounded-lg border border-gray-300 shadow-sm">
                        <img
                            src={`https://robohash.org/${persona.id}.png?size=40x40`}
                            alt={`${persona.name} avatar`}
                            className="w-10 h-10 rounded-full border-2 border-gray-300"
                        />
                        <div className="flex-1 min-w-0">
                            <p className="font-semibold text-gray-800">
                                {persona.name}
                            </p>
                            <p className="text-xs text-gray-600 truncate">
                                {persona.systemPrompt.slice(0, 100)}
                                {persona.systemPrompt.length > 100 && "..."}
                            </p>
                        </div>
                        <div className="text-right">
                            <div className="text-xs text-gray-500">
                                {wordCount} words
                                <div className="mt-1">
                                    {wordCount < 50 && (
                                        <span
                                            className="inline-block w-2.5 h-2.5 bg-red-500 rounded-full"
                                            title="Consider adding more detail"
                                        ></span>
                                    )}
                                    {wordCount >= 50 && wordCount <= 200 && (
                                        <span
                                            className="inline-block w-2.5 h-2.5 bg-green-500 rounded-full"
                                            title="Good length"
                                        ></span>
                                    )}
                                    {wordCount > 200 && (
                                        <span
                                            className="inline-block w-2.5 h-2.5 bg-yellow-500 rounded-full"
                                            title="Consider making more concise"
                                        ></span>
                                    )}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    );
}

export function PersonaBlank() {
    return (
        <div className="p-8 h-full flex items-center justify-center bg-gradient-to-br from-gray-50 to-blue-50/30">
            <div className="text-center text-gray-500">
                <div className="text-6xl mb-4">👤</div>
                <h3 className="text-xl font-semibold mb-2 text-gray-700">Select a Persona</h3>
                <p className="text-sm text-gray-500">
                    Choose a persona from the list to edit its details
                </p>
            </div>
        </div>
    );
}

export function MissingPersona({ personaId }: { personaId: string }) {
    return (
        <div className="p-8 h-full flex items-center justify-center bg-gradient-to-br from-gray-50 to-red-50/30">
            <div className="text-center">
                <div className="text-6xl mb-4">❗</div>
                <h3 className="text-xl font-semibold text-red-600 mb-2">
                    Persona Not Found
                </h3>
                <p className="text-sm text-gray-600 mb-4">
                    The persona with ID{" "}
                    <code className="bg-gray-100 px-2 py-1 rounded border border-gray-300">
                        {personaId}
                    </code>{" "}
                    could not be found.
                </p>
                <button
                    type="button"
                    hx-get="/personas"
                    hx-target="#Personas"
                    hx-swap="outerHTML"
                >
                    ← Back to Personas
                </button>
            </div>
        </div>
    );
}

export function PersonaDeleted() {
    return (
        <div className="p-8 h-full flex items-center justify-center bg-gradient-to-br from-gray-50 to-green-50/30">
            <div className="text-center">
                <div className="text-6xl mb-4">✅</div>
                <h3 className="text-xl font-semibold text-green-600 mb-2">
                    Persona Deleted
                </h3>
                <p className="text-sm text-gray-600 mb-4">
                    The persona has been successfully deleted.
                </p>
                <button
                    type="button"
                    hx-get="/personas"
                    hx-target="#Personas"
                    hx-swap="outerHTML"
                >
                    ← Back to Personas
                </button>
            </div>
        </div>
    );
}
