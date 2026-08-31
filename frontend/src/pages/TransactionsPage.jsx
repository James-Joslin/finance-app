import { useMemo, useState } from 'react';
import {
    CalendarClock,
    Check,
    ChevronDown,
    Download,
    FileUp,
    Filter,
    History,
    RefreshCw,
    RotateCcw,
    Search,
    WalletCards,
} from 'lucide-react';
import RecurringEditor from '../components/RecurringEditor';
import { Card, Field, Modal, PageState, Pill } from '../components/ui';
import { apiError, money, relativeDate, shortDate } from '../lib/format';
import {
    mutations,
    queryKeys,
    useAccounts,
    useCategories,
    useFinovaMutation,
    useImportRows,
    useImports,
    useTransactions,
} from '../lib/queries';

const initialFilters = {
    type: 'all',
    accountId: '',
    categoryId: '',
    startDate: '',
    endDate: '',
};

export default function TransactionsPage() {
    const [filters, setFilters] = useState(initialFilters);
    const [search, setSearch] = useState('');
    const [page, setPage] = useState(1);
    const [filtersOpen, setFiltersOpen] = useState(false);
    const [importOpen, setImportOpen] = useState(false);
    const [recurringTransaction, setRecurringTransaction] = useState(null);
    const accounts = useAccounts();
    const categories = useCategories();

    const params = useMemo(
        () => ({
            page,
            pageSize: 20,
            type: filters.type,
            accountId: filters.accountId || undefined,
            categoryId: filters.categoryId || undefined,
            startDate: filters.startDate || undefined,
            endDate: filters.endDate || undefined,
            search: search || undefined,
        }),
        [page, filters, search]
    );
    const transactions = useTransactions(params);
    const hasFilters =
        search ||
        Object.values(filters).some((value) => value && value !== 'all');

    const change = (name, value) => {
        setFilters((current) => ({ ...current, [name]: value }));
        setPage(1);
    };

    const exportUrl =
        '/api/transactions/export?' +
        new URLSearchParams(
            Object.fromEntries(
                Object.entries(params).filter(
                    ([key, value]) =>
                        value && key !== 'page' && key !== 'pageSize'
                )
            )
        ).toString();

    return (
        <div className="page-stack">
            <div className="page-toolbar">
                <div className="segmented">
                    {['all', 'income', 'spending'].map((type) => (
                        <button
                            key={type}
                            className={filters.type === type ? 'active' : ''}
                            onClick={() => change('type', type)}
                        >
                            {type[0].toUpperCase() + type.slice(1)}
                        </button>
                    ))}
                </div>
                <div className="toolbar-actions">
                    <button
                        className={
                            'button secondary ' + (hasFilters ? 'active' : '')
                        }
                        onClick={() => setFiltersOpen(!filtersOpen)}
                    >
                        <Filter /> Filters
                    </button>
                    <a className="button secondary" href={exportUrl}>
                        <Download /> Export
                    </a>
                    <button
                        className="button"
                        onClick={() => setImportOpen(true)}
                    >
                        <FileUp /> Import
                    </button>
                </div>
            </div>

            <Card className="transactions-card">
                <div className="transaction-search">
                    <Search />
                    <input
                        value={search}
                        onChange={(event) => {
                            setSearch(event.target.value);
                            setPage(1);
                        }}
                        placeholder="Search transactions…"
                    />
                    {transactions.isFetching && <RefreshCw className="spin" />}
                </div>

                {filtersOpen && (
                    <div className="filter-panel">
                        <Field label="Account">
                            <select
                                value={filters.accountId}
                                onChange={(event) =>
                                    change('accountId', event.target.value)
                                }
                            >
                                <option value="">All accounts</option>
                                {accounts.data?.map((account) => (
                                    <option key={account.id} value={account.id}>
                                        {account.name}
                                    </option>
                                ))}
                            </select>
                        </Field>
                        <Field label="Category">
                            <select
                                value={filters.categoryId}
                                onChange={(event) =>
                                    change('categoryId', event.target.value)
                                }
                            >
                                <option value="">All categories</option>
                                {categories.data?.map((category) => (
                                    <option
                                        key={category.id}
                                        value={category.id}
                                    >
                                        {category.name}
                                    </option>
                                ))}
                            </select>
                        </Field>
                        <Field label="From">
                            <input
                                type="date"
                                value={filters.startDate}
                                onChange={(event) =>
                                    change('startDate', event.target.value)
                                }
                            />
                        </Field>
                        <Field label="To">
                            <input
                                type="date"
                                value={filters.endDate}
                                onChange={(event) =>
                                    change('endDate', event.target.value)
                                }
                            />
                        </Field>
                        <button
                            className="button ghost"
                            onClick={() => {
                                setFilters(initialFilters);
                                setSearch('');
                                setPage(1);
                            }}
                        >
                            Clear all
                        </button>
                    </div>
                )}

                <PageState
                    loading={transactions.isLoading}
                    error={transactions.error && apiError(transactions.error)}
                    empty={transactions.data?.items?.length === 0}
                    emptyTitle="No matching transactions"
                    emptyCopy="Try a different filter or import a bank statement."
                >
                    <TransactionTable
                        items={transactions.data?.items || []}
                        categories={categories.data || []}
                        onMarkRecurring={setRecurringTransaction}
                    />
                    <Pagination
                        page={page}
                        totalPages={transactions.data?.totalPages || 1}
                        totalItems={transactions.data?.totalItems || 0}
                        onChange={setPage}
                    />
                </PageState>
            </Card>
            <ImportModal
                open={importOpen}
                onClose={() => setImportOpen(false)}
                accounts={accounts.data || []}
            />
            <RecurringEditor
                open={Boolean(recurringTransaction)}
                transaction={recurringTransaction}
                onClose={() => setRecurringTransaction(null)}
                accounts={accounts.data || []}
                categories={categories.data || []}
            />
        </div>
    );
}

