import { useEffect, useState } from 'react';
import {
    AlertTriangle,
    CheckCircle2,
    LockKeyhole,
    Plus,
    Scale,
    Trash2,
} from 'lucide-react';
import { Card, Field, InlineError, PageState, Pill } from '../components/ui';
import { apiError, money, shortDate, todayIso } from '../lib/format';
import { useAccounts } from '../lib/queries';
import {
    reconciliationMutations,
    useReconciliationMutation,
    useReconciliationSessions,
    useStatementSession,
} from '../lib/reconciliationQueries';
import './ReconciliationPage.css';

const initialForm = () => {
    const today = todayIso();
    return {
        periodStart: today,
        periodEnd: today,
        statementOpeningBalance: '',
        statementClosingBalance: '',
    };
};

export default function ReconciliationPage() {
    const accounts = useAccounts();
    const [accountId, setAccountId] = useState('');
    const [selectedId, setSelectedId] = useState(null);
    const [creating, setCreating] = useState(false);
    const [form, setForm] = useState(initialForm);
    const sessions = useReconciliationSessions(accountId);
    const detail = useStatementSession(selectedId);
    const create = useReconciliationMutation(
        reconciliationMutations.create,
        accountId,
        selectedId
    );

    useEffect(() => {
        if (!accountId && accounts.data?.length) {
            setAccountId(String(accounts.data[0].id));
        }
    }, [accountId, accounts.data]);

    useEffect(() => {
        const items = sessions.data || [];
        if (creating) return;
        if (!items.length) {
            setSelectedId(null);
            return;
        }
        if (!items.some((item) => item.id === selectedId)) {
            const open = items.find((item) => item.status === 'open');
            setSelectedId((open || items[0]).id);
        }
    }, [sessions.data, selectedId, creating]);

    const queryError = accounts.error || sessions.error || detail.error;
    const loading =
        accounts.isLoading || sessions.isLoading || detail.isLoading;

    const createSession = (event) => {
        event.preventDefault();
        create.mutate(
            {
                accountId: Number(accountId),
                ...form,
                statementOpeningBalance: Number(form.statementOpeningBalance),
                statementClosingBalance: Number(form.statementClosingBalance),
            },
            {
                onSuccess: (data) => {
                    setSelectedId(data.session.id);
                    setCreating(false);
                    setForm(initialForm());
                },
            }
        );
    };

    return (
        <div className="page-stack reconciliation-page">
            <div className="page-toolbar">
                <div>
                    <span className="eyebrow">Statement control</span>
                    <h2>Reconcile an account to its bank statement.</h2>
                </div>
                <Field label="Account">
                    <select
                        aria-label="Reconciliation account"
                        value={accountId}
                        onChange={(event) => {
                            setAccountId(event.target.value);
                            setSelectedId(null);
                            setCreating(false);
                        }}
                    >
                        <option value="">Choose an account</option>
                        {(accounts.data || []).map((account) => (
                            <option key={account.id} value={account.id}>
                                {account.name}
                            </option>
                        ))}
                    </select>
                </Field>
            </div>

            <PageState
                loading={loading}
                error={queryError && apiError(queryError)}
                onRetry={() => {
                    accounts.refetch();
                    sessions.refetch();
                    detail.refetch();
                }}
                retrying={
                    accounts.isFetching ||
                    sessions.isFetching ||
                    detail.isFetching
                }
            >
                {!accountId ? (
                    <Card className="reconciliation-empty">
                        <Scale />
                        <h2>Choose an account to begin.</h2>
                        <p className="muted-copy">
                            Reconciliation compares statement activity with the
                            account ledger.
                        </p>
                    </Card>
                ) : (
                    <div className="reconciliation-layout">
                        <aside className="reconciliation-history">
                            <Card>
                                <div className="reconciliation-section-heading">
                                    <div>
                                        <span className="eyebrow">History</span>
                                        <h2>Statement sessions</h2>
                                    </div>
                                    <Scale />
                                </div>
                                <button
                                    className="button reconciliation-new-button"
                                    type="button"
                                    onClick={() => {
                                        setSelectedId(null);
                                        setCreating(true);
                                    }}
                                >
                                    <Plus /> New session
                                </button>
                                <div className="reconciliation-session-list">
                                    {(sessions.data || []).length === 0 ? (
                                        <p className="muted-copy">
                                            No sessions yet.
                                        </p>
                                    ) : (
                                        sessions.data.map((item) => (
                                            <button
                                                type="button"
                                                key={item.id}
                                                className={
                                                    'reconciliation-session-item ' +
                                                    (selectedId === item.id
                                                        ? 'active'
                                                        : '')
                                                }
                                                onClick={() =>
                                                    setSelectedId(item.id)
                                                }
                                            >
                                                <span>
                                                    <strong>
                                                        {shortDate(
                                                            item.periodStart
                                                        )}{' '}
                                                        –{' '}
                                                        {shortDate(
                                                            item.periodEnd
                                                        )}
                                                    </strong>
                                                    <small>
                                                        {money(
                                                            item.statementClosingBalance
                                                        )}{' '}
                                                        closing balance
                                                    </small>
                                                </span>
                                                <Pill
                                                    tone={
                                                        item.status === 'closed'
                                                            ? 'success'
                                                            : 'warning'
                                                    }
                                                >
                                                    {item.status}
                                                </Pill>
                                            </button>
                                        ))
                                    )}
                                </div>
                            </Card>
                        </aside>

                        <main>
                            {!selectedId || !detail.data ? (
                                <NewSessionCard
                                    form={form}
                                    setForm={setForm}
                                    onSubmit={createSession}
                                    pending={create.isPending}
                                    error={create.error}
                                />
                            ) : (
                                <SessionWorkspace
                                    detail={detail.data}
                                    accountId={accountId}
                                />
                            )}
                        </main>
                    </div>
                )}
            </PageState>
        </div>
    );
}

