import { useMemo, useState } from 'react';
import { Download, FileUp, Filter, RefreshCw, Search, SlidersHorizontal, WalletCards } from 'lucide-react';
import { Card, Field, Modal, PageState, Pill } from '../components/ui';
import { apiError, money, relativeDate, shortDate } from '../lib/format';
import { mutations, queryKeys, useAccounts, useCategories, useFinovaMutation, useTransactions } from '../lib/queries';

const initialFilters = { type: 'all', accountId: '', categoryId: '', startDate: '', endDate: '' };

export default function TransactionsPage() {
    const [filters, setFilters] = useState(initialFilters);
    const [search, setSearch] = useState('');
    const [page, setPage] = useState(1);
    const [filtersOpen, setFiltersOpen] = useState(false);
    const [importOpen, setImportOpen] = useState(false);
    const accounts = useAccounts();
    const categories = useCategories();

    const params = useMemo(() => ({
        page,
        pageSize: 20,
        type: filters.type,
        accountId: filters.accountId || undefined,
        categoryId: filters.categoryId || undefined,
        startDate: filters.startDate || undefined,
        endDate: filters.endDate || undefined,
        search: search || undefined,
    }), [page, filters, search]);
    const transactions = useTransactions(params);
    const hasFilters = search || Object.values(filters).some((value) => value && value !== 'all');

    const change = (name, value) => {
        setFilters((current) => ({ ...current, [name]: value }));
        setPage(1);
    };

    const exportUrl = '/api/transactions/export?' + new URLSearchParams(
        Object.fromEntries(Object.entries(params).filter(([key, value]) => value && key !== 'page' && key !== 'pageSize'))
    ).toString();

    return (
        <div className="page-stack">
            <div className="page-toolbar">
                <div className="segmented">
                    {['all', 'income', 'spending'].map((type) => (
                        <button key={type} className={filters.type === type ? 'active' : ''} onClick={() => change('type', type)}>
                            {type[0].toUpperCase() + type.slice(1)}
                        </button>
                    ))}
                </div>
                <div className="toolbar-actions">
                    <button className={'button secondary ' + (hasFilters ? 'active' : '')} onClick={() => setFiltersOpen(!filtersOpen)}><Filter /> Filters</button>
                    <a className="button secondary" href={exportUrl}><Download /> Export</a>
                    <button className="button" onClick={() => setImportOpen(true)}><FileUp /> Import</button>
                </div>
            </div>

            <Card className="transactions-card">
                <div className="transaction-search">
                    <Search />
                    <input value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} placeholder="Search transactions…" />
                    {transactions.isFetching && <RefreshCw className="spin" />}
                </div>

                {filtersOpen && (
                    <div className="filter-panel">
                        <Field label="Account"><select value={filters.accountId} onChange={(event) => change('accountId', event.target.value)}><option value="">All accounts</option>{accounts.data?.map((account) => <option key={account.id} value={account.id}>{account.name}</option>)}</select></Field>
                        <Field label="Category"><select value={filters.categoryId} onChange={(event) => change('categoryId', event.target.value)}><option value="">All categories</option>{categories.data?.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}</select></Field>
                        <Field label="From"><input type="date" value={filters.startDate} onChange={(event) => change('startDate', event.target.value)} /></Field>
                        <Field label="To"><input type="date" value={filters.endDate} onChange={(event) => change('endDate', event.target.value)} /></Field>
                        <button className="button ghost" onClick={() => { setFilters(initialFilters); setSearch(''); setPage(1); }}>Clear all</button>
                    </div>
                )}

                <PageState loading={transactions.isLoading} error={transactions.error && apiError(transactions.error)} empty={transactions.data?.items?.length === 0} emptyTitle="No matching transactions" emptyCopy="Try a different filter or import a bank statement.">
                    <TransactionTable items={transactions.data?.items || []} categories={categories.data || []} />
                    <Pagination page={page} totalPages={transactions.data?.totalPages || 1} totalItems={transactions.data?.totalItems || 0} onChange={setPage} />
                </PageState>
            </Card>
            <ImportModal open={importOpen} onClose={() => setImportOpen(false)} accounts={accounts.data || []} />
        </div>
    );
}

