import { useSearchParams } from 'react-router-dom';
import { useEffect, useState } from 'react';
import {
    Archive,
    ArrowDownToLine,
    ArrowLeftRight,
    ArrowUpFromLine,
    CalendarClock,
    Check,
    ChevronDown,
    CreditCard,
    Lightbulb,
    Pencil,
    Plus,
    RotateCcw,
    ShieldCheck,
    Sparkles,
    Trash2,
} from 'lucide-react';
import RecurringEditor from '../components/RecurringEditor';
import OccurrenceEditor from '../components/OccurrenceEditor';
import {
    Card,
    Field,
    InlineError,
    Modal,
    PageState,
    Pill,
    Progress,
} from '../components/ui';
import {
    apiError,
    money,
    percent,
    relativeDate,
    shortDate,
} from '../lib/format';
import {
    mutations,
    queryKeys,
    useAccounts,
    useBudgetMonths,
    useBudgets,
    useCategories,
    useFinovaMutation,
    useOccurrences,
    useRecurring,
    useSafety,
    useSuggestions,
} from '../lib/queries';

import { parseRecordId, useDeepLinkTarget } from '../utils/deepLink';

export default function PlanPage() {
    const [searchParams] = useSearchParams();
    const recurringId = parseRecordId(searchParams.get('recurringId'));
    const safety = useSafety();
    const recurring = useRecurring();
    const occurrences = useOccurrences();
    const budgetMonths = useBudgetMonths();
    const [budgetMonth, setBudgetMonth] = useState('');
    const selectedBudgetMonth =
        budgetMonth || budgetMonths.data?.currentMonth || '';
    const budgets = useBudgets(selectedBudgetMonth || undefined, true);
    const suggestions = useSuggestions();
    const accounts = useAccounts();
    const categories = useCategories();
    const [recurringEditor, setRecurringEditor] = useState(null);
    const [occurrenceEditor, setOccurrenceEditor] = useState(null);
    const [budgetOpen, setBudgetOpen] = useState(false);
    const [upcomingOpen, setUpcomingOpen] = useState(false);
    const [schedulesOpen, setSchedulesOpen] = useState(false);
    useEffect(() => {
        if (recurringId) setSchedulesOpen(true);
    }, [recurringId]);
    useEffect(() => {
        if (!budgetMonth && budgetMonths.data?.currentMonth)
            setBudgetMonth(budgetMonths.data.currentMonth);
    }, [budgetMonth, budgetMonths.data?.currentMonth]);

    const pageQueries = [
        safety,
        recurring,
        occurrences,
        budgetMonths,
        budgets,
        suggestions,
        accounts,
        categories,
    ];
    const loading = pageQueries.some((query) => query.isLoading);
    const error = pageQueries.find((query) => query.error)?.error;
    const upcomingItems = (occurrences.data || [])
        .filter((item) => item.status === 'expected')
        .slice(0, 12);
    const recurringItems = recurring.data || [];
    const activeRecurringCount = recurringItems.filter(
        (item) => item.isActive
    ).length;
    useDeepLinkTarget(
        recurringId,
        recurring.data && schedulesOpen,
        '[data-deep-link-type="recurring"]'
    );

    return (
        <PageState
            loading={loading}
            error={error && apiError(error)}
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
        >
            <div className="page-stack">
                <section className="section-heading">
                    <div>
                        <span className="eyebrow">Safe zone</span>
                        <h2>Money with breathing room</h2>
                        <p>
                            Each account keeps its floor and this month's expected bills
                            protected.
                        </p>
                    </div>
                </section>
                <div className="safety-grid">
                    {(safety.data || []).map((account) => (
                        <SafetyCard key={account.accountId} account={account} />
                    ))}
                    {(safety.data || []).length === 0 && (
                        <Card className="empty-inline">
                            <ShieldCheck />
                            <p>
                                Add an account in Settings to configure its safe
                                zone.
                            </p>
                        </Card>
                    )}
                </div>

                <CollapsiblePlanSection
                    id="upcoming-cash-flow"
                    eyebrow="Cash-flow calendar"
                    title="Upcoming bills and paydays"
                    copy="Only unmatched confirmed occurrences change safe to spend."
                    summary={
                        upcomingItems.length === 0
                            ? 'Nothing upcoming'
                            : `${upcomingItems.length} upcoming`
                    }
                    open={upcomingOpen}
                    onToggle={() => setUpcomingOpen((value) => !value)}
                >
                    <Card className="plan-list-card">
                        <OccurrenceTimeline
                            items={upcomingItems}
                            onEdit={setOccurrenceEditor}
                        />
                    </Card>
                </CollapsiblePlanSection>

                <CollapsiblePlanSection
                    id="recurring-household-schedules"
                    eyebrow="Recurring rules"
                    title="Flexible household schedules"
                    copy="Edit the rule for every future occurrence, or pause it without losing its history."
                    summary={
                        recurringItems.length === 0
                            ? 'No schedules'
                            : `${activeRecurringCount} active`
                    }
                    open={schedulesOpen}
                    onToggle={() => setSchedulesOpen((value) => !value)}
                    actions={
                        <button
                            className="button"
                            onClick={() => setRecurringEditor('new')}
                        >
                            <Plus /> Add recurring
                        </button>
                    }
                >
                    <Card className="plan-list-card">
                        <RecurringTimeline
                            items={recurringItems}
                            onEdit={setRecurringEditor}
                        />
                    </Card>
                </CollapsiblePlanSection>

                {(suggestions.data || []).length > 0 && (
                    <Suggestions items={suggestions.data} />
                )}

                <BudgetSection
                    months={budgetMonths.data}
                    selectedMonth={selectedBudgetMonth}
                    onMonth={setBudgetMonth}
                    budgets={budgets.data || []}
                    onEdit={setBudgetOpen}
                    onNew={() => setBudgetOpen(true)}
                />

                <RecurringEditor
                    open={Boolean(recurringEditor)}
                    item={
                        recurringEditor && recurringEditor !== 'new'
                            ? recurringEditor
                            : null
                    }
                    onClose={() => setRecurringEditor(null)}
                    accounts={accounts.data || []}
                    categories={categories.data || []}
                />
                <OccurrenceEditor
                    occurrence={occurrenceEditor}
                    onClose={() => setOccurrenceEditor(null)}
                />
                <BudgetModal
                    open={Boolean(budgetOpen)}
                    budget={typeof budgetOpen === 'object' ? budgetOpen : null}
                    onClose={() => setBudgetOpen(false)}
                    categories={categories.data || []}
                />
            </div>
        </PageState>
    );
}

