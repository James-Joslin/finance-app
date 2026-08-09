import { ArrowDownRight, ArrowRight, ArrowUpRight, CalendarDays, Landmark, ShieldCheck, Sparkles, WalletCards } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { GoalVisual } from '../components/GoalVisual';
import { Card, PageState, Pill, Progress } from '../components/ui';
import { money, percent, relativeDate, shortDate } from '../lib/format';
import { useDashboard, useInsights } from '../lib/queries';

export default function OverviewPage() {
    const dashboard = useDashboard();
    const today = new Date();
    const startDate = new Date(today.getFullYear(), today.getMonth(), 1).toISOString().slice(0, 10);
    const endDate = today.toISOString().slice(0, 10);
    const insights = useInsights({ startDate, endDate });

    return (
        <PageState loading={dashboard.isLoading} error={dashboard.error?.message}>
            <div className="overview-grid">
                <SafeToSpendCard data={dashboard.data} />
                <BalanceTrendCard data={insights.data} />
                <AccountsCard accounts={dashboard.data?.accounts || []} />
                <RecentTransactionsCard items={dashboard.data?.recentTransactions || []} />
                <PriorityGoalCard goal={dashboard.data?.priorityGoal} />
                <SnapshotCard data={insights.data} warnings={dashboard.data?.budgetWarnings || []} />
            </div>
        </PageState>
    );
}

function SafeToSpendCard({ data }) {
    const hasShortfall = Number(data?.shortfall) > 0;
    return (
        <Card className="safe-card">
            <div className="card-heading">
                <span className="eyebrow"><ShieldCheck /> Safe to spend</span>
                <Pill tone={hasShortfall ? 'danger' : 'success'}>{hasShortfall ? 'Needs attention' : 'Protected'}</Pill>
            </div>
            <strong className="hero-amount">{money(data?.safeToSpend)}</strong>
            <p>Available after buffers and confirmed bills—not including money that has not arrived yet.</p>
            <div className="safe-breakdown">
                <span><small>Net position</small><strong>{money(data?.totalBalance)}</strong></span>
                <span><small>Assets</small><strong>{money(data?.totalAssets)}</strong></span>
                <span><small>Debt</small><strong>{money(data?.totalDebt)}</strong></span>
                <span><small>Protected</small><strong>{money(data?.totalProtected)}</strong></span>
                <span><small>Upcoming</small><strong>{money(data?.upcomingCommitments)}</strong></span>
            </div>
            {hasShortfall && <div className="inline-alert">Household shortfall: <strong>{money(data.shortfall)}</strong></div>}
            <div className="safe-landscape" aria-hidden="true"><span /><span /><i /></div>
        </Card>
    );
}

function BalanceTrendCard({ data }) {
    const trend = data?.balanceTrend || [];
    return (
        <Card className="trend-card">
            <div className="card-heading">
                <div><span className="eyebrow">Net balance trend</span><strong className="card-amount">{money(data?.totalBalance)}</strong></div>
                <Pill tone={Number(data?.netSavings) >= 0 ? 'success' : 'danger'}>
                    {Number(data?.netSavings) >= 0 ? <ArrowUpRight /> : <ArrowDownRight />}
                    {money(Math.abs(Number(data?.netSavings || 0)))} this month
                </Pill>
            </div>
            <div className="mini-chart" aria-label="Balance trend chart">
                {trend.length > 1 ? (
                    <ResponsiveContainer width="100%" height="100%">
                        <AreaChart data={trend}>
                            <defs><linearGradient id="balance-fill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#168bff" stopOpacity=".25" /><stop offset="1" stopColor="#168bff" stopOpacity="0" /></linearGradient></defs>
                            <CartesianGrid stroke="var(--border)" vertical={false} />
                            <XAxis dataKey="date" hide />
                            <YAxis hide domain={['dataMin', 'dataMax']} />
                            <Tooltip formatter={(value) => money(value)} labelFormatter={shortDate} contentStyle={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 12 }} />
                            <Area type="monotone" dataKey="value" stroke="#168bff" strokeWidth={2.4} fill="url(#balance-fill)" />
                        </AreaChart>
                    </ResponsiveContainer>
                ) : <div className="chart-empty">Import more activity to reveal a trend.</div>}
            </div>
        </Card>
    );
}

