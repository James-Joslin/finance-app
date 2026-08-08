import { useEffect, useState } from 'react';
import { ArrowDownToLine, ArrowUpFromLine, CalendarClock, Check, Lightbulb, Plus, ShieldCheck, Sparkles } from 'lucide-react';
import { Card, Field, Modal, PageState, Pill, Progress } from '../components/ui';
import { apiError, money, percent, relativeDate, shortDate } from '../lib/format';
import {
    mutations, queryKeys, useAccounts, useBudgets, useCategories, useFinovaMutation,
    useRecurring, useSafety, useSuggestions,
} from '../lib/queries';

export default function PlanPage() {
    const safety = useSafety();
    const recurring = useRecurring();
    const budgets = useBudgets();
    const suggestions = useSuggestions();
    const accounts = useAccounts();
    const categories = useCategories();
    const [recurringOpen, setRecurringOpen] = useState(false);
    const [budgetOpen, setBudgetOpen] = useState(false);

    const loading = safety.isLoading || recurring.isLoading || budgets.isLoading;
    const error = safety.error || recurring.error || budgets.error;

    return (
        <PageState loading={loading} error={error && apiError(error)}>
            <div className="page-stack">
                <section className="section-heading">
                    <div><span className="eyebrow">Safe zone</span><h2>Money with breathing room</h2><p>Each account keeps its floor and near-term commitments protected.</p></div>
                </section>
                <div className="safety-grid">
                    {(safety.data || []).map((account) => <SafetyCard key={account.accountId} account={account} />)}
                    {(safety.data || []).length === 0 && <Card className="empty-inline"><ShieldCheck /><p>Add an account in Settings to configure its safe zone.</p></Card>}
                </div>

                <section className="section-heading">
                    <div><span className="eyebrow">Cash-flow calendar</span><h2>Upcoming bills and paydays</h2><p>Only confirmed items change safe to spend.</p></div>
                    <button className="button" onClick={() => setRecurringOpen(true)}><Plus /> Add recurring</button>
                </section>
                <Card className="plan-list-card">
                    <RecurringTimeline items={recurring.data || []} />
                </Card>

                {(suggestions.data || []).length > 0 && <Suggestions items={suggestions.data} />}

                <section className="section-heading">
                    <div><span className="eyebrow">Monthly budgets</span><h2>Spend with intention</h2><p>Unused money can roll forward by category; overspending never creates rollover debt.</p></div>
                    <button className="button secondary" onClick={() => setBudgetOpen(true)}><Plus /> Set a budget</button>
                </section>
                <div className="budget-grid">
                    {(budgets.data || []).map((budget) => <BudgetCard key={budget.id} budget={budget} onEdit={() => setBudgetOpen(budget)} />)}
                    {(budgets.data || []).length === 0 && <Card className="empty-inline"><Lightbulb /><p>Set the first monthly category budget to start measuring pace.</p></Card>}
                </div>

                <RecurringModal open={recurringOpen} onClose={() => setRecurringOpen(false)} accounts={accounts.data || []} categories={categories.data || []} />
                <BudgetModal open={Boolean(budgetOpen)} budget={typeof budgetOpen === 'object' ? budgetOpen : null} onClose={() => setBudgetOpen(false)} categories={categories.data || []} />
            </div>
        </PageState>
    );
}

function SafetyCard({ account }) {
    const healthy = Number(account.shortfall) === 0;
    return (
        <Card className="safety-card">
            <div className="card-heading"><span className="account-dot account-0"><ShieldCheck /></span><Pill tone={healthy ? 'success' : 'danger'}>{healthy ? 'Protected' : money(account.shortfall) + ' short'}</Pill></div>
            <h3>{account.accountName}</h3>
            <strong className="card-amount">{money(account.safeToSpend)}</strong>
            <small>safe to spend through {shortDate(account.horizonDate)}</small>
            <div className="money-bar" aria-label="Account safety breakdown">
                <span className="money-bar-safe" style={{ flex: Math.max(1, Number(account.safeToSpend)) }} />
                <span className="money-bar-bills" style={{ flex: Math.max(1, Number(account.upcomingBills)) }} />
                <span className="money-bar-buffer" style={{ flex: Math.max(1, Number(account.bufferAmount)) }} />
            </div>
            <div className="safety-legend"><span><i className="safe" />Available {money(account.safeToSpend)}</span><span><i className="bills" />Bills {money(account.upcomingBills)}</span><span><i className="buffer" />Buffer {money(account.bufferAmount)}</span></div>
        </Card>
    );
}