function CollapsiblePlanSection({
    id,
    eyebrow,
    title,
    copy,
    summary,
    open,
    onToggle,
    actions,
    children,
}) {
    return (
        <section className="plan-collapsible-section">
            <div className="section-heading plan-collapsible-heading">
                <button
                    type="button"
                    className="plan-collapse-trigger"
                    aria-expanded={open}
                    aria-controls={id}
                    onClick={onToggle}
                >
                    <span className="plan-collapse-copy">
                        <span className="eyebrow">{eyebrow}</span>
                        <span className="plan-collapse-title-row">
                            <h2>{title}</h2>
                            <Pill>{summary}</Pill>
                        </span>
                        <span className="plan-collapse-description">
                            {copy}
                        </span>
                    </span>
                    <ChevronDown
                        className="plan-collapse-chevron"
                        aria-hidden="true"
                    />
                </button>
                {actions && (
                    <div className="plan-collapsible-actions">{actions}</div>
                )}
            </div>
            <div id={id} hidden={!open}>
                {children}
            </div>
        </section>
    );
}

function SafetyCard({ account }) {
    if (account.accountType === 'credit') {
        const utilisation = Number(account.creditUtilizationPercent || 0);
        const tone =
            utilisation >= 80
                ? 'danger'
                : utilisation >= 50
                  ? 'warning'
                  : 'info';
        return (
            <Card className="safety-card">
                <div className="card-heading">
                    <span className="account-dot account-0">
                        <CreditCard />
                    </span>
                    <Pill tone={tone}>
                        {account.creditLimit
                            ? `${percent(utilisation)} used`
                            : 'Credit debt'}
                    </Pill>
                </div>
                <h3>{account.accountName}</h3>
                <strong className="card-amount">
                    {money(account.debtBalance)} owed
                </strong>
                <small>
                    {account.creditLimit
                        ? `${money(account.availableCredit)} of ${money(account.creditLimit)} credit available`
                        : 'Add a credit limit in Settings to track utilisation'}
                </small>
                {account.creditLimit && (
                    <Progress
                        value={utilisation}
                        tone={utilisation >= 80 ? 'danger' : 'brand'}
                        label={account.accountName + ' credit utilisation'}
                    />
                )}
                <div className="safety-legend">
                    <span>
                        <i className="bills" />
                        Debt {money(account.debtBalance)}
                    </span>
                    {Number(account.balance) > 0 && (
                        <span>
                            <i className="safe" />
                            Credit balance {money(account.balance)}
                        </span>
                    )}
                </div>
            </Card>
        );
    }
    const healthy = Number(account.shortfall) === 0;
    return (
        <Card className="safety-card">
            <div className="card-heading">
                <span className="account-dot account-0">
                    <ShieldCheck />
                </span>
                <Pill tone={healthy ? 'success' : 'danger'}>
                    {healthy
                        ? 'Protected'
                        : money(account.shortfall) + ' short'}
                </Pill>
            </div>
            <h3>{account.accountName}</h3>
            <strong className="card-amount">
                {money(account.safeToSpend)}
            </strong>
            <small>
                safe to spend through {shortDate(account.horizonDate)}
            </small>
            <div className="money-bar" aria-label="Account safety breakdown">
                <span
                    className="money-bar-safe"
                    style={{ flex: Math.max(1, Number(account.safeToSpend)) }}
                />
                <span
                    className="money-bar-bills"
                    style={{ flex: Math.max(1, Number(account.upcomingBills)) }}
                />
                <span
                    className="money-bar-buffer"
                    style={{ flex: Math.max(1, Number(account.bufferAmount)) }}
                />
            </div>
            <div className="safety-legend">
                <span>
                    <i className="safe" />
                    Available {money(account.safeToSpend)}
                </span>
                <span>
                    <i className="bills" />
                    Bills {money(account.upcomingBills)}
                </span>
                <span>
                    <i className="buffer" />
                    Buffer {money(account.bufferAmount)}
                </span>
            </div>
        </Card>
    );
}

