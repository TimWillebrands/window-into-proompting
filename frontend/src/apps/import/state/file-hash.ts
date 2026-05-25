/**
 * SHA-1 of (systemInstruction + every chunk text, joined by `|`). Used as the
 * localStorage key for an import draft. Two different files almost never collide;
 * the same file re-picked across browser sessions deterministically lands on the
 * same key so we can offer "restore prior draft?".
 */
export async function computeFileHash(
    systemInstruction: string,
    chunks: ReadonlyArray<{ text: string }>,
): Promise<string> {
    const blob = `${systemInstruction}\n\n${chunks.map((c) => c.text).join('|')}`;
    const enc = new TextEncoder().encode(blob);
    const buf = await crypto.subtle.digest('SHA-1', enc);
    const bytes = new Uint8Array(buf);
    let hex = '';
    for (let i = 0; i < bytes.length; i++) {
        hex += bytes[i].toString(16).padStart(2, '0');
    }
    return hex;
}
