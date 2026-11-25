import { CommandButton, Hyperlink, Icon, Text, TextArea } from '@partytown/ui';
import { QueryErrorResetBoundary } from '@tanstack/react-query';
import {
    Component,
    type ErrorInfo,
    type ReactNode,
    useId,
    useState,
} from 'react';
import { ProblemDetailsError } from '../../api/custom-fetch';
import { trackError } from '../../lib/analytics';
import { redirectToSignIn } from '../../lib/clerk-session';

interface InnerProps {
    children: ReactNode;
    onReset?: () => void;
    onClose?: () => void;
    windowTitle?: string;
}

interface InnerState {
    error: Error | null;
}

class ErrorBoundaryInner extends Component<InnerProps, InnerState> {
    state: InnerState = { error: null };

    static getDerivedStateFromError(error: Error): InnerState {
        return { error };
    }

    componentDidCatch(error: Error, info: ErrorInfo) {
        console.error(
            `[WindowErrorBoundary:${this.props.windowTitle ?? 'window'}]`,
            error,
            info,
        );
        if (!(error instanceof ProblemDetailsError)) {
            trackError(error, { window: this.props.windowTitle });
        }
    }

    handleRetry = () => {
        this.props.onReset?.();
        this.setState({ error: null });
    };

    render() {
        if (this.state.error) {
            return (
                <ErrorFallback
                    error={this.state.error}
                    windowTitle={this.props.windowTitle}
                    onRetry={this.handleRetry}
                    onClose={this.props.onClose}
                />
            );
        }
        return this.props.children;
    }
}

interface WindowErrorBoundaryProps {
    children: ReactNode;
    onClose?: () => void;
    windowTitle?: string;
}

export default function WindowErrorBoundary({
    children,
    onClose,
    windowTitle,
}: WindowErrorBoundaryProps) {
    return (
        <QueryErrorResetBoundary>
            {({ reset }) => (
                <ErrorBoundaryInner
                    onReset={reset}
                    onClose={onClose}
                    windowTitle={windowTitle}
                >
                    {children}
                </ErrorBoundaryInner>
            )}
        </QueryErrorResetBoundary>
    );
}

interface FallbackProps {
    error: Error;
    windowTitle?: string;
    onRetry: () => void;
    onClose?: () => void;
}

// Chunk gone after a deploy; lazy() caches the failure, so only reload helps.
const STALE_CHUNK =
    /dynamically imported module|Importing a module script failed|Unable to preload CSS/i;

export function isStaleChunkError(error: Error): boolean {
    return STALE_CHUNK.test(error.message);
}

function summarize(error: Error): { headline: string; status?: number } {
    if (isStaleChunkError(error)) {
        return {
            headline:
                'Proompting was updated since you opened it. Reload to get the new version.',
        };
    }
    if (error instanceof ProblemDetailsError) {
        const status =
            typeof error.status === 'number'
                ? error.status
                : typeof error.status === 'string'
                  ? Number.parseInt(error.status, 10)
                  : undefined;
        return {
            headline: error.message,
            status:
                status !== undefined && !Number.isNaN(status)
                    ? status
                    : undefined,
        };
    }
    return { headline: error.message || 'Unexpected error' };
}

function ErrorFallback({
    error,
    windowTitle,
    onRetry,
    onClose,
}: FallbackProps) {
    const [showDetails, setShowDetails] = useState(false);
    const headingId = useId();
    const bodyId = useId();
    const { headline, status } = summarize(error);
    const isServer = status !== undefined && status >= 500;
    const needsSignIn = status === 401;
    const stale = isStaleChunkError(error);
    const heading = needsSignIn
        ? 'Sign in to see this'
        : windowTitle
          ? `${windowTitle} couldn't load`
          : "This window couldn't load";

    return (
        <div className="flex h-full w-full items-center justify-center p-4">
            <div
                role="alertdialog"
                aria-labelledby={headingId}
                aria-describedby={bodyId}
                className="flex w-full max-w-[400px] flex-col gap-3"
            >
                <div className="flex items-start gap-3">
                    <Icon
                        name={
                            needsSignIn
                                ? 'info'
                                : isServer
                                  ? 'error'
                                  : 'warning'
                        }
                        size={32}
                    />
                    <div className="flex min-w-0 flex-col gap-1">
                        <Text tone={isServer ? 'danger' : 'muted'} strong>
                            {isServer ? 'Server problem' : 'Problem'}
                        </Text>
                        <Text as="h2" id={headingId} size="title" strong>
                            {heading}
                        </Text>
                        <Text as="p" id={bodyId} className="m-0 break-words">
                            {headline}
                        </Text>
                        <Hyperlink
                            className="self-start"
                            aria-expanded={showDetails}
                            onClick={() => setShowDetails((s) => !s)}
                        >
                            {showDetails ? 'Hide details' : 'Show details'}
                        </Hyperlink>
                    </div>
                </div>
                {showDetails && (
                    <TextArea
                        mono
                        readOnly
                        rows={8}
                        aria-label="Error details"
                        value={error.stack ?? String(error)}
                    />
                )}
                <div className="flex justify-end gap-2">
                    <CommandButton
                        isDefault
                        type="button"
                        onClick={
                            needsSignIn
                                ? redirectToSignIn
                                : stale
                                  ? () => window.location.reload()
                                  : onRetry
                        }
                    >
                        {needsSignIn ? 'Sign in' : stale ? 'Reload' : 'Retry'}
                    </CommandButton>
                    {onClose && (
                        <CommandButton onClick={onClose}>Close</CommandButton>
                    )}
                </div>
            </div>
        </div>
    );
}