function OccurrenceTimeline({ items, onEdit }) {
    if (items.length === 0)
        return (
            <div className="empty-inline">
                <CalendarClock />
                <p>
                    No upcoming confirmed occurrences. Add a recurring rule or
                    confirm a suggestion.
                </p>
            </div>
        );
    return (
        <div className="recurring-list">
            {items.map((item) => (
                <article key={item.id} className="recurring-row occurrence-row">
                    <span className={'recurring-icon ' + item.kind}>
                        {item.kind === 'income' ? (
                            <ArrowDownToLine />
                        ) : item.kind === 'transfer' ? (
                            <ArrowLeftRight />
                        ) : (
                            <ArrowUpFromLine />
                        )}
                    </span>
                    <span>
                        <strong>{item.itemName}</strong>
                        <small>
                            {item.accountName} · due {shortDate(item.dueDate)}
                        </small>
                    </span>
                    <span>
                        <small>{relativeDate(item.dueDate)}</small>
                        <strong
                            className={item.kind === 'income' ? 'positive' : ''}
                        >
                            {item.kind === 'income'
                                ? '+'
                                : item.kind === 'transfer'
                                  ? '↔ '
                                  : '−'}
                            {money(item.expectedAmount)}
                        </strong>
                    </span>
                    <span className="recurring-actions">
                        <Pill tone="info">held</Pill>
                        <button
                            className="icon-button"
                            onClick={() => onEdit(item)}
                            aria-label={'Edit ' + item.itemName + ' occurrence'}
                        >
                            <Pencil />
                        </button>
                    </span>
                </article>
            ))}
        </div>
    );
}

