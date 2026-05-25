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
        const gradC = `${baseId}-c`;
        return (
            <div
                aria-hidden
                className={`pointer-events-none absolute inset-0 overflow-hidden ${className}`}
            >
                <svg
                    role="presentation"
                    viewBox="0 0 300 800"
                    preserveAspectRatio="xMidYMid slice"
                    className="absolute inset-0 h-full w-full"
                >
                    <defs>
                        <linearGradient
                            id={gradA}
                            x1="0"
                            y1="0"
                            x2="0.6"
                            y2="1"
                        >
                            <stop
                                offset="0%"
                                stopColor="#a5b4fc"
                                stopOpacity="0.85"
                            />
                            <stop
                                offset="55%"
                                stopColor="#818cf8"
                                stopOpacity="0.9"
                            />
                            <stop
                                offset="100%"
                                stopColor="#7c3aed"
                                stopOpacity="0.95"
                            />
                        </linearGradient>
                        <linearGradient id={gradB} x1="0" y1="0" x2="0" y2="1">
                            <stop
                                offset="0%"
                                stopColor="white"
                                stopOpacity="0.55"
                            />
                            <stop
                                offset="100%"
                                stopColor="white"
                                stopOpacity="0"
                            />
                        </linearGradient>
                        <linearGradient
                            id={gradC}
                            x1="0"
                            y1="0"
                            x2="1"
                            y2="0.6"
                        >
                            <stop
                                offset="0%"
                                stopColor="#c7d2fe"
                                stopOpacity="0.55"
                            />
                            <stop
                                offset="100%"
                                stopColor="#ddd6fe"
                                stopOpacity="0"
                            />
                        </linearGradient>
                    </defs>
                    {/* faint echo wash high above main wave */}
                    <path
                        d="M-40,640 C 70,580 160,700 320,610 L320,660 C 200,720 80,640 -40,700 Z"
                        fill={`url(#${gradC})`}
                    />
                    {/* main wave — bottom flourish only */}
                    <path
                        d="M-40,700 C 60,630 140,750 220,680 C 270,640 300,650 340,640 L340,820 L-40,820 Z"
                        fill={`url(#${gradA})`}
                    />
                    {/* crest sheen */}
                    <path
                        d="M-40,700 C 60,630 140,750 220,680 C 270,640 300,650 340,640 L340,740 C 250,720 150,790 60,750 Z"
                        fill={`url(#${gradB})`}
                    />
                    {/* highlight ribbons inside wave */}
                    <path
                        d="M-20,760 C 80,700 180,810 340,720"
                        fill="none"
                        stroke="white"
                        strokeOpacity="0.4"
                        strokeWidth="1.5"
                    />
                    <path
                        d="M-20,795 C 100,750 220,830 340,765"
                        fill="none"
                        stroke="white"
                        strokeOpacity="0.25"
                        strokeWidth="1.25"
                    />
                </svg>
                <span className="absolute left-5 bottom-12 text-sm text-white/90 drop-shadow">
                    ✨
                </span>
                <span className="absolute left-12 bottom-24 text-[10px] text-white/75">
                    ✦
                </span>
                <span className="absolute right-10 bottom-16 text-[11px] text-white/80">
                    ✦
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
