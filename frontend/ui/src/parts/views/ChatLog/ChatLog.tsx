import {
    SunkenField,
    type SunkenFieldProps,
} from '../../surfaces/SunkenField/SunkenField';

export type ChatLogProps = Omit<SunkenFieldProps, 'text'>;

export function ChatLog({ className, children, ...rest }: ChatLogProps) {
    return (
        <SunkenField
            role="log"
            tabIndex={0}
            text="reading"
            className={className ? `ui-chatlog ${className}` : 'ui-chatlog'}
            {...rest}
        >
            {children}
        </SunkenField>
    );
}
