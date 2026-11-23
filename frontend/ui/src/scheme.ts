export const schemes = ['luna-blue', 'noir'] as const;

export type Scheme = (typeof schemes)[number];

export const defaultScheme: Scheme = 'luna-blue';

const isScheme = (value: string | null | undefined): value is Scheme =>
    schemes.some((scheme) => scheme === value);

const rootOf = (root?: HTMLElement) =>
    root ??
    (typeof document === 'undefined' ? undefined : document.documentElement);

export const setScheme = (scheme: Scheme, root?: HTMLElement) => {
    rootOf(root)?.setAttribute('data-ui-scheme', scheme);
};

export const getScheme = (root?: HTMLElement): Scheme => {
    const value = rootOf(root)?.getAttribute('data-ui-scheme');
    return isScheme(value) ? value : defaultScheme;
};
