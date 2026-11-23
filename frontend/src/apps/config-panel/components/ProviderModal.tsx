import { Dialog, Icon } from '@partytown/ui';
import type { ReactNode } from 'react';

export default function ProviderModal({
    title,
    onClose,
    buttons,
    children,
}: {
    title: string;
    onClose: () => void;
    buttons?: ReactNode;
    children: ReactNode;
}) {
    return (
        <Dialog
            title={title}
            icon={<Icon name="tools" />}
            onClose={onClose}
            buttons={buttons}
            className="max-h-[85vh] w-[480px]"
        >
            {children}
        </Dialog>
    );
}