function RecurringTimeline({ items }) {
    if (items.length === 0) return <div className="empty-inline"><CalendarClock /><p>No recurring items yet. Add a payday or bill to make safe to spend forward-looking.</p></div>;
    return (
        <div className="recurring-list">
            {items.map((item) => (
                <article key={item.id} className="recurring-row">
                    <span className={'recurring-icon ' + item.kind}>{item.kind === 'income' ? <ArrowDownToLine /> : <ArrowUpFromLine />}</span>
                    <span><strong>{item.name}</strong><small>{item.accountName} · {item.frequency}</small></span>
                    <span><small>{relativeDate(item.nextDate)}</small><strong className={item.kind === 'income' ? 'positive' : ''}>{item.kind === 'income' ? '+' : '−'}{money(item.amount)}</strong></span>
                    <Pill tone={item.source === 'suggestion' ? 'info' : 'neutral'}>{item.source}</Pill>
                </article>
            ))}
        </div>
    );
}

function Suggestions({ items }) {
    const create = useFinovaMutation(mutations.createRecurring, [
        queryKeys.recurring, queryKeys.suggestions, queryKeys.safety, queryKeys.dashboard,
    ]);
    return (
        <Card className="suggestions-card">
            <div className="card-heading"><div><span className="eyebrow"><Sparkles /> Pattern suggestions</span><h3>Finova noticed these repeating</h3></div><Pill tone="info">Nothing changes until confirmed</Pill></div>
            <div className="suggestion-grid">
                {items.slice(0, 3).map((item) => (
                    <article key={item.accountId + item.name}>
                        <span className={'recurring-icon ' + item.kind}>{item.kind === 'income' ? <ArrowDownToLine /> : <ArrowUpFromLine />}</span>
                        <span><strong>{item.name}</strong><small>{item.frequency} · {Math.round(item.confidence * 100)}% confidence</small></span>
                        <strong>{money(item.amount)}</strong>
                        <button className="button small secondary" onClick={() => create.mutate({
                            name: item.name, kind: item.kind, accountId: item.accountId, categoryId: null,
                            amount: item.amount, frequency: item.frequency, nextDate: item.nextDate, source: 'suggestion', isActive: true,
                        })}><Check /> Confirm</button>
                    </article>
                ))}
            </div>
        </Card>
    );
}

function BudgetCard({ budget, onEdit }) {
    const over = Number(budget.remainingAmount) < 0;
    return (
        <Card className="budget-card" onClick={onEdit}>
            <div className="card-heading"><span className={'category-chip color-' + budget.colorKey}>{budget.categoryName}</span><Pill tone={over ? 'danger' : budget.progressPercent >= 80 ? 'warning' : 'success'}>{percent(budget.progressPercent)}</Pill></div>
            <div className="budget-values"><strong>{money(budget.spentAmount)}</strong><span>of {money(budget.availableAmount)}</span></div>
            <Progress value={budget.progressPercent} tone={over ? 'danger' : 'brand'} label={budget.categoryName + ' budget'} />
            <div className="budget-footer"><span>{over ? money(Math.abs(budget.remainingAmount)) + ' over' : money(budget.remainingAmount) + ' left'}</span>{budget.rolloverEnabled && <span>{money(budget.rolloverIn)} rolled in</span>}</div>
        </Card>
    );
}

