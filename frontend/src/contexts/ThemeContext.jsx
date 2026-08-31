import { createContext, useContext, useEffect, useMemo, useState } from 'react';

const ThemeContext = createContext(null);
const STORAGE_KEY = 'finova-theme';

export function ThemeProvider({ children }) {
    const [preference, setPreference] = useState(
        () => localStorage.getItem(STORAGE_KEY) || 'system'
    );
    const [systemDark, setSystemDark] = useState(
        () => window.matchMedia('(prefers-color-scheme: dark)').matches
    );

    useEffect(() => {
        const media = window.matchMedia('(prefers-color-scheme: dark)');
        const onChange = (event) => setSystemDark(event.matches);
        media.addEventListener('change', onChange);
        return () => media.removeEventListener('change', onChange);
    }, []);

    const resolved =
        preference === 'system' ? (systemDark ? 'dark' : 'light') : preference;

    useEffect(() => {
        document.documentElement.dataset.theme = resolved;
        document.documentElement.style.colorScheme = resolved;
        localStorage.setItem(STORAGE_KEY, preference);
    }, [preference, resolved]);

    const value = useMemo(
        () => ({ preference, setPreference, resolved }),
        [preference, resolved]
    );
    return (
        <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
    );
}

export function useTheme() {
    const value = useContext(ThemeContext);
    if (!value) throw new Error('useTheme must be used inside ThemeProvider');
    return value;
}
