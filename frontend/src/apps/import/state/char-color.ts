/**
 * Per-character HSL color via golden-ratio hue stepping. The first character is
 * placed at hue=0; each subsequent character at the previous hue + 137.508° (the
 * golden angle), which maximises perceptual separation for any sequence length
 * up to ~25 distinct chars before visible collisions appear.
 *
 * Saturation + lightness are fixed so that swatches read as a coherent palette
 * even when rendered side-by-side in the col4 character cards.
 */
const HUE_STEP = 137.508;

export function colorForCharIndex(n: number): string {
    const hue = (((n * HUE_STEP) % 360) + 360) % 360;
    return `hsl(${hue.toFixed(1)} 70% 55%)`;
}