function RecurringTimeline({ items, onEdit }) {
    if (items.length === 0)
        return (
            <div className="empty-inline">
                <CalendarClock />
                <p>
                    No recurring items yet. Add a payday or bill to make safe to
                    spend forward-looking.
                </p>
            </div>
        );
    return (
        <div className="recurring-list">
            {items.map((item) => (
                <article
                    key={item.id}
                    className="recurring-row"
                    data-deep-link-type="recurring"
                    data-deep-link-id={item.id}
                >
                    <span className={'recurring-icon ' + item.kind}>
                        {item.kind === 'income' ? (
                            <ArrowDownToLine />
                        ) : item.kind === 'transfer' ? (
                            <ArrowLeftRight />
                        ) : (
                            <ArrowUpFromLine />
                        )}
                    </span>
                    <span>
                        <strong>{item.name}</strong>
                        <small>
                            {item.accountName} · {item.frequency}
                        </small>
                    </span>
                    <span>
                        <small>{relativeDate(item.nextDate)}</small>
                        <strong
                            className={item.kind === 'income' ? 'positive' : ''}
                        >
                            {item.kind === 'income'
                                ? '+'
                                : item.kind === 'transfer'
                                  ? '↔ '
                                  : '−'}
                            {money(item.amount)}
                        </strong>
                    </span>
                    <span className="recurring-actions">
                        <Pill
                            tone={
                                !item.isActive
                                    ? 'warning'
                                    : item.lastMatchedDate
                                      ? 'success'
                                      : item.source === 'suggestion'
                                        ? 'info'
                                        : 'neutral'
                            }
                        >
                            {!item.isActive
                                ? 'paused'
                                : item.lastMatchedDate
                                  ? 'matching'
                                  : item.source}
                        </Pill>
                        <button
                            className="icon-button"
                            onClick={() => onEdit(item)}
                            aria-label={'Edit ' + item.name}
                        >
                            <Pencil />
                        </button>
                    </span>
                </article>
            ))}
        </div>
    );
}

function Suggestions({ items }) {
    const create = useFinovaMutation(
        mutations.createRecurring,
        [
            queryKeys.recurring,
            queryKeys.suggestions,
            queryKeys.safety,
            queryKeys.dashboard,
        ],
        { successMessage: 'Suggestion added to the recurring plan.' }
    );
    return (
        <Card className="suggestions-card">
            <div className="card-heading">
                <div>
                    <span className="eyebrow">
                        <Sparkles /> Pattern suggestions
                    </span>
                    <h3>Finova noticed these repeating</h3>
                </div>
                <Pill tone="info">Nothing changes until confirmed</Pill>
            </div>
            <div className="suggestion-grid">
                {items.slice(0, 3).map((item) => (
                    <article key={item.accountId + item.name}>
                        <span className={'recurring-icon ' + item.kind}>
                            {item.kind === 'income' ? (
                                <ArrowDownToLine />
                            ) : item.kind === 'transfer' ? (
                                <ArrowLeftRight />
                            ) : (
                                <ArrowUpFromLine />
                            )}
                        </span>
                        <span>
                            <strong>{item.name}</strong>
                            <small>
                                {item.frequency} ·{' '}
                                {Math.round(item.confidence * 100)}% confidence
                            </small>
                        </span>
                        <strong>{money(item.amount)}</strong>
                        <button
                            className="button small secondary"
                            disabled={create.isPending}
                            onClick={() =>
                                create.mutate({
                                    name: item.name,
                                    kind: item.kind,
                                    accountId: item.accountId,
                                    categoryId: null,
                                    amount: item.amount,
                                    frequency: item.frequency,
                                    nextDate: item.nextDate,
                                    source: 'suggestion',
                                    isActive: true,
                                })
                            }
                        >
                            <Check />{' '}
                            {create.isPending ? 'Confirming…' : 'Confirm'}
                        </button>
                    </article>
                ))}
            </div>
            <InlineError>{create.error && apiError(create.error)}</InlineError>
        </Card>
    );
}

