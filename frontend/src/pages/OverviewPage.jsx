import {
    ArrowDownRight,
    ArrowRight,
    ArrowUpRight,
    CalendarDays,
    Landmark,
    ShieldCheck,
    Sparkles,
    WalletCards,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import {
    Area,
    AreaChart,
    CartesianGrid,
    ResponsiveContainer,
    Tooltip,
    XAxis,
    YAxis,
} from 'recharts';
import { GoalVisual } from '../components/GoalVisual';
import { Card, PageState, Pill, Progress } from '../components/ui';
import { useTheme } from '../contexts/ThemeContext';
import {
    money,
    percent,
    relativeDate,
    shortDate,
    todayIso,
} from '../lib/format';
import { useDashboard, useInsights, useOccurrences } from '../lib/queries';
import { staticAssetUrl } from '../lib/staticAssets';
import { currentMonthRange, overviewTrendRange } from '../utils/trendRange';

export default function OverviewPage() {
    const dashboard = useDashboard();
    const { resolved } = useTheme();
    const trendRange = overviewTrendRange(dashboard.data?.recentTransactions);
    const monthRange = currentMonthRange();
    const insights = useInsights(trendRange);
    const monthInsights = useInsights(monthRange);
    const occurrences = useOccurrences();
    const upcomingExpenditures = (occurrences.data || [])
        .filter(
            (item) =>
                item.status === 'expected' &&
                item.kind === 'bill' &&
                item.dueDate >= todayIso()
        )
        .slice(0, 4);
    const pageQueries = [dashboard, insights, monthInsights, occurrences];

    return (
        <PageState
            loading={pageQueries.some((query) => query.isLoading)}
            error={pageQueries.find((query) => query.error)?.error?.message}
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
            <div className="overview-grid">
                <SafeToSpendCard data={dashboard.data} theme={resolved} />
                <BalanceTrendCard data={insights.data} range={trendRange} />
                <AccountsCard
                    accounts={dashboard.data?.accounts || []}
                    theme={resolved}
                />
                <RecentTransactionsCard
                    items={dashboard.data?.recentTransactions || []}
                />
                <PriorityGoalCard goal={dashboard.data?.priorityGoal} />
                <SnapshotCard
                    data={monthInsights.data}
                    warnings={dashboard.data?.budgetWarnings || []}
                />
                <UpcomingExpenditureCard items={upcomingExpenditures} />
            </div>
        </PageState>
    );
}

function SafeToSpendCard({ data, theme }) {
    const hasShortfall = Number(data?.shortfall) > 0;
    return (
        <Card className="safe-card">
            <div className="card-heading">
                <span className="eyebrow">
                    <ShieldCheck /> Safe to spend
                </span>
                <Pill tone={hasShortfall ? 'danger' : 'success'}>
                    {hasShortfall ? 'Needs attention' : 'Protected'}
                </Pill>
            </div>
            <strong className="hero-amount">{money(data?.safeToSpend)}</strong>
            <p>
                Available after buffers and confirmed bills—not including money
                that has not arrived yet.
            </p>
            <div className="safe-breakdown">
                <span>
                    <small>Net position</small>
                    <strong>{money(data?.totalBalance)}</strong>
                </span>
                <span>
                    <small>Assets</small>
                    <strong>{money(data?.totalAssets)}</strong>
                </span>
                <span>
                    <small>Debt</small>
                    <strong>{money(data?.totalDebt)}</strong>
                </span>
                <span>
                    <small>Protected</small>
                    <strong>{money(data?.totalProtected)}</strong>
                </span>
                <span>
                    <small>Upcoming</small>
                    <strong>{money(data?.upcomingCommitments)}</strong>
                </span>
            </div>
            {hasShortfall && (
                <div className="inline-alert">
                    Household shortfall:{' '}
                    <strong>{money(data.shortfall)}</strong>
                </div>
            )}
            <div className="safe-art-stage" aria-hidden="true">
                <img
                    className="safe-landscape-art"
                    src={staticAssetUrl(
                        `landscapes/landscape_hills_tree${theme === 'dark' ? '_night' : ''}.png`
                    )}
                    alt=""
                />
                <img
                    className="safe-circles-art"
                    src={staticAssetUrl(
                        `micro_elements/decor_circles${theme === 'dark' ? '_night' : ''}.png`
                    )}
                    alt=""
                />
            </div>
        </Card>
    );
}

function BalanceTrendCard({ data, range }) {
    const trend = data?.balanceTrend || [];
    return (
        <Card className="trend-card">
            <div className="card-heading">
                <div>
                    <span className="eyebrow">Net balance trend</span>
                    <strong className="card-amount">
                        {money(data?.totalBalance)}
                    </strong>
                    <small>30 days ending {shortDate(range.endDate)}</small>
                </div>
                <Pill
                    tone={Number(data?.netSavings) >= 0 ? 'success' : 'danger'}
                >
                    {Number(data?.netSavings) >= 0 ? (
                        <ArrowUpRight />
                    ) : (
                        <ArrowDownRight />
                    )}
                    {money(Math.abs(Number(data?.netSavings || 0)))} this period
                </Pill>
            </div>
            <div className="mini-chart" aria-label="Balance trend chart">
                {trend.length > 1 ? (
                    <ResponsiveContainer width="100%" height="100%">
                        <AreaChart data={trend}>
                            <defs>
                                <linearGradient
                                    id="balance-fill"
                                    x1="0"
                                    y1="0"
                                    x2="0"
                                    y2="1"
                                >
                                    <stop
                                        offset="0"
                                        stopColor="#168bff"
                                        stopOpacity=".25"
                                    />
                                    <stop
                                        offset="1"
                                        stopColor="#168bff"
                                        stopOpacity="0"
                                    />
                                </linearGradient>
                            </defs>
                            <CartesianGrid
                                stroke="var(--border)"
                                vertical={false}
                            />
                            <XAxis dataKey="date" hide />
                            <YAxis hide domain={['dataMin', 'dataMax']} />
                            <Tooltip
                                formatter={(value) => money(value)}
                                labelFormatter={shortDate}
                                contentStyle={{
                                    background: 'var(--surface)',
                                    border: '1px solid var(--border)',
                                    borderRadius: 12,
                                }}
                            />
                            <Area
                                type="monotone"
                                dataKey="value"
                                stroke="#168bff"
                                strokeWidth={2.4}
                                fill="url(#balance-fill)"
                            />
                        </AreaChart>
                    </ResponsiveContainer>
                ) : (
                    <div className="chart-empty">
                        <img
                            className="trend-empty-art"
                            src={staticAssetUrl('decor/decor_wave_01.png')}
                            alt=""
                            aria-hidden="true"
                        />
                        <span>Import more activity to reveal a trend.</span>
                    </div>
                )}
            </div>
        </Card>
    );
}

function AccountsCard({ accounts, theme }) {
    return (
        <Card className="accounts-card">
            <div className="card-heading">
                <span className="eyebrow">Your accounts</span>
                <Link to="/settings">Manage</Link>
            </div>
            <img
                className="accounts-landscape-art"
                src={staticAssetUrl(
                    `landscapes/landscape_house_trees${theme === 'dark' ? '_night' : ''}.png`
                )}
                alt=""
                aria-hidden="true"
            />
            <div className="account-list">
                {accounts.length === 0 && (
                    <p className="muted">Add an account to get started.</p>
                )}
                {accounts.slice(0, 5).map((account, index) => (
                    <div className="account-row" key={account.accountId}>
                        <span className={'account-dot account-' + (index % 5)}>
                            <Landmark />
                        </span>
                        <span>
                            <strong>{account.accountName}</strong>
                            <small>
                                {account.accountType === 'credit'
                                    ? 'Credit card debt'
                                    : `${money(account.bufferAmount)} protected`}
                            </small>
                        </span>
                        <AccountSafetyValues account={account} />
                    </div>
                ))}
            </div>
        </Card>
    );
}

function AccountSafetyValues({ account }) {
    if (account.accountType !== 'credit') {
        return (
            <span className="account-values">
                <strong>{money(account.balance)}</strong>
                <small>{money(account.safeToSpend)} safe</small>
            </span>
        );
    }

    const position =
        Number(account.debtBalance) > 0
            ? `${money(account.debtBalance)} owed`
            : Number(account.balance) > 0
              ? `${money(account.balance)} in credit`
              : 'Settled';
    return (
        <span className="account-values">
            <strong>{position}</strong>
            <small>
                {account.creditLimit
                    ? `${money(account.availableCredit)} available · ${percent(account.creditUtilizationPercent)} used`
                    : 'Limit not set'}
            </small>
        </span>
    );
}

function RecentTransactionsCard({ items }) {
    return (
        <Card className="recent-card">
            <div className="card-heading">
                <span className="eyebrow">Recent transactions</span>
                <Link to="/transactions">See all</Link>
            </div>
            <div className="transaction-list">
                {items.length === 0 && (
                    <p className="muted">No transactions yet.</p>
                )}
                {items.map((item) => (
                    <div className="transaction-row" key={item.id}>
                        <span
                            className={
                                'transaction-mark ' +
                                (isIncomeTransaction(item) ? 'income' : '')
                            }
                        >
                            {isIncomeTransaction(item) ? (
                                <ArrowDownRight />
                            ) : (
                                <WalletCards />
                            )}
                        </span>
                        <span>
                            <strong>
                                {item.payee || item.memo || 'Transaction'}
                            </strong>
                            <small>
                                {item.categoryName} · {relativeDate(item.date)}
                            </small>
                        </span>
                        <strong
                            className={
                                isIncomeTransaction(item) ? 'positive' : ''
                            }
                        >
                            {isIncomeTransaction(item) ? '+' : ''}
                            {money(item.amount)}
                        </strong>
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
            <div className="card-heading">
                <span className="eyebrow">Priority goal</span>
                <Link to="/goals">All goals</Link>
            </div>
            {!goal ? (
                <div className="goal-empty">
                    <Sparkles />
                    <strong>Give your money a destination</strong>
                    <p>Create a goal and Finova will calculate the path.</p>
                    <Link className="button secondary" to="/goals">
                        Create a goal
                    </Link>
                </div>
            ) : (
                <>
                    <GoalVisual
                        iconKey={goal.iconKey}
                        colorKey={goal.colorKey}
                        imageUrl={goal.imageUrl}
                        label={goal.name}
                    />
                    <div className="goal-card-title">
                        <div>
                            <strong>{goal.name}</strong>
                            <small>
                                {goal.description || goal.accountName}
                            </small>
                        </div>
                        <b>{percent(goal.progressPercent)}</b>
                    </div>
                    <Progress value={goal.progressPercent} />
                    <div className="goal-meta">
                        <span>
                            <strong>{money(goal.allocatedAmount)}</strong> of{' '}
                            {money(goal.targetAmount)}
                        </span>
                        <span>
                            <CalendarDays />{' '}
                            {goal.targetDate
                                ? shortDate(goal.targetDate)
                                : 'No target date'}
                        </span>
                    </div>
                </>
            )}
        </Card>
    );
}

function UpcomingExpenditureCard({ items }) {
    return (
        <Card className="upcoming-expenditure-card">
            <div className="card-heading">
                <span className="eyebrow">
                    <CalendarDays /> Coming up
                </span>
                <Link to="/plan">
                    View plan <ArrowRight />
                </Link>
            </div>
            {items.length === 0 ? (
                <p className="upcoming-empty">
                    No expected expenditure coming up.
                </p>
            ) : (
                <div className="upcoming-expenditure-grid">
                    {items.map((item) => (
                        <article
                            className="upcoming-expenditure-item"
                            key={item.id}
                        >
                            <span className="upcoming-expenditure-date">
                                <strong>{shortDate(item.dueDate)}</strong>
                                <small>{relativeDate(item.dueDate)}</small>
                            </span>
                            <span>
                                <strong>{item.itemName}</strong>
                                <small>{item.accountName}</small>
                            </span>
                            <strong className="upcoming-expenditure-amount">
                                −{money(item.expectedAmount)}
                            </strong>
                        </article>
                    ))}
                </div>
            )}
        </Card>
    );
}

function SnapshotCard({ data, warnings }) {
    const savingRate = Number(data?.savingsRate || 0);
    return (
        <Card className="snapshot-card">
            <div className="card-heading">
                <span className="eyebrow">This month</span>
                <Link to="/insights">
                    Explore <ArrowRight />
                </Link>
            </div>
            <div
                className="snapshot-ring"
                style={{
                    '--progress': Math.max(0, Math.min(100, savingRate)) + '%',
                }}
            >
                <span>
                    <strong>{percent(savingRate)}</strong>
                    <small>saved</small>
                </span>
            </div>
            <div className="snapshot-stats">
                <span>
                    <small>Income</small>
                    <strong className="positive">{money(data?.income)}</strong>
                </span>
                <span>
                    <small>Spending</small>
                    <strong>{money(data?.spending)}</strong>
                </span>
            </div>
            {warnings.length > 0 ? (
                <div className="insight-callout warning">
                    <span>!</span>
                    <p>
                        <strong>Watch {warnings[0].categoryName}</strong>
                        <br />
                        {percent(warnings[0].progressPercent)} of this month’s
                        plan is used.
                    </p>
                </div>
            ) : (
                <div className="insight-callout">
                    <Sparkles />
                    <p>
                        <strong>Your plan looks steady</strong>
                        <br />
                        Finova will highlight meaningful changes here.
                    </p>
                </div>
            )}
        </Card>
    );
}
