import { defineSkin } from '../skin';

export const msn = defineSkin({
    name: 'msn',
    label: 'Messenger',
    titleBar: 'tall',
    maximise: true,
    minSize: { width: 280, height: 360 },
    regions: [
        { name: 'gloss', in: 'titlebar' },
        { name: 'swoosh', in: 'titlebar' },
        { name: 'sky', in: 'frame' },
    ],
});