function BudgetSection({
    months,
    selectedMonth,
    onMonth,
    budgets,
    onEdit,
    onNew,
}) {
    const close = useFinovaMutation(
        mutations.closeBudgetMonth,
        [queryKeys.budgets, queryKeys.dashboard],
        { successMessage: 'Budget month finalized.' }
    );
    const currentMonth = months?.currentMonth || selectedMonth;
    const summary = (months?.months || []).find(
        (item) => item.month === selectedMonth
    );
    const closed = summary?.isClosed || budgets.some((item) => item.isClosed);
    const isCurrent = selectedMonth === currentMonth;
    const activeBudgets = budgets.filter((item) => item.isActive);
    const inactiveBudgets = budgets.filter((item) => !item.isActive);
    const canEdit = isCurrent && !closed;
    const closeMonth = async () => {
        if (
            !selectedMonth ||
            closed ||
            !window.confirm(
                `Finalize ${monthLabel(selectedMonth)}? This permanently locks the budget snapshot and cannot be reopened.`
            )
        )
            return;
        try {
            await close.mutateAsync({ month: selectedMonth });
        } catch {
            // The mutation error remains visible beside the month controls.
        }
    };

    return (
        <section className="budget-section">
            <div className="section-heading budget-section-heading">
                <div>
                    <span className="eyebrow">Monthly budgets</span>
                    <h2>Spend with intention</h2>
                    <p>
                        Unused money can roll forward by category; overspending
                        never creates rollover debt.
                    </p>
                </div>
                <div className="budget-history-controls">
                    <label className="budget-month-field">
                        <span>Budget month</span>
                        <select
                            aria-label="Budget month"
                            value={selectedMonth}
                            onChange={(event) => onMonth(event.target.value)}
                        >
                            {(months?.months || []).map((item) => (
                                <option key={item.month} value={item.month}>
                                    {monthLabel(item.month)}
                                    {item.isClosed ? ' · Finalized' : ' · Open'}
                                </option>
                            ))}
                        </select>
                    </label>
                    <button
                        className="button secondary"
                        onClick={onNew}
                        disabled={!canEdit}
                        title={
                            canEdit
                                ? undefined
                                : 'Only the open current month can be edited.'
                        }
                    >
                        <Plus /> Set a budget
                    </button>
                    <button
                        className="button"
                        onClick={closeMonth}
                        disabled={
                            closed || budgets.length === 0 || close.isPending
                        }
                    >
                        {close.isPending
                            ? 'Finalizing…'
                            : closed
                              ? 'Month finalized'
                              : 'Close month'}
                    </button>
                </div>
            </div>
            <InlineError>{close.error && apiError(close.error)}</InlineError>
            {closed && (
                <div className="budget-finalized-note">
                    <Archive aria-hidden="true" />
                    <span>
                        {monthLabel(selectedMonth)} is finalized. Its snapshot
                        and rollover boundary are permanent.
                    </span>
                </div>
            )}
            <div className="budget-grid">
                {activeBudgets.map((budget) => (
                    <BudgetCard
                        key={budget.id}
                        budget={budget}
                        readOnly={!canEdit}
                        onEdit={() => onEdit(budget)}
                    />
                ))}
                {activeBudgets.length === 0 && inactiveBudgets.length === 0 && (
                    <Card className="empty-inline">
                        <Lightbulb />
                        <p>
                            Set the first monthly category budget to start
                            measuring pace.
                        </p>
                    </Card>
                )}
            </div>
            {inactiveBudgets.length > 0 && (
                <div className="budget-inactive-section">
                    <div className="card-heading">
                        <div>
                            <span className="eyebrow">Inactive budgets</span>
                            <p>
                                These budgets remain available in history but no
                                longer appear in active totals.
                            </p>
                        </div>
                        <Pill tone="warning">
                            {inactiveBudgets.length} inactive
                        </Pill>
                    </div>
                    <div className="budget-inactive-list">
                        {inactiveBudgets.map((budget) => (
                            <BudgetCard
                                key={budget.id}
                                budget={budget}
                                readOnly
                                controlsEnabled={canEdit}
                                onEdit={() => {}}
                            />
                        ))}
                    </div>
                </div>
            )}
        </section>
    );
}

