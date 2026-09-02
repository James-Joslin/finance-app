import { useMemo, useState } from 'react';
import {
    CalendarClock,
    Check,
    ChevronDown,
    Download,
    FileUp,
    Filter,
    Pencil,
    History,
    Link2,
    Unlink,
    RefreshCw,
    RotateCcw,
    Search,
    WalletCards,
} from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import RecurringEditor from '../components/RecurringEditor';
import TransactionEditor from '../components/TransactionEditor';
import {
    Card,
    Field,
    InlineError,
    Modal,
    PageState,
    Pill,
} from '../components/ui';
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
    useTransferCandidates,
} from '../lib/queries';

import { parseRecordId, useDeepLinkTarget } from '../utils/deepLink';
const initialFilters = {
    type: 'all',
    accountId: '',
    categoryId: '',
    startDate: '',
    endDate: '',
};

export default function TransactionsPage() {
    const [searchParams, setSearchParams] = useSearchParams();
    const transactionId = parseRecordId(searchParams.get('transactionId'));
    const [filters, setFilters] = useState(initialFilters);
    const [search, setSearch] = useState('');
    const [page, setPage] = useState(1);
    const [filtersOpen, setFiltersOpen] = useState(false);
    const [importOpen, setImportOpen] = useState(false);
    const [recurringTransaction, setRecurringTransaction] = useState(null);
    const [pairTransaction, setPairTransaction] = useState(null);
    const [transactionEditorOpen, setTransactionEditorOpen] = useState(false);
    const [editingTransaction, setEditingTransaction] = useState(null);
    const accounts = useAccounts();
    const categories = useCategories();

    const params = useMemo(
        () => ({
            page,
            pageSize: 20,
            transactionId: transactionId || undefined,
            type: filters.type,
            accountId: filters.accountId || undefined,
            categoryId: filters.categoryId || undefined,
            startDate: filters.startDate || undefined,
            endDate: filters.endDate || undefined,
            search: search || undefined,
        }),
        [page, filters, search, transactionId]
    );
    const transactions = useTransactions(params);
    useDeepLinkTarget(
        transactionId,
        transactions.data,
        '[data-deep-link-type="transaction"]'
    );
    const pageQueries = [transactions, accounts, categories];
    const hasFilters =
        search ||
        Object.values(filters).some((value) => value && value !== 'all');

    const clearTransactionTarget = () => {
        if (!searchParams.has('transactionId')) return;
        const next = new URLSearchParams(searchParams);
        next.delete('transactionId');
        setSearchParams(next, { replace: true });
    };
    const change = (name, value) => {
        clearTransactionTarget();
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
                        className="button"
                        onClick={() => {
                            setEditingTransaction(null);
                            setTransactionEditorOpen(true);
                        }}
                    >
                        <Pencil /> Add transaction
                    </button>
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
                            clearTransactionTarget();
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
                                clearTransactionTarget();
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
                    loading={pageQueries.some((query) => query.isLoading)}
                    error={
                        pageQueries.find((query) => query.error)?.error &&
                        apiError(pageQueries.find((query) => query.error).error)
                    }
                    onRetry={() =>
                        Promise.all(
                            pageQueries
                                .filter((query) => query.error)
                                .map((query) => query.refetch())
                        )
                    }
                    retrying={pageQueries.some(
                        (query) => query.error && query.isFetching
                    )}
                    empty={transactions.data?.items?.length === 0}
                    emptyTitle="No matching transactions"
                    emptyCopy="Try a different filter or import a bank statement."
                >
                    <TransactionTable
                        items={transactions.data?.items || []}
                        categories={categories.data || []}
                        onMarkRecurring={setRecurringTransaction}
                        onPair={setPairTransaction}
                        focusedId={transactionId}
                        onEdit={(item) => {
                            setEditingTransaction(item);
                            setTransactionEditorOpen(true);
                        }}
                    />
                    <Pagination
                        page={page}
                        totalPages={transactions.data?.totalPages || 1}
                        totalItems={transactions.data?.totalItems || 0}
                        onChange={(nextPage) => {
                            clearTransactionTarget();
                            setPage(nextPage);
                        }}
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
            <TransferPairModal
                open={Boolean(pairTransaction)}
                transaction={pairTransaction}
                onClose={() => setPairTransaction(null)}
            />
            <TransactionEditor
                open={transactionEditorOpen}
                transaction={editingTransaction}
                onClose={() => {
                    setTransactionEditorOpen(false);
                    setEditingTransaction(null);
                }}
                accounts={accounts.data || []}
                categories={categories.data || []}
            />
        </div>
    );
}

