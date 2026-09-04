import { createElement } from 'react';
import {
    ArrowRight,
    CalendarClock,
    CircleHelp,
    FileUp,
    Landmark,
    Scale,
    Settings2,
    ShieldCheck,
    Target,
    Wrench,
} from 'lucide-react';
import { Link } from 'react-router-dom';
import { Card } from '../components/ui';

function HelpCard({ icon: Icon, eyebrow, title, children }) {
    return (
        <Card className="help-card">
            <div className="help-card-heading">
                <span className="help-icon">{createElement(Icon)}</span>
                <div>
                    <span className="eyebrow">{eyebrow}</span>
                    <h2>{title}</h2>
                </div>
            </div>
            <div className="help-card-copy">{children}</div>
        </Card>
    );
}

function HelpLink({ to, children }) {
    return (
        <Link to={to}>
            {children}
            <ArrowRight />
        </Link>
    );
}

export default function HelpPage() {
    return (
        <div className="page-stack help-page">
            <section className="help-intro card">
                <span className="eyebrow">
                    <CircleHelp /> Finova guide
                </span>
                <h2>A calmer way to understand your household money.</h2>
                <p>
                    Finova is a private household workspace. It organises the
                    balances and transactions you provide, helps you plan what
                    is next, and never connects to a bank or moves money.
                </p>
            </section>

            <div className="help-grid">
                <HelpCard
                    icon={Landmark}
                    eyebrow="Start here"
                    title="Set up your household"
                >
                    <p>
                        Complete enrollment with your name and household name,
                        then open Settings to add each account. Enter the
                        opening balance and account type that match your
                        starting point.
                    </p>
                    <HelpLink to="/settings">Open settings</HelpLink>
                </HelpCard>

                <HelpCard
                    icon={FileUp}
                    eyebrow="Transactions"
                    title="Import and review activity"
                >
                    <p>
                        Use Transactions to preview and review an import before
                        committing it. Finova accepts OFX, QIF, and selectable-
                        text Halifax PDF statements, and can export your data as
                        CSV.
                    </p>
                    <p className="help-note">
                        Image-only PDF scans need OCR first. Finova rejects them
                        instead of guessing at incomplete financial data.
                    </p>
                    <HelpLink to="/transactions">Open transactions</HelpLink>
                </HelpCard>

                <HelpCard
                    icon={ShieldCheck}
                    eyebrow="Overview"
                    title="Read safe to spend"
                >
                    <p>
                        Overview shows what is available after each account’s
                        protected buffer and confirmed near-term bills. It is a
                        planning view based on your opening values and imported
                        activity, not a live bank balance.
                    </p>
                    <p className="help-note">
                        Keep account opening values and imports current when you
                        want the clearest picture.
                    </p>
                    <HelpLink to="/">Open overview</HelpLink>
                </HelpCard>

                <HelpCard
                    icon={CalendarClock}
                    eyebrow="Plan"
                    title="Plan bills, paydays, and budgets"
                >
                    <p>
                        Plan keeps account safety floors and buffers visible,
                        tracks recurring bills and paydays, and highlights
                        transaction patterns that may become recurring rules.
                    </p>
                    <p>
                        Monthly category budgets can use positive rollover.
                        Finova does not create rollover debt, and only unmatched
                        confirmed occurrences affect safe to spend.
                    </p>
                    <HelpLink to="/plan">Open plan</HelpLink>
                </HelpCard>

                <HelpCard
                    icon={Target}
                    eyebrow="Goals"
                    title="Give savings a destination"
                >
                    <p>
                        Create goals with an amount, date, and account. Reorder
                        active goals to set their priority; Finova uses that
                        order for account-backed waterfall allocation and
                        progress calculations.
                    </p>
                    <p className="help-note">
                        Goals describe a path for your money. Finova never
                        transfers funds automatically.
                    </p>
                    <HelpLink to="/goals">Open goals</HelpLink>
                </HelpCard>

                <HelpCard
                    icon={Scale}
                    eyebrow="Statement control"
                    title="Reconcile an account"
                >
                    <p>
                        Start a reconciliation session with the statement dates
                        and opening and closing balances. Clear matching ledger
                        transactions, then resolve any remaining discrepancy
                        with a documented adjustment before closing the session.
                    </p>
                    <HelpLink to="/reconciliation">
                        Open reconciliation
                    </HelpLink>
                </HelpCard>

                <HelpCard
                    icon={Settings2}
                    eyebrow="Make it yours"
                    title="Settings, privacy, and portability"
                >
                    <p>
                        Settings controls your profile, household name,
                        currency, locale, timezone, theme, accounts, categories,
                        and automatic category rules. Archived records remain
                        available where appropriate without changing history.
                    </p>
                    <p>
                        Finova is intended for a trusted private network. Use
                        the portability tools in Settings to export or restore
                        the household archive, including private goal images.
                    </p>
                    <HelpLink to="/settings">Open settings</HelpLink>
                </HelpCard>

                <HelpCard
                    icon={Wrench}
                    eyebrow="Troubleshooting"
                    title="When something does not look right"
                >
                    <ul>
                        <li>
                            Use the page’s retry action for a temporary loading
                            or API error.
                        </li>
                        <li>
                            Check that the development or production API
                            readiness endpoint is healthy if the whole app will
                            not start.
                        </li>
                        <li>
                            For imports, confirm the file is one of the
                            supported formats and contains selectable text.
                        </li>
                        <li>
                            Recheck account opening values, buffers, confirmed
                            occurrences, and import history when totals differ.
                        </li>
                    </ul>
                </HelpCard>
            </div>
        </div>
    );
}
