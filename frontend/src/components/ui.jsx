import {
    createContext,
    useCallback,
    useContext,
    useEffect,
    useId,
    useMemo,
    useRef,
    useState,
} from 'react';
import {
    AlertCircle,
    CheckCircle2,
    Inbox,
    LoaderCircle,
    X,
} from 'lucide-react';

const FeedbackContext = createContext(null);
const FOCUSABLE = [
    'a[href]',
    'button:not([disabled])',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[contenteditable="true"]',
    '[tabindex]:not([tabindex="-1"])',
].join(',');

export function FeedbackProvider({ children }) {
    const [messages, setMessages] = useState([]);

    const dismiss = useCallback((id) => {
        setMessages((current) => current.filter((item) => item.id !== id));
    }, []);
    const notifySuccess = useCallback((message) => {
        const id =
            globalThis.crypto?.randomUUID?.() ||
            Date.now() + '-' + Math.random();
        setMessages((current) => [...current, { id, message }]);
        return id;
    }, []);
    const value = useMemo(
        () => ({ notifySuccess, dismiss }),
        [dismiss, notifySuccess]
    );

    return (
        <FeedbackContext.Provider value={value}>
            {children}
            <div
                className="toast-region"
                role="region"
                aria-label="Notifications"
                aria-live="polite"
                aria-atomic="false"
            >
                {messages.map((item) => (
                    <SuccessToast
                        key={item.id}
                        item={item}
                        onDismiss={dismiss}
                    />
                ))}
            </div>
        </FeedbackContext.Provider>
    );
}

function SuccessToast({ item, onDismiss }) {
    useEffect(() => {
        const timeout = window.setTimeout(() => onDismiss(item.id), 6000);
        return () => window.clearTimeout(timeout);
    }, [item.id, onDismiss]);

    return (
        <div className="toast toast-success">
            <CheckCircle2 aria-hidden="true" />
            <span>{item.message}</span>
            <button
                className="icon-button"
                type="button"
                onClick={() => onDismiss(item.id)}
                aria-label="Dismiss notification"
            >
                <X />
            </button>
        </div>
    );
}

export function useFeedback() {
    const context = useContext(FeedbackContext);
    if (!context) {
        throw new Error('useFeedback must be used inside FeedbackProvider');
    }
    return context;
}

export function Card({ className = '', children, ...props }) {
    return (
        <section className={'card ' + className} {...props}>
            {children}
        </section>
    );
}

export function PageState({
    loading,
    error,
    onRetry,
    retrying = false,
    empty,
    emptyTitle = 'Nothing here yet',
    emptyCopy,
    children,
}) {
    if (loading && !error) {
        return (
            <div className="page-state">
                <LoaderCircle className="spin" />
                <p>Loading your household data…</p>
            </div>
        );
    }
    if (error) {
        return (
            <div className="page-state error-state" role="alert">
                <AlertCircle />
                <h2>We could not load this page</h2>
                <p>{error}</p>
                {onRetry && (
                    <button
                        className="button"
                        type="button"
                        onClick={onRetry}
                        disabled={retrying}
                    >
                        {retrying ? 'Trying again…' : 'Try again'}
                    </button>
                )}
            </div>
        );
    }
    if (empty) {
        return (
            <div className="page-state">
                <Inbox />
                <h2>{emptyTitle}</h2>
                <p>{emptyCopy}</p>
            </div>
        );
    }
    return children;
}

export function Progress({ value, tone = 'brand', label }) {
    const safeValue = Math.max(0, Math.min(100, Number(value || 0)));
    return (
        <div className="progress-wrap">
            {label && (
                <span className="sr-only">
                    {label}: {safeValue}%
                </span>
            )}
            <div className="progress-track" aria-hidden="true">
                <span
                    className={'progress-fill tone-' + tone}
                    style={{ width: safeValue + '%' }}
                />
            </div>
        </div>
    );
}

export function Modal({
    open,
    title,
    copy,
    onClose,
    children,
    wide = false,
    initialFocusRef,
}) {
    const dialogRef = useRef(null);
    const closeRef = useRef(onClose);
    const titleId = useId();
    const descriptionId = useId();

    useEffect(() => {
        closeRef.current = onClose;
    }, [onClose]);

    useEffect(() => {
        if (!open) return undefined;
        const previouslyFocused = document.activeElement;
        const dialog = dialogRef.current;
        const focusable = () =>
            Array.from(dialog?.querySelectorAll(FOCUSABLE) || []);
        const initialTarget =
            initialFocusRef?.current || focusable()[0] || dialog;
        initialTarget?.focus();

        const handleKeyDown = (event) => {
            if (event.key === 'Escape') {
                event.preventDefault();
                closeRef.current?.();
                return;
            }
            if (event.key !== 'Tab') return;
            const elements = focusable();
            if (elements.length === 0) {
                event.preventDefault();
                dialog?.focus();
                return;
            }
            const first = elements[0];
            const last = elements[elements.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (
                !event.shiftKey &&
                (document.activeElement === last ||
                    !dialog?.contains(document.activeElement))
            ) {
                event.preventDefault();
                first.focus();
            }
        };

        document.addEventListener('keydown', handleKeyDown);
        return () => {
            document.removeEventListener('keydown', handleKeyDown);
            if (previouslyFocused?.isConnected) previouslyFocused.focus();
        };
    }, [initialFocusRef, open]);

    if (!open) return null;
    return (
        <div
            className="modal-backdrop"
            role="presentation"
            onMouseDown={onClose}
        >
            <section
                ref={dialogRef}
                className={'modal ' + (wide ? 'modal-wide' : '')}
                role="dialog"
                aria-modal="true"
                aria-labelledby={titleId}
                aria-describedby={copy ? descriptionId : undefined}
                tabIndex="-1"
                onMouseDown={(event) => event.stopPropagation()}
            >
                <div className="modal-header">
                    <div>
                        <h2 id={titleId}>{title}</h2>
                        {copy && <p id={descriptionId}>{copy}</p>}
                    </div>
                    <button
                        className="icon-button"
                        type="button"
                        onClick={onClose}
                        aria-label="Close dialog"
                    >
                        <X />
                    </button>
                </div>
                {children}
            </section>
        </div>
    );
}

export function InlineError({
    children,
    className = '',
    onRetry,
    retrying = false,
}) {
    if (!children) return null;
    return (
        <p className={'form-error ' + className} role="alert">
            <span>{children}</span>
            {onRetry && (
                <button
                    className="button small secondary"
                    type="button"
                    onClick={onRetry}
                    disabled={retrying}
                >
                    {retrying ? 'Trying again…' : 'Try again'}
                </button>
            )}
        </p>
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