function TransactionTable({
    items,
    categories,
    onMarkRecurring,
    onPair,
    focusedId,
    onEdit,
}) {
    const updateCategory = useFinovaMutation(
        mutations.updateTransactionCategory,
        [
            ['transactions'],
            queryKeys.dashboard,
            ['insights'],
            queryKeys.budgets,
            queryKeys.rules,
        ],
        { successMessage: 'Transaction category updated.' }
    );
    const unpair = useFinovaMutation(
        mutations.unpairTransfer,
        [
            ['transactions'],
            queryKeys.dashboard,
            queryKeys.insights,
            queryKeys.budgets,
            queryKeys.safety,
            ['transfer-candidates'],
        ],
        { successMessage: 'Transfer unpaired.' }
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
                            <tr
                                key={item.id}
                                className={
                                    focusedId === item.id
                                        ? 'deep-link-target'
                                        : ''
                                }
                                data-deep-link-type="transaction"
                                data-deep-link-id={item.id}
                            >
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
                                    {item.isSplit ? (
                                        <span className="split-label">
                                            Split · {item.splitCount} categories
                                        </span>
                                    ) : (
                                        <select
                                            className="table-select"
                                            disabled={updateCategory.isPending}
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
                                    )}
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
                                    {item.pairedTransactionId ? (
                                        <>
                                            <span
                                                className="recurring-linked"
                                                title={
                                                    'Paired with ' +
                                                    item.pairedAccountName
                                                }
                                            >
                                                <Link2 />
                                                <span>
                                                    {item.pairedAccountName}
                                                </span>
                                            </span>
                                            <button
                                                className="icon-button"
                                                disabled={unpair.isPending}
                                                onClick={() =>
                                                    unpair.mutate(item.id)
                                                }
                                                aria-label="Unpair transfer"
                                                title="Unpair transfer"
                                            >
                                                <Unlink />
                                            </button>
                                        </>
                                    ) : (
                                        <button
                                            className="icon-button"
                                            onClick={() => onPair(item)}
                                            aria-label="Pair transfer"
                                            title="Pair with another account transaction"
                                        >
                                            <Link2 />
                                        </button>
                                    )}
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
                                    {item.isEditable && (
                                        <button
                                            className="icon-button"
                                            onClick={() => onEdit(item)}
                                            aria-label="Edit manual transaction"
                                            title="Edit manual transaction"
                                        >
                                            <Pencil />
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
                    <article
                        className={
                            focusedId === item.id
                                ? 'mobile-transaction deep-link-target'
                                : 'mobile-transaction'
                        }
                        data-deep-link-type="transaction"
                        data-deep-link-id={item.id}
                        key={item.id}
                    >
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
                            {item.isEditable && (
                                <button
                                    className="icon-button"
                                    onClick={() => onEdit(item)}
                                    aria-label="Edit manual transaction"
                                    title="Edit manual transaction"
                                >
                                    <Pencil />
                                </button>
                            )}
                            {item.pairedTransactionId ? (
                                <button
                                    className="icon-button"
                                    onClick={() => unpair.mutate(item.id)}
                                    disabled={unpair.isPending}
                                    aria-label="Unpair transfer"
                                >
                                    <Unlink />
                                </button>
                            ) : (
                                <button
                                    className="icon-button"
                                    onClick={() => onPair(item)}
                                    aria-label="Pair transfer"
                                >
                                    <Link2 />
                                </button>
                            )}
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
            <InlineError>
                {(updateCategory.error || unpair.error) &&
                    apiError(updateCategory.error || unpair.error)}
            </InlineError>
        </>
    );
}

function TransferPairModal({ open, transaction, onClose }) {
    const candidates = useTransferCandidates(transaction?.id);
    const pair = useFinovaMutation(
        mutations.pairTransfer,
        [
            ['transactions'],
            queryKeys.dashboard,
            queryKeys.insights,
            queryKeys.budgets,
            queryKeys.safety,
            ['transfer-candidates'],
        ],
        { successMessage: 'Transfer paired.' }
    );
    const choose = async (candidate) => {
        try {
            await pair.mutateAsync({
                id: transaction.id,
                pairedTransactionId: candidate.id,
            });
            onClose();
        } catch {
            // Keep the modal open so the error remains visible.
        }
    };
    return (
        <Modal
            open={open}
            onClose={onClose}
            title="Pair transfer"
            copy="Choose the matching transaction in another account. Amounts must be equal and opposite."
            wide
        >
            <div className="pair-candidate-list">
                {candidates.isLoading ? (
                    <p className="muted-copy">Finding matching transactions…</p>
                ) : null}
                {!candidates.isLoading &&
                (candidates.data || []).length === 0 ? (
                    <p className="muted-copy">
                        No unpaired transaction with the opposite amount was
                        found.
                    </p>
                ) : null}
                {(candidates.data || []).map((candidate) => (
                    <button
                        className="pair-candidate"
                        key={candidate.id}
                        onClick={() => choose(candidate)}
                        disabled={pair.isPending}
                    >
                        <span>
                            <strong>
                                {candidate.payee ||
                                    candidate.memo ||
                                    'Transaction'}
                            </strong>
                            <small>
                                {candidate.accountName} ·{' '}
                                {shortDate(candidate.date)}
                            </small>
                        </span>
                        <strong
                            className={candidate.amount >= 0 ? 'positive' : ''}
                        >
                            {candidate.amount >= 0 ? '+' : ''}
                            {money(candidate.amount)}
                        </strong>
                    </button>
                ))}
                <InlineError>
                    {(candidates.error || pair.error) &&
                        apiError(candidates.error || pair.error)}
                </InlineError>
                <div className="modal-actions">
                    <button
                        type="button"
                        className="button secondary"
                        onClick={onClose}
                    >
                        Cancel
                    </button>
                </div>
            </div>
        </Modal>
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

    const preview = useFinovaMutation(mutations.previewImport, [], {
        successMessage: 'Import preview is ready.',
    });
    const commit = useFinovaMutation(
        mutations.commitImport,
        importInvalidations,
        { successMessage: 'Transactions imported.' }
    );
    const undo = useFinovaMutation(mutations.undoImport, importInvalidations, {
        successMessage: 'Import undone.',
    });
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
        try {
            await preview.mutateAsync(form);
        } catch {
            // The preview error remains visible in the form.
        }
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
        try {
            await undo.mutateAsync(item.id);
            setExpandedId(null);
        } catch {
            // The undo error remains visible in the history panel.
        }
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
                            <InlineError>
                                {preview.error && apiError(preview.error)}
                            </InlineError>
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
                            <InlineError>
                                {commit.error && apiError(commit.error)}
                            </InlineError>
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
    if (query.error)
        return (
            <InlineError
                onRetry={() => query.refetch()}
                retrying={query.isFetching}
            >
                {apiError(query.error)}
            </InlineError>
        );
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
                <InlineError
                    onRetry={() => query.refetch()}
                    retrying={query.isFetching}
                >
                    {apiError(query.error)}
                </InlineError>
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
            <InlineError>{undo.error && apiError(undo.error)}</InlineError>
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