function NewSessionCard({ form, setForm, onSubmit, pending, error }) {
    const change = (name, value) =>
        setForm((current) => ({ ...current, [name]: value }));
    return (
        <Card className="reconciliation-new-session">
            <div className="reconciliation-section-heading">
                <div>
                    <span className="eyebrow">New session</span>
                    <h2>Enter statement balances</h2>
                </div>
                <Scale />
            </div>
            <p className="muted-copy">
                Choose the statement period and enter the opening and closing
                balances exactly as printed.
            </p>
            <form onSubmit={onSubmit} className="reconciliation-form">
                <Field label="Period starts">
                    <input
                        type="date"
                        required
                        value={form.periodStart}
                        onChange={(event) =>
                            change('periodStart', event.target.value)
                        }
                    />
                </Field>
                <Field label="Period ends">
                    <input
                        type="date"
                        required
                        value={form.periodEnd}
                        onChange={(event) =>
                            change('periodEnd', event.target.value)
                        }
                    />
                </Field>
                <Field label="Statement opening balance">
                    <input
                        type="number"
                        inputMode="decimal"
                        step="0.01"
                        required
                        value={form.statementOpeningBalance}
                        onChange={(event) =>
                            change(
                                'statementOpeningBalance',
                                event.target.value
                            )
                        }
                    />
                </Field>
                <Field label="Statement closing balance">
                    <input
                        type="number"
                        inputMode="decimal"
                        step="0.01"
                        required
                        value={form.statementClosingBalance}
                        onChange={(event) =>
                            change(
                                'statementClosingBalance',
                                event.target.value
                            )
                        }
                    />
                </Field>
                <InlineError>{error && apiError(error)}</InlineError>
                <div className="modal-actions">
                    <button className="button" type="submit" disabled={pending}>
                        {pending ? 'Creating session…' : 'Start reconciliation'}
                    </button>
                </div>
            </form>
        </Card>
    );
}