function AccountsCard({ accounts }) {
    return (
        <Card className="accounts-card">
            <div className="card-heading"><span className="eyebrow">Your accounts</span><Link to="/settings">Manage</Link></div>
            <div className="account-list">
                {accounts.length === 0 && <p className="muted">Add an account to get started.</p>}
                {accounts.slice(0, 5).map((account, index) => (
                    <div className="account-row" key={account.accountId}>
                        <span className={'account-dot account-' + (index % 5)}><Landmark /></span>
                        <span><strong>{account.accountName}</strong><small>{account.accountType === 'credit' ? 'Credit card debt' : `${money(account.bufferAmount)} protected`}</small></span>
                        <AccountSafetyValues account={account} />
                    </div>
                ))}
            </div>
        </Card>
    );
}

function AccountSafetyValues({ account }) {
    if (account.accountType !== 'credit') {
        return <span className="account-values"><strong>{money(account.balance)}</strong><small>{money(account.safeToSpend)} safe</small></span>;
    }

    const position = Number(account.debtBalance) > 0
        ? `${money(account.debtBalance)} owed`
        : Number(account.balance) > 0 ? `${money(account.balance)} in credit` : 'Settled';
    return <span className="account-values"><strong>{position}</strong><small>{account.creditLimit
        ? `${money(account.availableCredit)} available · ${percent(account.creditUtilizationPercent)} used`
        : 'Limit not set'}</small></span>;
}

function RecentTransactionsCard({ items }) {
    return (
        <Card className="recent-card">
            <div className="card-heading"><span className="eyebrow">Recent transactions</span><Link to="/transactions">See all</Link></div>
            <div className="transaction-list">
                {items.length === 0 && <p className="muted">No transactions yet.</p>}
                {items.map((item) => (
                    <div className="transaction-row" key={item.id}>
                        <span className={'transaction-mark ' + (isIncomeTransaction(item) ? 'income' : '')}>
                            {isIncomeTransaction(item) ? <ArrowDownRight /> : <WalletCards />}
                        </span>
                        <span><strong>{item.payee || item.memo || 'Transaction'}</strong><small>{item.categoryName} · {relativeDate(item.date)}</small></span>
                        <strong className={isIncomeTransaction(item) ? 'positive' : ''}>{isIncomeTransaction(item) ? '+' : ''}{money(item.amount)}</strong>
                    </div>
                ))}
            </div>
        </Card>
    );
}

function isIncomeTransaction(item) {
    return Number(item.amount) >= 0 && item.accountType !== 'credit';
}

function PriorityGoalCard({ goal }) {
    return (
        <Card className="priority-card">
            <div className="card-heading"><span className="eyebrow">Priority goal</span><Link to="/goals">All goals</Link></div>
            {!goal ? (
                <div className="goal-empty"><Sparkles /><strong>Give your money a destination</strong><p>Create a goal and Finova will calculate the path.</p><Link className="button secondary" to="/goals">Create a goal</Link></div>
            ) : (
                <>
                    <GoalVisual iconKey={goal.iconKey} colorKey={goal.colorKey} imageUrl={goal.imageUrl} label={goal.name} />
                    <div className="goal-card-title"><div><strong>{goal.name}</strong><small>{goal.description || goal.accountName}</small></div><b>{percent(goal.progressPercent)}</b></div>
                    <Progress value={goal.progressPercent} />
                    <div className="goal-meta"><span><strong>{money(goal.allocatedAmount)}</strong> of {money(goal.targetAmount)}</span><span><CalendarDays /> {goal.targetDate ? shortDate(goal.targetDate) : 'No target date'}</span></div>
                </>
            )}
        </Card>
    );
}

function SnapshotCard({ data, warnings }) {
    const savingRate = Number(data?.savingsRate || 0);
    return (
        <Card className="snapshot-card">
            <div className="card-heading"><span className="eyebrow">This month</span><Link to="/insights">Explore <ArrowRight /></Link></div>
            <div className="snapshot-ring" style={{ '--progress': Math.max(0, Math.min(100, savingRate)) + '%' }}>
                <span><strong>{percent(savingRate)}</strong><small>saved</small></span>
            </div>
            <div className="snapshot-stats">
                <span><small>Income</small><strong className="positive">{money(data?.income)}</strong></span>
                <span><small>Spending</small><strong>{money(data?.spending)}</strong></span>
            </div>
            {warnings.length > 0
                ? <div className="insight-callout warning"><span>!</span><p><strong>Watch {warnings[0].categoryName}</strong><br />{percent(warnings[0].progressPercent)} of this month’s plan is used.</p></div>
                : <div className="insight-callout"><Sparkles /><p><strong>Your plan looks steady</strong><br />Finova will highlight meaningful changes here.</p></div>}
        </Card>
    );
}
