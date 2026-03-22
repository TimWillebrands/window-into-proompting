import { type ReactNode, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';

export default function CSSIsolation({ children }: { children: ReactNode }) {
    const [mounted, setMounted] = useState(false);

    useEffect(() => {
        setMounted(true);
    }, []);

    if (!mounted) {
        return null;
    }

    return createPortal(
        <div
            style={{
                fontFamily: 'system-ui, -apple-system, sans-serif',
                fontSize: '16px',
                color: 'black',
                lineHeight: '1.5',
            }}
        >
            {children}
        </div>,
        document.body,
    );
}
