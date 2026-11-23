import './story-kit.css';

export const picture = (hue: number) =>
    `data:image/svg+xml,${encodeURIComponent(
        `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><rect width="32" height="32" fill="hsl(${hue} 60% 80%)"/><circle cx="16" cy="12.5" r="6" fill="hsl(${hue} 45% 45%)"/><path d="M4 32c1-9 6-12 12-12s11 3 12 12z" fill="hsl(${hue} 45% 45%)"/></svg>`,
    )}`;