function TransactionTable({ items, categories, onMarkRecurring }) {
    const updateCategory = useFinovaMutation(
        mutations.updateTransactionCategory,
        [
            ['transactions'],
            queryKeys.dashboard,
            ['insights'],
            queryKeys.budgets,
            queryKeys.rules,
        ]
    );
    const changeCategory = (item, categoryId) => {
        const saveRule = window.confirm(
            `Apply this category automatically to future imports matching ${item.payee || item.memo || 'this reference'}?\n\n` +
                'Choose OK to create a rule, or Cancel to change only this transaction.'
        );
        updateCategory.mutate({ id: item.id, categoryId, saveRule });
    };
    return (
        <>
            <div className="desktop-table-wrap">
                <table className="data-table">
                    <thead>
                        <tr>
                            <th>Date</th>
                            <th>Description</th>
                            <th>Category</th>
                            <th>Account</th>
                            <th>Status</th>
                            <th className="align-right">Amount</th>
                            <th>
                                <span className="sr-only">Actions</span>
                            </th>
                        </tr>
                    </thead>
                    <tbody>
                        {items.map((item) => (
                            <tr key={item.id}>
                                <td>{shortDate(item.date)}</td>
                                <td>
                                    <div className="description-cell">
                                        <span
                                            className={
                                                'transaction-mark small ' +
                                                (isIncomeTransaction(item)
                                                    ? 'income'
                                                    : '')
                                            }
                                        >
                                            <WalletCards />
                                        </span>
                                        <span>
                                            <strong>
                                                {item.payee ||
                                                    item.memo ||
                                                    'Transaction'}
                                            </strong>
                                            <small>
                                                {transactionDetail(item)}
                                            </small>
                                        </span>
                                    </div>
                                </td>
                                <td>
                                    <select
                                        className="table-select"
                                        title="Choose whether this applies once or to future matching imports."
                                        aria-label={
                                            'Category for ' +
                                            (item.payee ||
                                                item.memo ||
                                                'transaction')
                                        }
                                        value={item.categoryId || ''}
                                        onChange={(event) =>
                                            changeCategory(
                                                item,
                                                Number(event.target.value)
                                            )
                                        }
                                    >
                                        {categories.map((category) => (
                                            <option
                                                key={category.id}
                                                value={category.id}
                                            >
                                                {category.name}
                                            </option>
                                        ))}
                                    </select>
                                </td>
                                <td>{item.accountName}</td>
                                <td>
                                    <Pill
                                        tone={
                                            item.status === 'completed'
                                                ? 'success'
                                                : 'info'
                                        }
                                    >
                                        {item.status}
                                    </Pill>
                                </td>
                                <td
                                    className={
                                        'align-right amount ' +
                                        (isIncomeTransaction(item)
                                            ? 'positive'
                                            : '')
                                    }
                                >
                                    {isIncomeTransaction(item) ? '+' : ''}
                                    {money(item.amount)}
                                </td>
                                <td className="transaction-action">
                                    {item.recurringItemId ? (
                                        <span
                                            className="recurring-linked"
                                            title="Matched to a recurring plan"
                                        >
                                            <Check />
                                            <span>Planned</span>
                                        </span>
                                    ) : (
                                        <button
                                            className="icon-button"
                                            onClick={() =>
                                                onMarkRecurring(item)
                                            }
                                            aria-label={
                                                'Mark ' +
                                                (item.payee ||
                                                    item.memo ||
                                                    'transaction') +
                                                ' as recurring'
                                            }
                                            title="Mark as recurring"
                                        >
                                            <CalendarClock />
                                        </button>
                                    )}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
            <div className="mobile-transaction-list">
                {items.map((item) => (
                    <article className="mobile-transaction" key={item.id}>
                        <span
                            className={
                                'transaction-mark ' +
                                (isIncomeTransaction(item) ? 'income' : '')
                            }
                        >
                            <WalletCards />
                        </span>
                        <span>
                            <small>{relativeDate(item.date)}</small>
                            <strong>
                                {item.payee || item.memo || 'Transaction'}
                            </strong>
                            <em>
                                {item.categoryName} · {item.accountName}
                                {item.transactionTypeCode
                                    ? ` · ${item.transactionTypeCode}`
                                    : ''}
                            </em>
                        </span>
                        <span className="mobile-transaction-amount">
                            <strong
                                className={
                                    isIncomeTransaction(item) ? 'positive' : ''
                                }
                            >
                                {isIncomeTransaction(item) ? '+' : ''}
                                {money(item.amount)}
                            </strong>
                            {item.recurringItemId ? (
                                <span
                                    className="recurring-linked"
                                    title="Matched to a recurring plan"
                                >
                                    <Check />
                                    <span>Planned</span>
                                </span>
                            ) : (
                                <button
                                    className="icon-button"
                                    onClick={() => onMarkRecurring(item)}
                                    aria-label={
                                        'Mark ' +
                                        (item.payee ||
                                            item.memo ||
                                            'transaction') +
                                        ' as recurring'
                                    }
                                >
                                    <CalendarClock />
                                </button>
                            )}
                        </span>
                    </article>
                ))}
            </div>
        </>
    );
}

function isIncomeTransaction(item) {
    return Number(item.amount) >= 0 && item.accountType !== 'credit';
}

function transactionDetail(item) {
    const type = item.transactionTypeCode
        ? `${item.transactionTypeCode}${item.transactionTypeMeaning ? ` — ${item.transactionTypeMeaning}` : ''}`
        : null;
    return [
        type,
        item.memo && item.payee ? item.memo : null,
        item.sourceFileType || 'Imported',
    ]
        .filter(Boolean)
        .join(' · ');
}

function Pagination({ page, totalPages, totalItems, onChange }) {
    return (
        <div className="pagination">
            <span>
                Showing page {page} of {totalPages} · {totalItems} transactions
            </span>
            <div>
                <button
                    className="icon-button"
                    disabled={page <= 1}
                    onClick={() => onChange(page - 1)}
                >
                    ‹
                </button>
                <strong>{page}</strong>
                <button
                    className="icon-button"
                    disabled={page >= totalPages}
                    onClick={() => onChange(page + 1)}
                >
                    ›
                </button>
            </div>
        </div>
    );
}

const importInvalidations = [
    ['transactions'],
    queryKeys.accounts,
    queryKeys.dashboard,
    ['insights'],
    queryKeys.goals,
    queryKeys.budgets,
    queryKeys.safety,
    queryKeys.occurrences,
    ['transaction-imports'],
    ['transaction-import-rows'],
];

function ImportModal({ open, onClose, accounts }) {
    const [tab, setTab] = useState('new');
    const [accountId, setAccountId] = useState('');
    const [historyAccountId, setHistoryAccountId] = useState('');
    const [file, setFile] = useState(null);
    const [rowOutcome, setRowOutcome] = useState('');
    const [rowPage, setRowPage] = useState(1);
    const [historyPage, setHistoryPage] = useState(1);
    const [expandedId, setExpandedId] = useState(null);
    const [historyRowPage, setHistoryRowPage] = useState(1);

    const preview = useFinovaMutation(mutations.previewImport);
    const commit = useFinovaMutation(
        mutations.commitImport,
        importInvalidations
    );
    const undo = useFinovaMutation(mutations.undoImport, importInvalidations);
    const batch = commit.data || preview.data;
    const rows = useImportRows(
        batch?.id,
        { outcome: rowOutcome || undefined, page: rowPage, pageSize: 50 },
        open && tab === 'new' && Boolean(batch)
    );
    const history = useImports(
        {
            accountId: historyAccountId || undefined,
            page: historyPage,
            pageSize: 10,
        },
        open && tab === 'history' && Boolean(historyAccountId)
    );
    const historyRows = useImportRows(
        expandedId,
        { page: historyRowPage, pageSize: 50 },
        open && tab === 'history' && Boolean(expandedId)
    );

    const close = () => {
        preview.reset();
        commit.reset();
        undo.reset();
        setFile(null);
        setRowOutcome('');
        setRowPage(1);
        setExpandedId(null);
        onClose();
    };
    const startAgain = () => {
        preview.reset();
        commit.reset();
        setFile(null);
        setRowOutcome('');
        setRowPage(1);
    };
    const submitPreview = async (event) => {
        event.preventDefault();
        const form = new FormData();
        form.append('AccountId', accountId);
        form.append('OfxContent', file);
        await preview.mutateAsync(form);
    };
    const showHistory = () => {
        setTab('history');
        setHistoryAccountId((current) => current || accountId);
        setExpandedId(null);
    };
    const undoBatch = async (item) => {
        if (
            !window.confirm(
                `Undo the import of ${item.imported} transactions from ${item.fileName} in ${item.accountName}?\n\n` +
                    'Any edits made directly to those transactions will be lost. Recurring plans and category rules will be kept.'
            )
        )
            return;
        await undo.mutateAsync(item.id);
        setExpandedId(null);
    };

    return (
        <Modal
            open={open}
            onClose={close}
            wide
            title="Import transactions"
            copy="Preview every row before importing, review previous batches, or undo the latest import for an account."
        >
            <div className="import-tabs" role="tablist">
                <button
                    type="button"
                    className={tab === 'new' ? 'active' : ''}
                    onClick={() => setTab('new')}
                >
                    <FileUp /> New import
                </button>
                <button
                    type="button"
                    className={tab === 'history' ? 'active' : ''}
                    onClick={showHistory}
                    disabled={preview.isPending || batch?.status === 'preview'}
                    title={
                        preview.isPending || batch?.status === 'preview'
                            ? 'Import or cancel the current preview before viewing history.'
                            : undefined
                    }
                >
                    <History /> History
                </button>
            </div>

            {tab === 'new' ? (
                <div className="form-stack">
                    {!batch ? (
                        <form className="form-stack" onSubmit={submitPreview}>
                            <Field label="Account">
                                <select
                                    required
                                    value={accountId}
                                    onChange={(event) =>
                                        setAccountId(event.target.value)
                                    }
                                >
                                    <option value="">Choose an account</option>
                                    {accounts.map((account) => (
                                        <option
                                            key={account.id}
                                            value={account.id}
                                        >
                                            {account.name}
                                        </option>
                                    ))}
                                </select>
                            </Field>
                            <label className="file-drop">
                                <FileUp />
                                <strong>
                                    {file?.name ||
                                        'Choose an OFX, QIF, or Halifax PDF'}
                                </strong>
                                <small>
                                    Maximum 10 MB · the original file is not
                                    retained
                                </small>
                                <input
                                    type="file"
                                    accept=".ofx,.qif,.pdf,application/pdf"
                                    onChange={(event) =>
                                        setFile(event.target.files[0] || null)
                                    }
                                />
                            </label>
                            {preview.error && (
                                <p className="form-error">
                                    {apiError(preview.error)}
                                </p>
                            )}
                            <div className="modal-actions">
                                <button
                                    type="button"
                                    className="button secondary"
                                    onClick={close}
                                >
                                    Close
                                </button>
                                <button
                                    className="button"
                                    disabled={
                                        !accountId || !file || preview.isPending
                                    }
                                >
                                    {preview.isPending
                                        ? 'Preparing preview…'
                                        : 'Preview transactions'}
                                </button>
                            </div>
                        </form>
                    ) : (
                        <>
                            <ImportSummary batch={batch} />
                            <div className="import-row-toolbar">
                                <strong>
                                    {batch.status === 'preview'
                                        ? 'Preview rows'
                                        : 'Final results'}
                                </strong>
                                <select
                                    aria-label="Filter import rows"
                                    value={rowOutcome}
                                    onChange={(event) => {
                                        setRowOutcome(event.target.value);
                                        setRowPage(1);
                                    }}
                                >
                                    <option value="">All outcomes</option>
                                    {batch.status === 'preview' && (
                                        <option value="ready">Ready</option>
                                    )}
                                    {batch.status !== 'preview' && (
                                        <option value="imported">
                                            Imported
                                        </option>
                                    )}
                                    <option value="skipped">Skipped</option>
                                    <option value="rejected">Rejected</option>
                                </select>
                            </div>
                            <ImportRows
                                query={rows}
                                page={rowPage}
                                onPage={setRowPage}
                            />
                            {(commit.error || rows.error) && (
                                <p className="form-error">
                                    {apiError(commit.error || rows.error)}
                                </p>
                            )}
                            <div className="modal-actions">
                                {batch.status === 'completed' ? (
                                    <>
                                        <button
                                            type="button"
                                            className="button secondary"
                                            onClick={close}
                                        >
                                            Close
                                        </button>
                                        <button
                                            type="button"
                                            className="button"
                                            onClick={startAgain}
                                        >
                                            Import another
                                        </button>
                                    </>
                                ) : (
                                    <>
                                        <button
                                            type="button"
                                            className="button secondary"
                                            onClick={startAgain}
                                        >
                                            Cancel preview
                                        </button>
                                        <button
                                            type="button"
                                            className="button"
                                            disabled={
                                                batch.importable === 0 ||
                                                commit.isPending
                                            }
                                            onClick={() =>
                                                commit.mutate(batch.id)
                                            }
                                        >
                                            {commit.isPending
                                                ? 'Importing…'
                                                : `Import ${batch.importable} transactions`}
                                        </button>
                                    </>
                                )}
                            </div>
                        </>
                    )}
                </div>
            ) : (
                <ImportHistory
                    accounts={accounts}
                    accountId={historyAccountId}
                    onAccount={(value) => {
                        setHistoryAccountId(value);
                        setHistoryPage(1);
                        setExpandedId(null);
                    }}
                    query={history}
                    page={historyPage}
                    onPage={setHistoryPage}
                    expandedId={expandedId}
                    onExpand={(id) => {
                        setExpandedId(expandedId === id ? null : id);
                        setHistoryRowPage(1);
                    }}
                    rows={historyRows}
                    rowPage={historyRowPage}
                    onRowPage={setHistoryRowPage}
                    onUndo={undoBatch}
                    undo={undo}
                    onClose={close}
                />
            )}
        </Modal>
    );
}

export function ImportSummary({ batch }) {
    return (
        <section className="import-summary">
            <div>
                <span>
                    <small>{batch.accountName}</small>
                    <strong>{batch.fileName}</strong>
                </span>
                <Pill
                    tone={
                        batch.status === 'completed'
                            ? 'success'
                            : batch.status === 'undone'
                              ? 'neutral'
                              : 'info'
                    }
                >
                    {batch.status}
                </Pill>
            </div>
            <div className="import-totals">
                <span>
                    <strong>
                        {batch.status === 'preview'
                            ? batch.importable
                            : batch.imported}
                    </strong>
                    <small>
                        {batch.status === 'preview' ? 'Ready' : 'Imported'}
                    </small>
                </span>
                <span>
                    <strong>{batch.skipped}</strong>
                    <small>Duplicates</small>
                </span>
                <span>
                    <strong>{batch.rejected}</strong>
                    <small>Rejected</small>
                </span>
                <span>
                    <strong>{batch.total}</strong>
                    <small>Total rows</small>
                </span>
            </div>
        </section>
    );
}

export function ImportRows({ query, page, onPage }) {
    if (query.isLoading)
        return (
            <div className="import-rows-state">
                <RefreshCw className="spin" /> Loading row results…
            </div>
        );
    if (query.error) return null;
    if (!query.data?.items?.length)
        return (
            <div className="import-rows-state">No rows match this outcome.</div>
        );
    return (
        <div className="import-rows-wrap">
            <div className="import-row-list">
                {query.data.items.map((row) => (
                    <article className="import-row" key={row.id}>
                        <span className="import-row-position">
                            <small>Row</small>
                            <strong>{row.ordinal}</strong>
                        </span>
                        <span className="import-row-description">
                            <small>
                                {row.sourceLabel}
                                {row.date
                                    ? ` · ${shortDate(row.date)}`
                                    : row.displayDate
                                      ? ` · ${row.displayDate}`
                                      : ''}
                            </small>
                            <strong>
                                {row.payee ||
                                    row.memo ||
                                    'Unreadable transaction'}
                            </strong>
                            {row.reasonMessage && <em>{row.reasonMessage}</em>}
                        </span>
                        <Pill
                            tone={
                                row.outcome === 'imported' ||
                                row.outcome === 'ready'
                                    ? 'success'
                                    : row.outcome === 'rejected'
                                      ? 'warning'
                                      : 'neutral'
                            }
                        >
                            {row.outcome}
                        </Pill>
                        <span className="import-row-values">
                            <span>
                                <small>Amount</small>
                                <strong
                                    className={
                                        Number(row.amount) >= 0
                                            ? 'positive'
                                            : ''
                                    }
                                >
                                    {row.amount != null
                                        ? money(row.amount)
                                        : row.displayAmount || '—'}
                                </strong>
                            </span>
                            <span>
                                <small>Balance after</small>
                                <strong>
                                    {row.balanceAfter != null
                                        ? money(row.balanceAfter)
                                        : '—'}
                                </strong>
                            </span>
                        </span>
                    </article>
                ))}
            </div>
            <ImportPagination
                page={page}
                totalPages={query.data.totalPages}
                totalItems={query.data.totalItems}
                onChange={onPage}
                label="rows"
            />
        </div>
    );
}

function ImportHistory({
    accounts,
    accountId,
    onAccount,
    query,
    page,
    onPage,
    expandedId,
    onExpand,
    rows,
    rowPage,
    onRowPage,
    onUndo,
    undo,
    onClose,
}) {
    return (
        <div className="form-stack">
            <Field
                label="Account"
                hint="Undo is available only on the latest active import for this account."
            >
                <select
                    value={accountId}
                    onChange={(event) => onAccount(event.target.value)}
                >
                    <option value="">Choose an account</option>
                    {accounts.map((account) => (
                        <option key={account.id} value={account.id}>
                            {account.name}
                        </option>
                    ))}
                </select>
            </Field>
            {!accountId ? (
                <div className="import-rows-state">
                    Choose an account to view its import history.
                </div>
            ) : query.isLoading ? (
                <div className="import-rows-state">
                    <RefreshCw className="spin" /> Loading import history…
                </div>
            ) : query.error ? (
                <p className="form-error">{apiError(query.error)}</p>
            ) : query.data?.items?.length === 0 ? (
                <div className="import-rows-state">
                    No completed imports for this account.
                </div>
            ) : (
                <div className="import-history-list">
                    {query.data?.items.map((item) => (
                        <article className="import-history-item" key={item.id}>
                            <button
                                type="button"
                                className="import-history-trigger"
                                onClick={() => onExpand(item.id)}
                                aria-expanded={expandedId === item.id}
                            >
                                <span>
                                    <History />
                                    <span>
                                        <strong>{item.fileName}</strong>
                                        <small>
                                            {relativeDate(
                                                item.completedAt ||
                                                    item.createdAt
                                            )}{' '}
                                            · {item.imported} imported ·{' '}
                                            {item.skipped} skipped ·{' '}
                                            {item.rejected} rejected
                                        </small>
                                    </span>
                                </span>
                                <span>
                                    <Pill
                                        tone={
                                            item.status === 'completed'
                                                ? 'success'
                                                : 'neutral'
                                        }
                                    >
                                        {item.status}
                                    </Pill>
                                    <ChevronDown />
                                </span>
                            </button>
                            {expandedId === item.id && (
                                <div className="import-history-detail">
                                    <ImportRows
                                        query={rows}
                                        page={rowPage}
                                        onPage={onRowPage}
                                    />
                                    {item.canUndo && (
                                        <button
                                            type="button"
                                            className="button danger import-undo"
                                            disabled={undo.isPending}
                                            onClick={() => onUndo(item)}
                                        >
                                            <RotateCcw />
                                            {undo.isPending
                                                ? 'Undoing…'
                                                : 'Undo this import'}
                                        </button>
                                    )}
                                </div>
                            )}
                        </article>
                    ))}
                    <ImportPagination
                        page={page}
                        totalPages={query.data?.totalPages || 1}
                        totalItems={query.data?.totalItems || 0}
                        onChange={onPage}
                        label="imports"
                    />
                </div>
            )}
            {undo.error && <p className="form-error">{apiError(undo.error)}</p>}
            <div className="modal-actions">
                <button
                    type="button"
                    className="button secondary"
                    onClick={onClose}
                >
                    Close
                </button>
            </div>
        </div>
    );
}

function ImportPagination({ page, totalPages, totalItems, onChange, label }) {
    if (totalPages <= 1) return null;
    return (
        <div className="import-pagination">
            <span>
                {totalItems} {label}
            </span>
            <div>
                <button
                    type="button"
                    className="icon-button"
                    disabled={page <= 1}
                    onClick={() => onChange(page - 1)}
                >
                    ‹
                </button>
                <strong>
                    {page} / {totalPages}
                </strong>
                <button
                    type="button"
                    className="icon-button"
                    disabled={page >= totalPages}
                    onClick={() => onChange(page + 1)}
                >
                    ›
                </button>
            </div>
        </div>
    );
}
