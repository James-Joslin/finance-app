import { createElement, useMemo, useState } from 'react';
import { ArrowDownRight, ArrowUpRight, CircleAlert, PiggyBank, Sparkles, WalletCards } from 'lucide-react';
import {
    Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, Legend, Pie, PieChart,
    ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import { Card, PageState, Pill, Progress } from '../components/ui';
import { apiError, compactMoney, money, percent, shortDate } from '../lib/format';
import { useInsights } from '../lib/queries';

const palette = ['#168bff', '#2fcdb0', '#f3b653', '#7b72ee', '#ef7898', '#57bad1', '#8ba2b8'];

export default function InsightsPage() {
    const [range, setRange] = useState('month');
    const dates = useMemo(() => rangeDates(range), [range]);
    const insights = useInsights(dates);
    const data = normaliseInsights(insights.data);

    return (
        <div className="page-stack">
            <div className="page-toolbar">
                <div className="segmented">{[['month', 'This month'], ['last', 'Last month'], ['year', 'This year']].map(([key, label]) => <button key={key} className={range === key ? 'active' : ''} onClick={() => setRange(key)}>{label}</button>)}</div>
                <Pill>{shortDate(dates.startDate)} – {shortDate(dates.endDate)}</Pill>
            </div>
            <PageState loading={insights.isLoading} error={insights.error && apiError(insights.error)}>
                <div className="insights-grid">
                    <Card className="insight-trend">
                        <div className="card-heading"><div><span className="eyebrow">Net balance trend</span><strong className="card-amount">{money(data?.totalBalance)}</strong></div><Pill tone={data?.netSavings >= 0 ? 'success' : 'danger'}>{data?.netSavings >= 0 ? <ArrowUpRight /> : <ArrowDownRight />}{money(Math.abs(data?.netSavings || 0))}</Pill></div>
                        <ChartFrame empty={!data?.balanceTrend?.length}><ResponsiveContainer width="100%" height="100%"><AreaChart data={data?.balanceTrend || []}><defs><linearGradient id="insight-balance" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#168bff" stopOpacity=".28" /><stop offset="1" stopColor="#168bff" stopOpacity="0" /></linearGradient></defs><CartesianGrid stroke="var(--border)" vertical={false} /><XAxis dataKey="date" tickFormatter={(value) => shortDate(value).replace(/ \d{4}$/, '')} tick={{ fill: 'var(--text-muted)', fontSize: 11 }} axisLine={false} tickLine={false} /><YAxis tickFormatter={(value) => compactMoney(value)} tick={{ fill: 'var(--text-muted)', fontSize: 11 }} axisLine={false} tickLine={false} /><Tooltip formatter={(value) => money(value)} contentStyle={tooltipStyle} /><Area type="monotone" dataKey="value" stroke="#168bff" strokeWidth={2.4} fill="url(#insight-balance)" /></AreaChart></ResponsiveContainer></ChartFrame>
                    </Card>

                    <Card className="spending-chart">
                        <div className="card-heading"><span className="eyebrow">Spending breakdown</span><strong>{money(data?.spending)}</strong></div>
                        <ChartFrame empty={!data?.categorySpending?.length}><ResponsiveContainer width="100%" height="100%"><PieChart><Pie data={data?.categorySpending || []} dataKey="amount" nameKey="name" innerRadius="58%" outerRadius="82%" paddingAngle={2}>{(data?.categorySpending || []).map((item, index) => <Cell key={item.name} fill={palette[index % palette.length]} />)}</Pie><Tooltip formatter={(value) => money(value)} contentStyle={tooltipStyle} /></PieChart></ResponsiveContainer><div className="donut-label"><strong>{money(data?.spending)}</strong><small>Total spent</small></div></ChartFrame>
                        <div className="legend-list">{(data?.categorySpending || []).slice(0, 5).map((item, index) => <span key={item.name}><i style={{ background: palette[index % palette.length] }} /><small>{item.name}</small><strong>{percent(item.percent)}</strong></span>)}</div>
                    </Card>

                    <Card className="summary-card">
                        <span className="eyebrow">Summary</span>
                        <Metric icon={ArrowDownRight} label="Total income" value={money(data?.income)} tone="positive" />
                        <Metric icon={ArrowUpRight} label="Total spending" value={money(data?.spending)} />
                        <Metric icon={WalletCards} label="Net savings" value={money(data?.netSavings)} tone={data?.netSavings >= 0 ? 'positive' : 'negative'} />
                        <Metric icon={PiggyBank} label="Savings rate" value={percent(data?.savingsRate)} tone="positive" />
                    </Card>

                    <Card className="cashflow-chart">
                        <div className="card-heading"><div><span className="eyebrow">Cash-flow rhythm</span><h3>Income and spending</h3></div></div>
                        <ChartFrame empty={!data?.incomeTrend?.length && !data?.spendingTrend?.length}><ResponsiveContainer width="100%" height="100%"><BarChart data={mergeTrends(data?.incomeTrend, data?.spendingTrend)}><CartesianGrid stroke="var(--border)" vertical={false} /><XAxis dataKey="date" hide /><YAxis tickFormatter={(value) => compactMoney(value)} tick={{ fill: 'var(--text-muted)', fontSize: 11 }} axisLine={false} tickLine={false} /><Tooltip formatter={(value) => money(value)} contentStyle={tooltipStyle} /><Legend /><Bar name="Income" dataKey="income" fill="#2fcdb0" radius={[5, 5, 0, 0]} /><Bar name="Spending" dataKey="spending" fill="#168bff" radius={[5, 5, 0, 0]} /></BarChart></ResponsiveContainer></ChartFrame>
                    </Card>

                    <Card className="insights-for-you">
                        <span className="eyebrow">Insights for you</span>
                        <Insight icon={data?.savingsRate >= 20 ? Sparkles : CircleAlert} title={data?.savingsRate >= 20 ? 'Strong savings rhythm' : 'A little more room'} copy={'Your savings rate is ' + percent(data?.savingsRate) + ' for this period.'} tone={data?.savingsRate >= 20 ? 'success' : 'warning'} />
                        <Insight icon={PiggyBank} title="Goal progress" copy={'Your active goals are ' + percent(data?.goalProgressPercent) + ' funded cumulatively.'} tone="info" />
                        {data?.uncategorisedSpending > 0 && <Insight icon={CircleAlert} title="Tidy uncategorised spending" copy={money(data.uncategorisedSpending) + ' is waiting to be categorised.'} tone="warning" />}
                    </Card>

                    <Card className="goal-insight-card">
                        <span className="eyebrow">Savings goals</span>
                        <div className="goal-insight-value"><PiggyBank /><strong>{percent(data?.goalProgressPercent)}</strong></div>
                        <Progress value={data?.goalProgressPercent} />
                        <p>Cumulative progress across every active household target.</p>
                    </Card>
                </div>
            </PageState>
        </div>
    );
}

const tooltipStyle = { background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 12, color: 'var(--text)' };

function ChartFrame({ empty, children }) {
    return <div className="chart-frame">{empty ? <div className="chart-empty">More transaction history will reveal this pattern.</div> : children}</div>;
}

function Metric({ icon, label, value, tone = '' }) {
    return <div className="metric-row"><span className={'metric-icon ' + tone}>{createElement(icon)}</span><span>{label}</span><strong className={tone}>{value}</strong></div>;
}

function Insight({ icon, title, copy, tone }) {
    return <div className={'insight-row ' + tone}><span>{createElement(icon)}</span><p><strong>{title}</strong><br />{copy}</p></div>;
}

function rangeDates(range) {
    const now = new Date();
    let start;
    let end;
    if (range === 'last') {
        start = new Date(now.getFullYear(), now.getMonth() - 1, 1);
        end = new Date(now.getFullYear(), now.getMonth(), 0);
    } else if (range === 'year') {
        start = new Date(now.getFullYear(), 0, 1);
        end = now;
    } else {
        start = new Date(now.getFullYear(), now.getMonth(), 1);
        end = now;
    }
    return { startDate: localIso(start), endDate: localIso(end) };
}

function localIso(date) {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return year + '-' + month + '-' + day;
}

function mergeTrends(income = [], spending = []) {
    const rows = new Map();
    asArray(income).forEach((item) => rows.set(item.date, { date: item.date, income: item.value, spending: 0 }));
    asArray(spending).forEach((item) => rows.set(item.date, { ...(rows.get(item.date) || { date: item.date, income: 0 }), spending: item.value }));
    return [...rows.values()].sort((a, b) => String(a.date).localeCompare(String(b.date)));
}

function normaliseInsights(value) {
    const source = value && typeof value === 'object' ? value : {};
    return {
        ...source,
        totalBalance: finiteNumber(source.totalBalance),
        income: finiteNumber(source.income),
        spending: finiteNumber(source.spending),
        netSavings: finiteNumber(source.netSavings),
        savingsRate: finiteNumber(source.savingsRate),
        goalProgressPercent: finiteNumber(source.goalProgressPercent),
        uncategorisedSpending: finiteNumber(source.uncategorisedSpending),
        balanceTrend: asArray(source.balanceTrend),
        categorySpending: asArray(source.categorySpending),
        incomeTrend: asArray(source.incomeTrend),
        spendingTrend: asArray(source.spendingTrend),
    };
}

function asArray(value) { return Array.isArray(value) ? value : []; }
function finiteNumber(value) { return Number.isFinite(Number(value)) ? Number(value) : 0; }