function BudgetCard({ budget, readOnly, controlsEnabled, onEdit }) {
    const over = Number(budget.remainingAmount) < 0;
    const canControl = controlsEnabled ?? !readOnly;
    const invalidate = [queryKeys.budgets, queryKeys.dashboard];
    const active = useFinovaMutation(mutations.setBudgetActive, invalidate, {
        successMessage: (data) =>
            data?.isActive ? 'Budget reactivated.' : 'Budget deactivated.',
    });
    const remove = useFinovaMutation(mutations.deleteBudget, invalidate, {
        successMessage: 'Budget deleted.',
    });
    const setActive = async (event) => {
        event.stopPropagation();
        if (
            !budget.isActive ||
            window.confirm(
                `Deactivate the ${budget.categoryName} budget? Its history will remain intact.`
            )
        ) {
            try {
                await active.mutateAsync({
                    id: budget.id,
                    isActive: !budget.isActive,
                });
            } catch {
                // The mutation error remains visible on the card.
            }
        }
    };
    const deleteBudget = async (event) => {
        event.stopPropagation();
        if (
            !window.confirm(
                `Delete the ${budget.categoryName} budget? Budgets with history must be deactivated instead.`
            )
        )
            return;
        try {
            await remove.mutateAsync(budget.id);
        } catch {
            // The mutation error remains visible on the card.
        }
    };
    const pending = active.isPending || remove.isPending;
    return (
        <Card
            className={
                'budget-card' + (readOnly ? ' budget-card-readonly' : '')
            }
            onClick={readOnly ? undefined : onEdit}
        >
            <div className="card-heading">
                <span
                    className={
                        'category-badge category-' +
                        (budget.colorKey || 'slate')
                    }
                >
                    {budget.categoryName}
                </span>
                <div className="budget-card-heading-actions">
                    <Pill
                        tone={
                            budget.isClosed
                                ? 'neutral'
                                : over
                                  ? 'danger'
                                  : budget.progressPercent >= 80
                                    ? 'warning'
                                    : 'success'
                        }
                    >
                        {budget.isClosed
                            ? 'Finalized'
                            : percent(budget.progressPercent)}
                    </Pill>
                    {!readOnly && (
                        <button
                            className="icon-button"
                            type="button"
                            onClick={onEdit}
                            aria-label={'Edit ' + budget.categoryName}
                        >
                            <Pencil />
                        </button>
                    )}
                </div>
            </div>
            <div className="budget-values">
                <strong>{money(budget.spentAmount)}</strong>
                <span>of {money(budget.availableAmount)}</span>
            </div>
            <Progress
                value={budget.progressPercent}
                tone={over ? 'danger' : 'brand'}
                label={budget.categoryName + ' budget'}
            />
            <div className="budget-footer">
                <span>
                    {over
                        ? money(Math.abs(budget.remainingAmount)) + ' over'
                        : money(budget.remainingAmount) + ' left'}
                </span>
                {Number(budget.scheduledAmount) > 0 && (
                    <span>
                        {money(budget.scheduledAmount)} scheduled ·{' '}
                        {money(budget.remainingAfterScheduled)} after planned
                    </span>
                )}
                {budget.rolloverEnabled && (
                    <span>{money(budget.rolloverIn)} rolled in</span>
                )}
            </div>
            {canControl && budget.isActive && (
                <div className="budget-card-actions">
                    <button
                        className="button small secondary"
                        type="button"
                        onClick={setActive}
                        disabled={pending}
                    >
                        <Archive /> Deactivate
                    </button>
                    <button
                        className="button small danger ghost"
                        type="button"
                        onClick={deleteBudget}
                        disabled={pending}
                    >
                        <Trash2 /> Delete
                    </button>
                </div>
            )}
            {canControl && !budget.isActive && !budget.isClosed && (
                <div className="budget-card-actions">
                    <button
                        className="button small secondary"
                        type="button"
                        onClick={setActive}
                        disabled={pending}
                    >
                        <RotateCcw /> Reactivate
                    </button>
                    <button
                        className="button small danger ghost"
                        type="button"
                        onClick={deleteBudget}
                        disabled={pending}
                    >
                        <Trash2 /> Delete
                    </button>
                </div>
            )}
            <InlineError>
                {(active.error || remove.error) &&
                    apiError(active.error || remove.error)}
            </InlineError>
        </Card>
    );
}