function RecurringModal({ open, onClose, accounts, categories }) {
    const [form, setForm] = useState({ name: '', kind: 'bill', accountId: '', categoryId: '', amount: '', frequency: 'monthly', nextDate: '' });
    const save = useFinovaMutation(mutations.createRecurring, [queryKeys.recurring, queryKeys.safety, queryKeys.dashboard, queryKeys.suggestions]);
    const submit = async (event) => {
        event.preventDefault();
        await save.mutateAsync({
            ...form, accountId: Number(form.accountId), categoryId: form.categoryId ? Number(form.categoryId) : null,
            amount: Number(form.amount), source: 'manual', isActive: true,
        });
        onClose();
    };
    return (
        <Modal open={open} onClose={onClose} title="Add a recurring item" copy="Confirmed bills and income make your safe-to-spend figure more useful.">
            <form className="form-grid" onSubmit={submit}>
                <Field label="Name" className="span-2"><input required value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} placeholder="Mortgage, payday…" /></Field>
                <Field label="Type"><select value={form.kind} onChange={(event) => setForm({ ...form, kind: event.target.value })}><option value="bill">Bill</option><option value="income">Income / payday</option></select></Field>
                <Field label="Amount"><input required min="0.01" step="0.01" type="number" value={form.amount} onChange={(event) => setForm({ ...form, amount: event.target.value })} /></Field>
                <Field label="Account"><select required value={form.accountId} onChange={(event) => setForm({ ...form, accountId: event.target.value })}><option value="">Choose account</option>{accounts.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></Field>
                <Field label="Category"><select value={form.categoryId} onChange={(event) => setForm({ ...form, categoryId: event.target.value })}><option value="">No category</option>{categories.filter((item) => item.kind !== 'income' || form.kind === 'income').map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></Field>
                <Field label="Frequency"><select value={form.frequency} onChange={(event) => setForm({ ...form, frequency: event.target.value })}><option value="weekly">Weekly</option><option value="fortnightly">Fortnightly</option><option value="monthly">Monthly</option><option value="quarterly">Quarterly</option><option value="yearly">Yearly</option></select></Field>
                <Field label="Next date"><input required type="date" value={form.nextDate} onChange={(event) => setForm({ ...form, nextDate: event.target.value })} /></Field>
                {save.error && <p className="form-error span-2">{apiError(save.error)}</p>}
                <div className="modal-actions span-2"><button type="button" className="button secondary" onClick={onClose}>Cancel</button><button className="button" disabled={save.isPending}>{save.isPending ? 'Saving…' : 'Add to plan'}</button></div>
            </form>
        </Modal>
    );
}

function BudgetModal({ open, budget, onClose, categories }) {
    const [categoryId, setCategoryId] = useState(budget?.categoryId || '');
    const [amount, setAmount] = useState(budget?.monthlyAmount || '');
    const [rollover, setRollover] = useState(budget?.rolloverEnabled || false);
    const save = useFinovaMutation(mutations.saveBudget, [queryKeys.budgets, queryKeys.dashboard]);
    useEffect(() => {
        setCategoryId(budget?.categoryId || '');
        setAmount(budget?.monthlyAmount || '');
        setRollover(budget?.rolloverEnabled || false);
    }, [budget, open]);
    const submit = async (event) => {
        event.preventDefault();
        await save.mutateAsync({ categoryId: Number(categoryId), monthlyAmount: Number(amount), rolloverEnabled: rollover });
        onClose();
    };
    return (
        <Modal open={open} onClose={onClose} title={budget ? 'Edit monthly budget' : 'Set a monthly budget'}>
            <form className="form-stack" onSubmit={submit}>
                <Field label="Category"><select required value={categoryId} disabled={Boolean(budget)} onChange={(event) => setCategoryId(event.target.value)}><option value="">Choose category</option>{categories.filter((item) => item.kind === 'expense').map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></Field>
                <Field label="Monthly amount"><input required type="number" step="0.01" min="0" value={amount} onChange={(event) => setAmount(event.target.value)} /></Field>
                <label className="check-row"><input type="checkbox" checked={rollover} onChange={(event) => setRollover(event.target.checked)} /><span><strong>Roll unused money forward</strong><small>Overspending will not reduce next month.</small></span></label>
                <div className="modal-actions"><button type="button" className="button secondary" onClick={onClose}>Cancel</button><button className="button">Save budget</button></div>
            </form>
        </Modal>
    );
}
