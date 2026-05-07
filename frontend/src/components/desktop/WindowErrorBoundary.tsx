import { QueryErrorResetBoundary } from '@tanstack/react-query';
import {
    Component,
    type ErrorInfo,
    type ReactNode,
    useId,
    useState,
} from 'react';
import { ProblemDetailsError } from '../../api/custom-fetch';

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

function summarize(error: Error): { headline: string; status?: number } {
    if (error instanceof ProblemDetailsError) {
        const status =
            typeof error.status === 'number'
                ? error.status
                : typeof error.status === 'string'
                  ? Number.parseInt(error.status, 10)
                  : undefined;
        return {
            headline: error.message,
            status: status !== undefined && !Number.isNaN(status) ? status : undefined,
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
    const { headline, status } = summarize(error);
    const isServer = status !== undefined && status >= 500;
    const heading = windowTitle
        ? `${windowTitle} couldn't load`
        : "This window couldn't load";

    return (
        <div className="flex h-full w-full items-center justify-center p-4 text-slate-800 dark:text-slate-100">
            <div
                role="alertdialog"
                aria-labelledby={headingId}
                className="w-full max-w-md overflow-hidden rounded-2xl bg-gradient-to-b from-white/90 to-blue-50/80 ring-1 ring-white/70 shadow-[0_10px_30px_-8px_rgba(31,55,148,0.45)] backdrop-blur-md dark:from-slate-800/90 dark:to-slate-900/85 dark:ring-white/10"
            >
                <div className="flex items-center gap-2 bg-gradient-to-r from-rose-500/90 to-rose-600/90 px-4 py-2 text-[12px] font-semibold uppercase tracking-wide text-white">
                    <span aria-hidden>⚠</span>
                    <span>{isServer ? 'Server problem' : 'Problem'}</span>
                </div>

                <div className="px-5 pt-4 pb-2">
                    <div
                        id={headingId}
                        className="mb-1 text-[14px] font-semibold leading-snug"
                    >
                        {heading}
                    </div>
                    <div className="break-words text-[12px] leading-relaxed text-slate-700 dark:text-slate-300">
                        {headline}
                    </div>
                    <button
                        type="button"
                        onClick={() => setShowDetails((s) => !s)}
                        className="mt-2 text-[11px] font-medium text-blue-700 underline-offset-2 hover:underline dark:text-blue-300"
                    >
                        {showDetails ? 'Hide details' : 'Show details'}
                    </button>
                    {showDetails && (
                        <pre className="mt-2 max-h-40 overflow-auto rounded-lg bg-black/5 p-2 text-[10.5px] leading-snug whitespace-pre-wrap break-words font-mono text-slate-700 ring-1 ring-black/5 dark:bg-white/5 dark:text-slate-200 dark:ring-white/5">
                            {error.stack ?? String(error)}
                        </pre>
                    )}
                </div>

                <div className="flex justify-end gap-2 border-t border-white/60 px-4 py-3 dark:border-white/10">
                    <button
                        type="button"
                        onClick={onRetry}
                        className="rounded-md bg-blue-600 px-3 py-1 text-[12px] font-medium text-white shadow-sm ring-1 ring-blue-700/50 transition-colors hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-400"
                    >
                        Retry
                    </button>
                    {onClose && (
                        <button
                            type="button"
                            onClick={onClose}
                            className="rounded-md bg-white/70 px-3 py-1 text-[12px] font-medium text-slate-700 ring-1 ring-black/10 transition-colors hover:bg-white dark:bg-white/10 dark:text-slate-100 dark:ring-white/10 dark:hover:bg-white/20"
                        >
                            Close
                        </button>
                    )}
                </div>
            </div>
        </div>
    );
}
