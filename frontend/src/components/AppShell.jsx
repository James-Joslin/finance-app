import { createElement, useEffect, useMemo, useRef, useState } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import {
    Bell, CalendarRange, ChartNoAxesCombined, ChevronRight, CircleHelp,
    LayoutDashboard, Menu, Moon, PiggyBank, ReceiptText, Search, Settings,
    Sun, X,
} from 'lucide-react';
import FinovaLogo from './FinovaLogo';
import { useTheme } from '../contexts/ThemeContext';
import { searchFinova, useDashboard } from '../lib/queries';

const navigation = [
    { to: '/', label: 'Overview', icon: LayoutDashboard },
    { to: '/transactions', label: 'Transactions', icon: ReceiptText },
    { to: '/plan', label: 'Plan', icon: CalendarRange },
    { to: '/goals', label: 'Goals', icon: PiggyBank },
    { to: '/insights', label: 'Insights', icon: ChartNoAxesCombined },
];

const titles = {
    '/': ['Overview', 'A clear view of your household money.'],
    '/transactions': ['Transactions', 'Review, organise and import activity.'],
    '/plan': ['Plan', 'Protect today and prepare for what is next.'],
    '/goals': ['Savings goals', 'Turn plans into visible progress.'],
    '/insights': ['Insights', 'Understand the patterns behind your money.'],
    '/settings': ['Settings & accounts', 'Shape Finova around your household.'],
};

export default function AppShell() {
    const location = useLocation();
    const navigate = useNavigate();
    const { resolved, setPreference } = useTheme();
    const dashboard = useDashboard();
    const [mobileMenu, setMobileMenu] = useState(false);
    const [alertsOpen, setAlertsOpen] = useState(false);
    const [searchOpen, setSearchOpen] = useState(false);
    const page = titles[location.pathname] || titles['/'];

    const toggleTheme = () => setPreference(resolved === 'dark' ? 'light' : 'dark');

    return (
        <div className="app-shell">
            <aside className={'side-rail ' + (mobileMenu ? 'mobile-open' : '')}>
                <div className="rail-top">
                    <FinovaLogo />
                    <button className="icon-button mobile-rail-close" onClick={() => setMobileMenu(false)} aria-label="Close menu"><X /></button>
                </div>
                <nav className="primary-nav" aria-label="Primary">
                    {navigation.map((item) => <NavItem key={item.to} {...item} onClick={() => setMobileMenu(false)} />)}
                </nav>
                <nav className="secondary-nav" aria-label="Support">
                    <NavItem to="/settings" label="Settings" icon={Settings} onClick={() => setMobileMenu(false)} />
                    <a href="https://github.com/" target="_blank" rel="noreferrer"><CircleHelp /><span>Help & support</span></a>
                </nav>
                <div className="household-profile">
                    <span className="avatar">MH</span>
                    <span><strong>{dashboard.data?.householdName || 'Matthews Household'}</strong><small>Private workspace</small></span>
                </div>
            </aside>

            {mobileMenu && <button className="rail-scrim" aria-label="Close menu" onClick={() => setMobileMenu(false)} />}

            <main className="app-main">
                <header className="topbar">
                    <button className="icon-button menu-button" onClick={() => setMobileMenu(true)} aria-label="Open menu"><Menu /></button>
                    <div className="page-heading">
                        <h1>{page[0]}</h1>
                        <p>{page[1]}</p>
                    </div>
                    <div className="topbar-actions">
                        <button className="global-search-trigger" onClick={() => setSearchOpen(true)}>
                            <Search /><span>Search anything…</span><kbd>⌘ K</kbd>
                        </button>
                        <button className="icon-button" onClick={toggleTheme} aria-label={'Switch to ' + (resolved === 'dark' ? 'light' : 'dark') + ' mode'}>
                            {resolved === 'dark' ? <Sun /> : <Moon />}
                        </button>
                        <div className="popover-wrap">
                            <button className="icon-button notification-button" onClick={() => setAlertsOpen(!alertsOpen)} aria-label="Household alerts">
                                <Bell />{dashboard.data?.alerts?.length > 0 && <span />}
                            </button>
                            {alertsOpen && <Alerts alerts={dashboard.data?.alerts || []} close={() => setAlertsOpen(false)} />}
                        </div>
                        <button className="avatar avatar-button" onClick={() => navigate('/settings')} aria-label="Open household settings">MH</button>
                    </div>
                </header>
                <div className="page-content"><Outlet /></div>
            </main>

            <nav className="bottom-nav" aria-label="Mobile navigation">
                {navigation.map((item) => <NavItem key={item.to} {...item} />)}
            </nav>

            <CommandSearch open={searchOpen} onClose={() => setSearchOpen(false)} />
        </div>
    );
}

function NavItem({ to, label, icon, onClick }) {
    return (
        <NavLink to={to} end={to === '/'} onClick={onClick}>
            {createElement(icon)}<span>{label}</span>
        </NavLink>
    );
}

function Alerts({ alerts, close }) {
    return (
        <div className="popover alerts-popover">
            <div className="popover-title"><strong>Household alerts</strong><button className="icon-button" onClick={close}><X /></button></div>
            {alerts.length === 0
                ? <p className="popover-empty">Everything looks calm. Finova will flag buffers, budgets, and dates here.</p>
                : alerts.map((alert) => <div className="alert-row" key={alert}><span className="alert-dot" /><p>{alert}</p></div>)}
        </div>
    );
}

function CommandSearch({ open, onClose }) {
    const navigate = useNavigate();
    const inputRef = useRef(null);
    const [query, setQuery] = useState('');
    const [results, setResults] = useState([]);
    const [loading, setLoading] = useState(false);

    useEffect(() => {
        const onKey = (event) => {
            if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
                event.preventDefault();
                if (open) onClose();
            }
            if (event.key === 'Escape' && open) onClose();
        };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [open, onClose]);

    useEffect(() => {
        if (open) window.setTimeout(() => inputRef.current?.focus(), 30);
        else { setQuery(''); setResults([]); }
    }, [open]);

    useEffect(() => {
        if (query.trim().length < 2) { setResults([]); return; }
        const timer = window.setTimeout(async () => {
            setLoading(true);
            try { setResults(await searchFinova(query)); }
            catch { setResults([]); }
            finally { setLoading(false); }
        }, 220);
        return () => window.clearTimeout(timer);
    }, [query]);

    const groups = useMemo(() => results.reduce((map, item) => {
        map[item.type] = [...(map[item.type] || []), item];
        return map;
    }, {}), [results]);

    if (!open) return null;
    return (
        <div className="command-backdrop" onMouseDown={onClose}>
            <section className="command-dialog" role="dialog" aria-modal="true" aria-label="Search Finova" onMouseDown={(event) => event.stopPropagation()}>
                <div className="command-input"><Search /><input ref={inputRef} value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search transactions, goals, accounts and plans…" /><kbd>Esc</kbd></div>
                <div className="command-results">
                    {query.length < 2 && <p>Type at least two characters to search the Matthews Household.</p>}
                    {loading && <p>Searching…</p>}
                    {!loading && query.length >= 2 && results.length === 0 && <p>No matches found.</p>}
                    {Object.entries(groups).map(([type, items]) => (
                        <div key={type} className="command-group">
                            <small>{type}</small>
                            {items.map((item) => (
                                <button key={type + item.id} onClick={() => { navigate(item.route); onClose(); }}>
                                    <span><strong>{item.title}</strong><small>{item.subtitle}</small></span><ChevronRight />
                                </button>
                            ))}
                        </div>
                    ))}
                </div>
            </section>
        </div>
    );
}