function SessionWorkspace({ detail, accountId }) {
    const { session, transactions } = detail;
    const setCleared = useReconciliationMutation(
        reconciliationMutations.setCleared,
        accountId,
        session.id
    );
    const adjustment = useReconciliationMutation(
        reconciliationMutations.adjustment,
        accountId,
        session.id
    );
    const deleteAdjustment = useReconciliationMutation(
        reconciliationMutations.deleteAdjustment,
        accountId,
        session.id
    );
    const close = useReconciliationMutation(
        reconciliationMutations.close,
        accountId,
        session.id
    );
    const pending =
        setCleared.isPending ||
        adjustment.isPending ||
        deleteAdjustment.isPending ||
        close.isPending;
    const discrepancy = Number(session.closingDiscrepancy);
    const openingDiscrepancy = Number(session.openingDiscrepancy);
    const adjustmentTransaction = transactions.find(
        (item) => item.isReconciliationAdjustment
    );
    const closeReason =
        openingDiscrepancy !== 0
            ? 'Resolve the opening balance discrepancy before closing.'
            : discrepancy !== 0
              ? 'Clear transactions or create an adjustment until the closing discrepancy is zero.'
              : null;

    return (
        <div className="reconciliation-workspace">
            <Card className="reconciliation-summary-card">
                <div className="reconciliation-section-heading">
                    <div>
                        <span className="eyebrow">
                            {session.status === 'closed'
                                ? 'Closed session'
                                : 'Open session'}
                        </span>
                        <h2>
                            {shortDate(session.periodStart)} –{' '}
                            {shortDate(session.periodEnd)}
                        </h2>
                    </div>
                    <Pill
                        tone={
                            session.status === 'closed' ? 'success' : 'warning'
                        }
                    >
                        {session.status === 'closed' ? (
                            <>
                                <LockKeyhole /> Closed
                            </>
                        ) : (
                            'Needs review'
                        )}
                    </Pill>
                </div>
                <div className="reconciliation-metric-grid">
                    <Metric
                        label="Statement opening"
                        value={session.statementOpeningBalance}
                    />
                    <Metric
                        label="Expected opening"
                        value={session.expectedOpeningBalance}
                        tone={openingDiscrepancy === 0 ? 'success' : 'danger'}
                    />
                    <Metric
                        label="Cleared balance"
                        value={session.clearedBalance}
                    />
                    <Metric
                        label="Statement closing"
                        value={session.statementClosingBalance}
                    />
                </div>
                <div className="reconciliation-checks">
                    <CheckRow
                        label="Opening balance check"
                        detail={`Expected ${money(session.expectedOpeningBalance)} · Statement ${money(session.statementOpeningBalance)}`}
                        discrepancy={openingDiscrepancy}
                    />
                    <CheckRow
                        label="Closing balance check"
                        detail={`${session.clearedTransactionCount} of ${session.transactionCount} transactions cleared`}
                        discrepancy={discrepancy}
                    />
                </div>
            </Card>

            <Card className="reconciliation-transactions-card">
                <div className="reconciliation-section-heading">
                    <div>
                        <span className="eyebrow">Statement activity</span>
                        <h2>Clear each transaction</h2>
                    </div>
                    <span className="muted-copy">
                        {transactions.length} transactions
                    </span>
                </div>
                <div className="desktop-table-wrap">
                    <table className="data-table reconciliation-table">
                        <thead>
                            <tr>
                                <th>Cleared</th>
                                <th>Date</th>
                                <th>Description</th>
                                <th>Category</th>
                                <th className="amount-cell">Amount</th>
                            </tr>
                        </thead>
                        <tbody>
                            {transactions.map((item) => (
                                <tr
                                    key={item.id}
                                    className={
                                        item.isReconciliationAdjustment
                                            ? 'adjustment-row'
                                            : ''
                                    }
                                >
                                    <td>
                                        <input
                                            type="checkbox"
                                            aria-label={`Clear ${item.payee || item.memo || 'transaction'}`}
                                            checked={item.isCleared}
                                            disabled={
                                                session.status === 'closed' ||
                                                item.isReconciliationAdjustment ||
                                                pending
                                            }
                                            onChange={(event) =>
                                                setCleared.mutate({
                                                    sessionId: session.id,
                                                    transactionId: item.id,
                                                    cleared:
                                                        event.target.checked,
                                                })
                                            }
                                        />
                                    </td>
                                    <td>{shortDate(item.date)}</td>
                                    <td>
                                        <strong>
                                            {item.payee ||
                                                'Unlabelled transaction'}
                                        </strong>
                                        {item.memo && (
                                            <small>{item.memo}</small>
                                        )}
                                    </td>
                                    <td>
                                        {item.isReconciliationAdjustment
                                            ? 'Reconciliation adjustment'
                                            : item.categoryName}
                                    </td>
                                    <td
                                        className={
                                            'amount-cell ' +
                                            (item.amount < 0
                                                ? 'negative'
                                                : 'positive')
                                        }
                                    >
                                        {money(item.amount)}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
                {transactions.length === 0 && (
                    <p className="muted-copy">
                        No transactions fall within this statement period.
                    </p>
                )}
            </Card>

            <Card className="reconciliation-actions-card">
                <div>
                    <span className="eyebrow">Resolve discrepancy</span>
                    <h2>
                        {discrepancy === 0
                            ? 'Balances match'
                            : `Difference of ${money(discrepancy)}`}
                    </h2>
                    <p className="muted-copy">
                        {discrepancy === 0
                            ? 'The cleared activity agrees with the statement closing balance.'
                            : 'An adjustment changes the account balance by exactly the remaining difference.'}
                    </p>
                </div>
                {session.status === 'open' && (
                    <div className="reconciliation-action-buttons">
                        {adjustmentTransaction ? (
                            <button
                                type="button"
                                className="button secondary"
                                disabled={pending}
                                onClick={() =>
                                    deleteAdjustment.mutate(session.id)
                                }
                            >
                                <Trash2 /> Remove adjustment
                            </button>
                        ) : discrepancy !== 0 ? (
                            <button
                                type="button"
                                className="button secondary"
                                disabled={pending}
                                onClick={() => adjustment.mutate(session.id)}
                            >
                                <Scale /> Create adjustment
                            </button>
                        ) : null}
                        <button
                            type="button"
                            className="button"
                            disabled={!session.canClose || pending}
                            title={
                                closeReason ||
                                'Close and lock this statement session'
                            }
                            onClick={() => close.mutate(session.id)}
                        >
                            {close.isPending ? 'Closing…' : 'Close session'}
                        </button>
                    </div>
                )}
                {session.status === 'closed' && (
                    <div className="reconciliation-closed-note">
                        <LockKeyhole /> This session is closed and read-only.
                    </div>
                )}
                <InlineError>
                    {(setCleared.error ||
                        adjustment.error ||
                        deleteAdjustment.error ||
                        close.error) &&
                        apiError(
                            setCleared.error ||
                                adjustment.error ||
                                deleteAdjustment.error ||
                                close.error
                        )}
                </InlineError>
                {closeReason && session.status === 'open' && (
                    <p className="reconciliation-close-reason">
                        <AlertTriangle /> {closeReason}
                    </p>
                )}
            </Card>
        </div>
    );
}

function Metric({ label, value, tone }) {
    return (
        <div className={'reconciliation-metric ' + (tone || '')}>
            <span>{label}</span>
            <strong>{money(value)}</strong>
        </div>
    );
}

function CheckRow({ label, detail, discrepancy }) {
    const matches = Number(discrepancy) === 0;
    return (
        <div
            className={
                'reconciliation-check-row ' + (matches ? 'matches' : 'mismatch')
            }
        >
            {matches ? <CheckCircle2 /> : <AlertTriangle />}
            <span>
                <strong>{label}</strong>
                <small>{detail}</small>
            </span>
            <b>{matches ? 'Matches' : money(discrepancy) + ' difference'}</b>
        </div>
    );
}
