export default function FinovaLogo({ compact = false }) {
    return (
        <div className="finova-logo" aria-label="Finova">
            <svg viewBox="0 0 32 32" aria-hidden="true">
                <defs>
                    <linearGradient id="finova-a" x1="0" x2="1" y1="0" y2="1">
                        <stop stopColor="#0a77ff" />
                        <stop offset="1" stopColor="#21b7ff" />
                    </linearGradient>
                    <linearGradient id="finova-b" x1="0" x2="1" y1="1" y2="0">
                        <stop stopColor="#28d2bd" />
                        <stop offset="1" stopColor="#68e0ff" />
                    </linearGradient>
                </defs>
                <path fill="url(#finova-a)" d="M4 9.2C4 5.2 6.8 3 10.6 3h6.1v9.2h-4.1v7.1H4V9.2Z" />
                <path fill="url(#finova-b)" d="M15.3 12.2h5c4.8 0 7.7 2.7 7.7 7.1v2.4c0 4.5-2.9 7.3-7.7 7.3H15v-9.7h4.4v-7.1h-4.1Z" />
                <path fill="#fff" fillOpacity=".86" d="M12.6 12.2h6.8v7.1h-6.8z" />
            </svg>
            {!compact && <span>Finova</span>}
        </div>
    );
}
