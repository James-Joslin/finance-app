import { AlertCircle, Inbox, LoaderCircle, X } from 'lucide-react';

export function Card({ className = '', children, ...props }) {
    return <section className={'card ' + className} {...props}>{children}</section>;
}

export function PageState({ loading, error, empty, emptyTitle = 'Nothing here yet', emptyCopy, children }) {
    if (loading) {
        return <div className="page-state"><LoaderCircle className="spin" /><p>Loading your household data…</p></div>;
    }
    if (error) {
        return <div className="page-state error-state"><AlertCircle /><h2>We could not load this page</h2><p>{error}</p></div>;
    }
    if (empty) {
        return <div className="page-state"><Inbox /><h2>{emptyTitle}</h2><p>{emptyCopy}</p></div>;
    }
    return children;
}

export function Progress({ value, tone = 'brand', label }) {
    const safeValue = Math.max(0, Math.min(100, Number(value || 0)));
    return (
        <div className="progress-wrap">
            {label && <span className="sr-only">{label}: {safeValue}%</span>}
            <div className="progress-track" aria-hidden="true">
                <span className={'progress-fill tone-' + tone} style={{ width: safeValue + '%' }} />
            </div>
        </div>
    );
}

export function Modal({ open, title, copy, onClose, children, wide = false }) {
    if (!open) return null;
    return (
        <div className="modal-backdrop" role="presentation" onMouseDown={onClose}>
            <section
                className={'modal ' + (wide ? 'modal-wide' : '')}
                role="dialog"
                aria-modal="true"
                aria-labelledby="modal-title"
                onMouseDown={(event) => event.stopPropagation()}
            >
                <div className="modal-header">
                    <div><h2 id="modal-title">{title}</h2>{copy && <p>{copy}</p>}</div>
                    <button className="icon-button" type="button" onClick={onClose} aria-label="Close dialog"><X /></button>
                </div>
                {children}
            </section>
        </div>
    );
}

export function Field({ label, hint, children, className = '' }) {
    return (
        <label className={'field ' + className}>
            <span>{label}</span>
            {children}
            {hint && <small>{hint}</small>}
        </label>
    );
}

export function Pill({ children, tone = 'neutral' }) {
    return <span className={'pill pill-' + tone}>{children}</span>;
}
