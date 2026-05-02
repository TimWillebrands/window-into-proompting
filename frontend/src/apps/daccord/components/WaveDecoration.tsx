import { useId } from 'react';

interface WaveDecorationProps {
    variant?: 'card' | 'panel' | 'profile';
    className?: string;
}

export default function WaveDecoration({
    variant = 'card',
    className = '',
}: WaveDecorationProps) {
    const baseId = useId();
    const gradA = `${baseId}-a`;
    const gradB = `${baseId}-b`;

    if (variant === 'panel') {
        return (
            <div
                aria-hidden
                className={`pointer-events-none absolute inset-0 overflow-hidden ${className}`}
            >
                <svg
                    role="presentation"
                    viewBox="0 0 300 800"
                    preserveAspectRatio="xMidYMid slice"
                    className="absolute inset-0 h-full w-full mix-blend-overlay opacity-70"
                >
                    <defs>
                        <linearGradient id={gradA} x1="0" y1="0" x2="1" y2="1">
                            <stop
                                offset="0%"
                                stopColor="white"
                                stopOpacity="0.7"
                            />
                            <stop
                                offset="100%"
                                stopColor="white"
                                stopOpacity="0"
                            />
                        </linearGradient>
                        <linearGradient id={gradB} x1="0" y1="1" x2="1" y2="0">
                            <stop
                                offset="0%"
                                stopColor="white"
                                stopOpacity="0.5"
                            />
                            <stop
                                offset="100%"
                                stopColor="white"
                                stopOpacity="0"
                            />
                        </linearGradient>
                    </defs>
                    <path
                        d="M-40,560 C 60,420 180,640 340,500 L340,820 L-40,820 Z"
                        fill={`url(#${gradA})`}
                    />
                    <path
                        d="M-40,640 C 80,540 200,720 340,580 L340,820 L-40,820 Z"
                        fill={`url(#${gradB})`}
                    />
                    <path
                        d="M-20,720 C 100,640 220,780 340,700"
                        fill="none"
                        stroke="white"
                        strokeOpacity="0.55"
                        strokeWidth="1.5"
                    />
                </svg>
                <span className="absolute right-3 top-6 text-base text-white/80 drop-shadow">
                    ✦
                </span>
                <span className="absolute right-8 top-14 text-[10px] text-white/70">
                    ✦
                </span>
                <span className="absolute left-6 bottom-24 text-sm text-white/70 drop-shadow">
                    ✨
                </span>
            </div>
        );
    }

    if (variant === 'profile') {
        return (
            <div
                aria-hidden
                className={`pointer-events-none absolute inset-0 overflow-hidden ${className}`}
            >
                <svg
                    role="presentation"
                    viewBox="0 0 300 220"
                    preserveAspectRatio="xMidYMid slice"
                    className="absolute inset-0 h-full w-full mix-blend-overlay opacity-90"
                >
                    <defs>
                        <linearGradient id={gradA} x1="0" y1="0" x2="1" y2="1">
                            <stop
                                offset="0%"
                                stopColor="white"
                                stopOpacity="0.9"
                            />
                            <stop
                                offset="100%"
                                stopColor="white"
                                stopOpacity="0"
                            />
                        </linearGradient>
                    </defs>
                    <path
                        d="M-30,120 C 60,40 200,200 340,80"
                        fill="none"
                        stroke={`url(#${gradA})`}
                        strokeWidth="2"
                    />
                    <path
                        d="M-30,150 C 80,80 220,220 340,120"
                        fill="none"
                        stroke="white"
                        strokeOpacity="0.45"
                        strokeWidth="1.25"
                    />
                    <path
                        d="M-30,180 C 60,140 220,250 340,170"
                        fill="none"
                        stroke="white"
                        strokeOpacity="0.3"
                        strokeWidth="1"
                    />
                </svg>
                <span className="absolute right-4 top-3 text-lg text-white drop-shadow-[0_0_4px_rgba(255,255,255,0.8)]">
                    ✨
                </span>
                <span className="absolute right-10 top-9 text-[10px] text-white/85">
                    ✦
                </span>
                <span className="absolute left-5 top-6 text-[11px] text-white/80">
                    ✦
                </span>
            </div>
        );
    }

    return (
        <div
            aria-hidden
            className={`pointer-events-none absolute inset-0 overflow-hidden ${className}`}
        >
            <svg
                role="presentation"
                viewBox="0 0 400 220"
                preserveAspectRatio="xMidYMid slice"
                className="absolute inset-0 h-full w-full mix-blend-overlay opacity-80"
            >
                <defs>
                    <linearGradient id={gradA} x1="0" y1="0" x2="1" y2="1">
                        <stop offset="0%" stopColor="white" stopOpacity="0.8" />
                        <stop offset="100%" stopColor="white" stopOpacity="0" />
                    </linearGradient>
                </defs>
                <path
                    d="M-40,140 C 80,60 220,220 460,90"
                    fill="none"
                    stroke={`url(#${gradA})`}
                    strokeWidth="2"
                />
                <path
                    d="M-40,170 C 100,100 240,240 460,130"
                    fill="none"
                    stroke="white"
                    strokeOpacity="0.4"
                    strokeWidth="1.25"
                />
                <path
                    d="M-40,200 C 60,160 280,260 460,180"
                    fill="none"
                    stroke="white"
                    strokeOpacity="0.25"
                    strokeWidth="1"
                />
            </svg>
            <span className="pointer-events-none absolute right-4 top-3 text-base text-white/90 drop-shadow-[0_0_4px_rgba(255,255,255,0.7)]">
                ✨
            </span>
            <span className="pointer-events-none absolute right-10 top-8 text-[10px] text-white/75">
                ✦
            </span>
        </div>
    );
}
