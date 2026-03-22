export default function DesktopWallpaper() {
    return (
        <div
            className="absolute inset-0 bg-gradient-to-br from-slate-300 via-slate-400 to-slate-300"
            style={{
                backgroundImage: "url('/img/blissful_penger.webp')",
                backgroundSize: 'cover',
                backgroundPosition: 'center',
            }}
        />
    );
}