function monthLabel(value) {
    if (!value) return 'Current month';
    return new Intl.DateTimeFormat(undefined, {
        month: 'long',
        year: 'numeric',
        timeZone: 'UTC',
    }).format(new Date(value + 'T00:00:00Z'));
}

function BudgetModal({ open, budget, onClose, categories }) {
    const [categoryId, setCategoryId] = useState(budget?.categoryId || '');
    const [amount, setAmount] = useState(budget?.monthlyAmount || '');
    const [rollover, setRollover] = useState(budget?.rolloverEnabled || false);
    const save = useFinovaMutation(
        mutations.saveBudget,
        [queryKeys.budgets, queryKeys.dashboard],
        { successMessage: 'Monthly budget saved.' }
    );
    useEffect(() => {
        setCategoryId(budget?.categoryId || '');
        setAmount(budget?.monthlyAmount || '');
        setRollover(budget?.rolloverEnabled || false);
    }, [budget, open]);
    const submit = async (event) => {
        event.preventDefault();
        try {
            await save.mutateAsync({
                categoryId: Number(categoryId),
                monthlyAmount: Number(amount),
                rolloverEnabled: rollover,
            });
            onClose();
        } catch {
            // The mutation error remains visible in the open form.
        }
    };
    return (
        <Modal
            open={open}
            onClose={onClose}
            title={budget ? 'Edit monthly budget' : 'Set a monthly budget'}
        >
            <form className="form-stack" onSubmit={submit}>
                <Field label="Category">
                    <select
                        required
                        value={categoryId}
                        disabled={Boolean(budget)}
                        onChange={(event) => setCategoryId(event.target.value)}
                    >
                        <option value="">Choose category</option>
                        {categories
                            .filter((item) => item.kind === 'expense')
                            .map((item) => (
                                <option key={item.id} value={item.id}>
                                    {item.name}
                                </option>
                            ))}
                    </select>
                </Field>
                <Field label="Monthly amount">
                    <input
                        required
                        type="number"
                        step="0.01"
                        min="0"
                        value={amount}
                        onChange={(event) => setAmount(event.target.value)}
                    />
                </Field>
                <label className="check-row">
                    <input
                        type="checkbox"
                        checked={rollover}
                        onChange={(event) => setRollover(event.target.checked)}
                    />
                    <span>
                        <strong>Roll unused money forward</strong>
                        <small>Overspending will not reduce next month.</small>
                    </span>
                </label>
                <InlineError>{save.error && apiError(save.error)}</InlineError>
                <div className="modal-actions">
                    <button
                        type="button"
                        className="button secondary"
                        onClick={onClose}
                    >
                        Cancel
                    </button>
                    <button className="button" disabled={save.isPending}>
                        {save.isPending ? 'Saving…' : 'Save budget'}
                    </button>
                </div>
            </form>
        </Modal>
    );
}