function TransactionTable({ items, categories }) {
    const updateCategory = useFinovaMutation(mutations.updateTransactionCategory, [
        ['transactions'], queryKeys.dashboard, ['insights'], queryKeys.budgets,
    ]);
    return (
        <>
            <div className="desktop-table-wrap">
                <table className="data-table">
                    <thead><tr><th>Date</th><th>Description</th><th>Category</th><th>Account</th><th>Status</th><th className="align-right">Amount</th></tr></thead>
                    <tbody>{items.map((item) => (
                        <tr key={item.id}>
                            <td>{shortDate(item.date)}</td>
                            <td><div className="description-cell"><span className={'transaction-mark small ' + (isIncomeTransaction(item) ? 'income' : '')}><WalletCards /></span><span><strong>{item.payee || item.memo || 'Transaction'}</strong><small>{transactionDetail(item)}</small></span></div></td>
                            <td><select className="table-select" value={item.categoryId || ''} onChange={(event) => updateCategory.mutate({ id: item.id, categoryId: Number(event.target.value), saveRule: true })}>{categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}</select></td>
                            <td>{item.accountName}</td>
                            <td><Pill tone={item.status === 'completed' ? 'success' : 'info'}>{item.status}</Pill></td>
                            <td className={'align-right amount ' + (isIncomeTransaction(item) ? 'positive' : '')}>{isIncomeTransaction(item) ? '+' : ''}{money(item.amount)}</td>
                        </tr>
                    ))}</tbody>
                </table>
            </div>
            <div className="mobile-transaction-list">
                {items.map((item) => (
                    <article className="mobile-transaction" key={item.id}>
                        <span className={'transaction-mark ' + (isIncomeTransaction(item) ? 'income' : '')}><WalletCards /></span>
                        <span><small>{relativeDate(item.date)}</small><strong>{item.payee || item.memo || 'Transaction'}</strong><em>{item.categoryName} · {item.accountName}{item.transactionTypeCode ? ` · ${item.transactionTypeCode}` : ''}</em></span>
                        <strong className={isIncomeTransaction(item) ? 'positive' : ''}>{isIncomeTransaction(item) ? '+' : ''}{money(item.amount)}</strong>
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
    return [type, item.memo && item.payee ? item.memo : null, item.sourceFileType || 'Imported'].filter(Boolean).join(' · ');
}

function Pagination({ page, totalPages, totalItems, onChange }) {
    return (
        <div className="pagination">
            <span>Showing page {page} of {totalPages} · {totalItems} transactions</span>
            <div><button className="icon-button" disabled={page <= 1} onClick={() => onChange(page - 1)}>‹</button><strong>{page}</strong><button className="icon-button" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>›</button></div>
        </div>
    );
}

function ImportModal({ open, onClose, accounts }) {
    const [accountId, setAccountId] = useState('');
    const [file, setFile] = useState(null);
    const mutation = useFinovaMutation(mutations.importTransactions, [
        ['transactions'], queryKeys.accounts, queryKeys.dashboard, ['insights'], queryKeys.goals,
    ]);
    const submit = async (event) => {
        event.preventDefault();
        const form = new FormData();
        form.append('AccountId', accountId);
        form.append('OfxContent', file);
        await mutation.mutateAsync(form);
    };
    return (
        <Modal open={open} onClose={onClose} title="Import transactions" copy="Upload an OFX, QIF, or text-based Halifax PDF statement. Finova skips matching activity automatically.">
            <form className="form-stack" onSubmit={submit}>
                <Field label="Account"><select required value={accountId} onChange={(event) => setAccountId(event.target.value)}><option value="">Choose an account</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.name}</option>)}</select></Field>
                <label className="file-drop">
                    <FileUp />
                    <strong>{file?.name || 'Choose an OFX, QIF, or Halifax PDF'}</strong>
                    <small>Maximum 10 MB</small>
                    <input type="file" accept=".ofx,.qif,.pdf,application/pdf" onChange={(event) => setFile(event.target.files[0])} />
                </label>
                {mutation.error && <p className="form-error">{apiError(mutation.error)}</p>}
                {mutation.isSuccess && <p className="form-success">Imported {mutation.data.imported} transactions; skipped {mutation.data.skipped} duplicates.</p>}
                <div className="modal-actions"><button type="button" className="button secondary" onClick={onClose}>Close</button><button className="button" disabled={!accountId || !file || mutation.isPending}>{mutation.isPending ? 'Importing…' : 'Import transactions'}</button></div>
            </form>
        </Modal>
    );
}
